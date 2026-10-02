#include "WindowsCddaReader.h"
#include "LinuxCddaDevice.h"
#include "CddaReadPolicy.h"

#include <cmath>
#include <cstdint>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

void runCddaSignalIntegrityTests();

namespace
{
constexpr std::uint32_t rawSectorBytes = 2352;

struct SimulatedDiscState
{
    std::vector<std::pair<std::uint32_t, std::uint32_t>> reads;
    std::uint32_t maximumBatchSectors = 16;
    int transientFailuresRemaining = 0;
    bool allAudio = false;
    double timeMs = 0;
    double readDurationMs = 0;
    bool shortReads = false;
};

class SimulatedCddaDevice final : public CddaDevice
{
public:
    explicit SimulatedCddaDevice(std::shared_ptr<SimulatedDiscState> state) : state(std::move(state)) {}

    std::vector<CddaTrackInfo> readToc() override
    {
        return {
            {1, 0, 18, true},
            {2, 18, 24, state->allAudio},
            {3, 24, 25, true},
        };
    }

    std::vector<std::uint8_t> readAudioSectors(
        std::uint32_t startLba,
        std::uint32_t sectorCount) override
    {
        state->reads.emplace_back(startLba, sectorCount);
        state->timeMs += state->readDurationMs;
        if (state->transientFailuresRemaining > 0)
        {
            --state->transientFailuresRemaining;
            throw std::runtime_error("audio_cd_simulated_transient_read_failure");
        }
        if (sectorCount > state->maximumBatchSectors)
            throw std::runtime_error("audio_cd_simulated_batch_too_large");
        std::vector<std::uint8_t> raw(static_cast<std::size_t>(sectorCount) * rawSectorBytes);
        if (state->shortReads) { raw.resize(raw.size() - 4); return raw; }
        for (std::size_t index = 0; index < raw.size(); index += 4)
        {
            raw[index] = 0x00;
            raw[index + 1] = 0x80;
            raw[index + 2] = 0xff;
            raw[index + 3] = 0x7f;
        }
        return raw;
    }

private:
    std::shared_ptr<SimulatedDiscState> state;
};

void require(bool condition, const std::string& message)
{
    if (! condition)
        throw std::runtime_error(message);
}

template <typename Callback>
void requireThrows(Callback&& callback, const std::string& expected)
{
    try
    {
        callback();
    }
    catch (const std::exception& error)
    {
        require(std::string(error.what()) == expected, "unexpected error: " + std::string(error.what()));
        return;
    }
    throw std::runtime_error("expected error: " + expected);
}
} // namespace

