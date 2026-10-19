#include "Eac3HdmiHost.h"
#ifdef _WIN32
#include "Eac3BurstReader.h"
#include "Eac3CarrierClock.h"
#include "WasapiEac3Output.h"
#include "RpcOutputWriter.h"
#include "../../audio-engine/third_party/nlohmann_json.hpp"
#include <windows.h>
#include <avrt.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <stdexcept>
#include <condition_variable>
#include <cstdio>
#include <deque>
#include <iostream>
#include <mutex>
#include <thread>
extern "C" {
#include <libavutil/log.h>
}

namespace {
using Json = nlohmann::json;
using Clock = std::chrono::steady_clock;
class JsonOutput {
public:
    void send(const Json& message) {
        const bool status = message.value("method", std::string()) == "hdmi.status";
        writer_.enqueue(message.dump(), status ? RpcOutputWriter::MessageClass::Position
            : RpcOutputWriter::MessageClass::Reliable);
    }
    bool failed() const { return writer_.failed(); }
private:
    // Existing bounded writer coalesces status so a stalled UI cannot block
    // device feeding on a full stdout pipe. Control responses remain reliable.
    RpcOutputWriter writer_ { 1, {}, 32 };
};
std::string errorCode(const std::exception& error) {
    const std::string code = error.what();
    return code.starts_with("eac3_hdmi_") && code.size() <= 96 ? code : "eac3_hdmi_host_failed";
}

class Playback {
public:
    Json dispatch(const std::string& method, const Json& params) {
        if (method == "rpc.ping") return {{"eac3HdmiV1", true}};
        if (method == "hdmi.open") {
            stop();
            const auto path = params.at("filePath").get<std::string>();
            if (path.size() > 32768) throw std::runtime_error("eac3_hdmi_path_exceeds_limit");
            // Exclude mapped remote drives before any demuxer/file access.
            if (path.size() < 3 || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
                throw std::runtime_error("eac3_hdmi_local_file_required");
            const std::wstring root { static_cast<wchar_t>(path[0]), L':', L'\\' };
            if (GetDriveTypeW(root.c_str()) != DRIVE_FIXED && GetDriveTypeW(root.c_str()) != DRIVE_REMOVABLE)
                throw std::runtime_error("eac3_hdmi_local_file_required");
            info_ = reader_.open(path);
            double start = params.value("startSeconds", 0.0);
            if (!std::isfinite(start) || start < 0 || start > 7 * 24 * 3600)
                throw std::runtime_error("eac3_hdmi_invalid_seek");
            if (info_.durationSeconds > 0)
                start = std::min(start, std::max(0.0, info_.durationSeconds - 1536.0 / 48000));
            if (start > 0) reader_.seek(start);
            output_.open(params.value("deviceId", std::string()), params.value("deviceName", std::string()), info_.channels);
            deviceOpen_ = true;
            name_ = output_.deviceName();
            anchor_ = start;
            prime();
            paused_ = params.value("startPaused", false);
            state_ = paused_ ? "paused" : "playing";
            if (!paused_) output_.start();
            lastProgress_ = Clock::now();
            return snapshot();
        }
        if (method == "hdmi.stop" || method == "host.shutdown") { stop(); return snapshot(); }
        if (method == "hdmi.status") return snapshot();
        if (!deviceOpen_) throw std::runtime_error("eac3_hdmi_output_not_open");
        if (method == "hdmi.pause") {
            output_.pause(); paused_ = true; state_ = "paused";
        } else if (method == "hdmi.resume") {
            if (paused_) output_.start();
            paused_ = false; state_ = "playing"; lastProgress_ = Clock::now();
        } else if (method == "hdmi.seek") {
            double target = params.at("positionSeconds").get<double>();
            if (!std::isfinite(target) || target < 0 || target > 7 * 24 * 3600)
                throw std::runtime_error("eac3_hdmi_invalid_seek");
            if (info_.durationSeconds > 0)
                target = std::min(target, std::max(0.0, info_.durationSeconds - 1536.0 / 48000));
            output_.reset();
            reader_.seek(target);
            anchor_ = target; submitted_ = 0; offset_ = burst_.size(); eof_ = false; lastElapsed_ = 0;
            prime();
            if (!paused_) output_.start();
            lastProgress_ = Clock::now();
        } else throw std::runtime_error("eac3_hdmi_method_unsupported");
        return snapshot();
    }
    void tick() {
        if (!deviceOpen_ || paused_) return;
        prime();
        const double elapsed = output_.elapsedSeconds();
        if (elapsed > lastElapsed_ + 0.0001) { lastProgress_ = Clock::now(); lastElapsed_ = elapsed; }
        // Both endpoint padding AND the native device clock must confirm the
        // encoded tail has drained. Demuxer EOF alone never ends playback.
        if (echo::eac3CarrierDrained(eof_, output_.paddingFrames(), submitted_, elapsed)) {
            position_ = anchor_ + submitted_ / 192000.0;
            info_.durationSeconds = position_; // Replace raw-file duration estimates with the drained native timeline.
            output_.close(); reader_.close(); deviceOpen_ = false;
            state_ = "ended";
        } else if (Clock::now() - lastProgress_ > std::chrono::seconds(3)) {
            throw std::runtime_error("eac3_hdmi_device_clock_stalled");
        }
    }
    Json snapshot() {
        const auto padding = deviceOpen_ ? output_.paddingFrames() : 0;
        if (deviceOpen_) position_ = anchor_ + std::min(output_.elapsedSeconds(), submitted_ / 192000.0);
        return {{"state", state_}, {"positionSeconds", position_}, {"durationSeconds", info_.durationSeconds},
            {"channels", info_.channels}, {"sourceAtmosSignalled", info_.atmosSignalled},
            {"sourceSampleRate", 48000}, {"carrierSampleRate", 192000}, {"bufferedCarrierFrames", padding},
            {"deviceName", name_}, {"outputReady", deviceOpen_}, {"format", "iec61937-eac3"}, {"error", error_}};
    }
    void fail(const std::exception& error) {
        stop(); state_ = "error"; error_ = errorCode(error);
    }
    void stop() {
        output_.close(); reader_.close(); deviceOpen_ = false;
        submitted_ = 0; offset_ = burst_.size(); eof_ = paused_ = false;
        state_ = "stopped"; error_.clear(); lastElapsed_ = 0;
    }
private:
    void prime() {
        auto available = output_.availableFrames();
        // At most the endpoint's <= 1-second buffer is filled; only one encoded
        // burst is retained between calls. No PCM/DSP/queue cache is created.
        while (available > 0 && !eof_) {
            if (offset_ == burst_.size()) {
                if (!reader_.next(burst_)) { eof_ = true; break; }
                if (submitted_ == 0) anchor_ = reader_.positionSeconds() - 1536.0 / 48000;
                offset_ = 0;
            }
            const auto frames = std::min<uint32_t>(available, static_cast<uint32_t>((burst_.size() - offset_) / 4));
            output_.write(burst_.data() + offset_, frames);
            offset_ += frames * 4; submitted_ += frames; available -= frames;
        }
        if (submitted_ == 0) throw std::runtime_error("eac3_hdmi_empty_source");
    }
    echo::Eac3BurstReader reader_;
    echo::WasapiEac3Output output_;
    echo::Eac3SourceInfo info_;
    std::array<uint8_t, echo::eac3BurstBytes> burst_ {};
    std::size_t offset_ = burst_.size();
    uint64_t submitted_ = 0;
    double anchor_ = 0, position_ = 0, lastElapsed_ = 0;
    bool eof_ = false, paused_ = false, deviceOpen_ = false;
    std::string state_ = "stopped", name_, error_;
    Clock::time_point lastProgress_ = Clock::now();
};
}

int runEac3HdmiHost() {
    av_log_set_level(AV_LOG_QUIET); // Never print source paths or metadata.
    JsonOutput output;
    std::mutex mutex;
    std::condition_variable wake;
    std::deque<Json> commands;
    bool inputClosed = false;
    std::thread worker([&] {
        const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        DWORD taskIndex = 0;
        const HANDLE audioTask = AvSetMmThreadCharacteristicsW(L"Audio", &taskIndex);
        Playback playback;
        auto nextStatus = Clock::now();
        bool running = true;
        while (running) {
            if (output.failed()) break;
            Json command;
            {
                std::unique_lock lock(mutex);
                wake.wait_for(lock, std::chrono::milliseconds(5), [&] { return inputClosed || !commands.empty(); });
                if (!commands.empty()) { command = std::move(commands.front()); commands.pop_front(); }
                else if (inputClosed) break;
            }
            if (!command.is_null()) {
                const auto id = command.value("id", Json());
                try {
                    if (FAILED(com)) throw std::runtime_error("eac3_hdmi_com_initialize_failed");
                    const auto method = command.at("method").get<std::string>();
                    const auto result = playback.dispatch(method, command.value("params", Json::object()));
                    if (!id.is_null()) output.send({{"jsonrpc", "2.0"}, {"id", id}, {"result", result}});
                    running = method != "host.shutdown";
                } catch (const std::exception& error) {
                    playback.fail(error);
                    if (!id.is_null()) output.send({{"jsonrpc", "2.0"}, {"id", id}, {"error", {{"code", -32000}, {"message", errorCode(error)}}}});
                }
                nextStatus = Clock::now();
            }
            try { playback.tick(); }
            catch (const std::exception& error) { playback.fail(error); nextStatus = Clock::now(); }
            if (Clock::now() >= nextStatus) {
                try { output.send({{"jsonrpc", "2.0"}, {"method", "hdmi.status"}, {"params", playback.snapshot()}}); }
                catch (const std::exception& error) { playback.fail(error); }
                nextStatus = Clock::now() + std::chrono::milliseconds(100);
            }
        }
        playback.stop();
        if (audioTask) AvRevertMmThreadCharacteristics(audioTask);
        if (SUCCEEDED(com)) CoUninitialize();
    });
    // Bounded control lines/queue. Source paths arrive only on this private pipe.
    std::array<char, 65536> line {};
    while (std::fgets(line.data(), static_cast<int>(line.size()), stdin)) {
        const std::string text(line.data());
        if (text.empty() || text.back() != '\n') break;
        const Json request = Json::parse(text, nullptr, false);
        if (request.is_discarded() || !request.is_object() || request.value("jsonrpc", std::string()) != "2.0") break;
        const bool shutdown = request.value("method", std::string()) == "host.shutdown";
        {
            const std::lock_guard lock(mutex);
            if (commands.size() >= 16) {
                output.send({{"jsonrpc", "2.0"}, {"id", request.value("id", Json())},
                    {"error", {{"code", -32000}, {"message", "eac3_hdmi_control_busy"}}}});
            } else commands.push_back(request);
        }
        wake.notify_one();
        if (shutdown) break;
    }
    { const std::lock_guard lock(mutex); inputClosed = true; }
    wake.notify_one(); worker.join();
    return 0;
}
#else
int runEac3HdmiHost() { return 1; }
#endif