int main()
{
    try
    {
        const auto state = std::make_shared<SimulatedDiscState>();
        WindowsCddaReader reader([state] (const std::string& drive)
        {
            require(drive == "D:", "simulated drive id was not forwarded");
            return std::make_unique<SimulatedCddaDevice>(state);
        });

        const auto tracks = reader.inspectDisc("D:");
        require(tracks.size() == 3, "simulated TOC track count mismatch");
        require(tracks[0].audio && ! tracks[1].audio && tracks[2].audio,
            "simulated mixed-mode track flags mismatch");

        std::vector<float> pcm;
        reader.streamTrackTo("D:", 1, [&pcm] (const std::vector<float>& chunk)
        {
            pcm.insert(pcm.end(), chunk.begin(), chunk.end());
        });
        require(state->reads == std::vector<std::pair<std::uint32_t, std::uint32_t>>({{0, 16}, {15, 3}, {17, 1}}),
            "CDDA reader did not preserve bounded sector batches");
        require(pcm.size() == 18u * rawSectorBytes / 2u, "PCM sample count mismatch");
        require(std::abs(pcm[0] + 1.0f) < 0.000001f, "negative PCM sample conversion mismatch");
        require(std::abs(pcm[1] - (32767.0f / 32768.0f)) < 0.000001f,
            "positive PCM sample conversion mismatch");

        const auto limitedState = std::make_shared<SimulatedDiscState>();
        limitedState->maximumBatchSectors = 4;
        WindowsCddaReader limitedReader([limitedState] (const std::string&)
        {
            return std::make_unique<SimulatedCddaDevice>(limitedState);
        });
        std::size_t limitedSampleCount = 0;
        limitedReader.streamTrackTo("D:", 1, [&limitedSampleCount] (const std::vector<float>& chunk)
        {
            limitedSampleCount += chunk.size();
        });
        require(limitedSampleCount == 18u * rawSectorBytes / 2u,
            "adaptive batch fallback lost PCM samples");
        require(limitedState->reads == std::vector<std::pair<std::uint32_t, std::uint32_t>>({
            {0, 16}, {0, 16}, {0, 8}, {0, 8}, {0, 4}, {3, 4}, {6, 4}, {9, 4}, {12, 4}, {15, 3}, {17, 1},
        }), "adaptive batch fallback did not retain the discovered drive limit");

        const auto transientState = std::make_shared<SimulatedDiscState>();
        transientState->transientFailuresRemaining = 1;
        WindowsCddaReader transientReader([transientState] (const std::string&)
        {
            return std::make_unique<SimulatedCddaDevice>(transientState);
        });
        transientReader.streamTrackTo("D:", 1, [] (const std::vector<float>&) {});
        require(transientState->reads == std::vector<std::pair<std::uint32_t, std::uint32_t>>({
            {0, 16}, {0, 16}, {15, 3}, {17, 1},
        }), "transient sector failure was not retried in place");

        const auto slowState = std::make_shared<SimulatedDiscState>();
        slowState->readDurationMs = 3000;
        slowState->transientFailuresRemaining = 8;
        WindowsCddaReader slowReader([slowState](const auto&) {
            return std::make_unique<SimulatedCddaDevice>(slowState);
        }, [slowState] { return slowState->timeMs; });
        requireThrows([&] { slowReader.streamTrackTo("D:", 1, [](const auto&) {}); },
            "audio_cd_read_retry_timeout");
        require(slowState->reads.size() == 2, "retry deadline restarted during batch reduction");

        const auto shortState = std::make_shared<SimulatedDiscState>();
        shortState->shortReads = true;
        WindowsCddaReader shortReader([shortState](const auto&) {
            return std::make_unique<SimulatedCddaDevice>(shortState);
        });
        std::size_t shortSamples = 0;
        requireThrows([&] { shortReader.streamTrackTo("D:", 1,
            [&shortSamples](const auto& chunk) { shortSamples += chunk.size(); }); }, "audio_cd_short_raw_read");
        require(shortSamples == 0 && shortState->reads.size() == 10, "short reads leaked PCM or retried indefinitely");

        double policyTime = 0;
        CddaReadPolicy policy([&] { return policyTime; });
        auto started = policy.beforeRead();
        policyTime = 500;
        policy.afterRead(started, 16, false);
        policy.reduceBatch();
        started = policy.beforeRead();
        policyTime = 750;
        policy.afterRead(started, 8, true);
        const auto stats = policy.snapshot();
        require(stats.readCalls == 2 && stats.retries == 1 && stats.sectorsRead == 8
            && stats.batchReductions == 1 && stats.totalReadMs == 750 && stats.maxReadMs == 500,
            "CDDA cumulative read diagnostics mismatch");
        policyTime = 9000; // stdout backpressure must not spend the next read's retry budget.
        started = policy.beforeRead();
        policy.afterRead(started, 8, true);
        started = policy.beforeRead(900, 4);
        policy.recordError(CddaReadError("audio_cd_raw_read_failed", CddaReadFault::Io, 1117));
        policy.afterRead(started, 4, false);
        policyTime += 5000;
        requireThrows([&] { (void)policy.beforeRead(900, 2); }, "audio_cd_read_retry_timeout");
        require(policy.snapshot().retryDeadlineExceeded && policy.snapshot().nativeError == 1117
            && policy.snapshot().failureLba == 900 && policy.snapshot().failureSectorCount == 4,
            "retry deadline replaced the actual hardware failure evidence");

        validateLinuxCddaTransfer(0, 0, 0, 0);
        requireThrows([] { validateLinuxCddaTransfer(0, 0, 0, 2352); }, "audio_cd_short_raw_read");
        requireThrows([] { validateLinuxCddaTransfer(0, 0, 0, -1); }, "audio_cd_short_raw_read");
        requireThrows([] { validateLinuxCddaTransfer(0, 0, 8, 0); }, "audio_cd_raw_read_failed_linux_scsi");
        requireThrows([] { validateLinuxCddaTransfer(2, 0, 0, 0); }, "audio_cd_raw_read_failed_linux_scsi");

        state->reads.clear();
        reader.streamTrackTo("D:", 1, [] (const auto&) {}, 16.0 / 75.0);
        require(state->reads == std::vector<std::pair<std::uint32_t, std::uint32_t>>({{16, 2}, {17, 1}}),
                "seek did not begin at the requested CD sector");
        requireThrows([&] { reader.streamTrackTo("D:", 1, [] (const auto&) {}, -1); },
            "audio_cd_invalid_position");

        requireThrows([&] { reader.streamTrackTo("D:", 1, [] (const auto&) {}, 0, 0, 25, 3); },
            "audio_cd_invalid_sequence");
        const auto sequenceState = std::make_shared<SimulatedDiscState>();
        sequenceState->allAudio = true;
        WindowsCddaReader sequenceReader([sequenceState](const std::string&) {
            return std::make_unique<SimulatedCddaDevice>(sequenceState);
        });
        std::size_t samples = 0;
        sequenceReader.streamTrackTo("D:", 1, [&samples](const auto& chunk) { samples += chunk.size(); }, 0, 0, 25, 3);
        require(samples == 25u * 2352u / 2u, "continuous CD sequence lost or added samples");
        require(sequenceState->reads == std::vector<std::pair<std::uint32_t, std::uint32_t>>({{0, 16}, {15, 10}, {24, 1}}),
                "continuous sequence reopened at a track boundary");

        requireThrows([&] { reader.streamTrackTo("D:", 2, [] (const auto&) {}); },
            "audio_cd_track_is_data");
        requireThrows([&] { reader.streamTrackTo("D:", 99, [] (const auto&) {}); },
            "audio_cd_track_not_found");

        require(isAllowedLinuxCddaDevicePath("/dev/sr0"), "sr0 must be an allowed Linux CDDA path");
        require(isAllowedLinuxCddaDevicePath("/dev/sr15"), "sr15 must be an allowed Linux CDDA path");
        require(isAllowedLinuxCddaDevicePath("/dev/cdrom"), "cdrom symlink must be allowed");
        require(! isAllowedLinuxCddaDevicePath("/dev/sda"), "disks must not be treated as optical");
        require(! isAllowedLinuxCddaDevicePath("/dev/sr0/../sda"), "path traversal must be rejected");
        require(! isAllowedLinuxCddaDevicePath("/tmp/sr0"), "non-dev paths must be rejected");

        runCddaSignalIntegrityTests();
        std::cout << "[PASS] CDDA TOC, adaptive/verified reads, seek, continuity, and deemphasis\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "[FAIL] " << error.what() << '\n';
        return 1;
    }
}
