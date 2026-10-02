#include "../../audio-engine/JsonRpcProtocol.h"
#include "../../audio-engine/EqTypes.h"

#include <cmath>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <iostream>
#include <mutex>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#include "HostCommon.h"

#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#else
#include <unistd.h>
#endif

namespace
{
void require(bool condition, const std::string& message)
{
    if (! condition)
        throw std::runtime_error(message);
}

void requireHasKey(const nlohmann::json& object, const char* key, const std::string& message)
{
    require(object.is_object(), message + " is not an object");
    require(object.contains(key), message + " missing key " + key);
}

struct ProtocolFixture
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    echo::ConvolutionProcessor convolution;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatialDsp;
    echo::PlaybackRateProcessor playbackRate;
    echo::LevelMeterProcessor levelMeter;
    echo::DspRackOrder dspRackOrder;
    echo::WorkshopAudioEffectController workshopAudioEffect;
    echo::EqPresetStore presets;
    int nextId = 1;

    ProtocolFixture()
    {
        eq.prepare(48000.0, 512, 2);
        channelBalance.prepare(48000.0, 512, 2);
        convolution.prepare(48000.0, 512, 2);
        headroom.prepare(48000.0, 512, 2);
        replayGain.prepare(48000.0, 512, 2);
        compressor.prepare(48000.0, 512, 2);
        spatialDsp.prepare(48000.0, 512, 2);
        playbackRate.prepare(48000.0, 512, 2);
        levelMeter.prepare(48000.0, 512, 2);

        echo::JsonRpcProtocol::setOpenFileCallback(nullptr);
        echo::JsonRpcProtocol::setOpenSourceCallback(nullptr);
        echo::JsonRpcProtocol::setPauseCallback(nullptr);
        echo::JsonRpcProtocol::setSeekCallback(nullptr);
        echo::JsonRpcProtocol::setStopCallback(nullptr);
        echo::JsonRpcProtocol::setPrefetchCallback(nullptr);
        echo::JsonRpcProtocol::setVolumeCallback(nullptr);
        echo::JsonRpcProtocol::setQueueSetCallback(nullptr);
        echo::JsonRpcProtocol::setQueueClearCallback(nullptr);
        echo::JsonRpcProtocol::setAutomixPrepareCallback(nullptr);
        echo::JsonRpcProtocol::setAutomixCancelCallback(nullptr);
        echo::JsonRpcProtocol::setAutomixStateCallback(nullptr);
        echo::JsonRpcProtocol::setWriteCallback(nullptr);
    }

    std::string raw(const nlohmann::json& request, bool proDspAllowed = true)
    {
        return echo::JsonRpcProtocol::handleJsonLine(
            request.dump(),
            eq,
            channelBalance,
            convolution,
            headroom,
            replayGain,
            compressor,
            spatialDsp,
            playbackRate,
            levelMeter,
            dspRackOrder,
            workshopAudioEffect,
            presets,
            proDspAllowed);
    }

    nlohmann::json call(
        const std::string& method,
        const nlohmann::json& params = nlohmann::json(),
        bool proDspAllowed = true)
    {
        nlohmann::json request;
        request["jsonrpc"] = "2.0";
        request["id"] = nextId++;
        request["method"] = method;
        if (! params.is_null())
            request["params"] = params;

        const auto responseText = raw(request, proDspAllowed);
        require(! responseText.empty(), method + " returned empty response");
        auto response = nlohmann::json::parse(responseText);
        require(response.value("jsonrpc", "") == "2.0", method + " response has JSON-RPC version");
        require(response.value("id", 0) == request["id"].get<int>(), method + " response echoes id");
        return response;
    }
};

nlohmann::json resultFor(ProtocolFixture& fixture, const std::string& method, const nlohmann::json& params = nlohmann::json())
{
    auto response = fixture.call(method, params);
    require(response.contains("result"), method + " expected result but got " + response.dump());
    return response["result"];
}

nlohmann::json errorFor(ProtocolFixture& fixture, const std::string& method, const nlohmann::json& params = nlohmann::json())
{
    auto response = fixture.call(method, params);
    require(response.contains("error"), method + " expected error but got " + response.dump());
    require(response["error"].contains("code"), method + " error has code");
    require(response["error"].contains("message"), method + " error has message");
    return response["error"];
}

void requireEqBandStructure(const nlohmann::json& band, const std::string& message)
{
    requireHasKey(band, "gainDb", message);
    requireHasKey(band, "frequencyHz", message);
    requireHasKey(band, "q", message);
    requireHasKey(band, "filterType", message);
    requireHasKey(band, "enabled", message);
    require(band["gainDb"].is_number(), message + " gainDb is numeric");
    require(band["frequencyHz"].is_number(), message + " frequencyHz is numeric");
    require(band["q"].is_number(), message + " q is numeric");
    require(band["filterType"].is_string(), message + " filterType is string");
    require(band["enabled"].is_boolean(), message + " enabled is boolean");
}

void requireFullEqState(const nlohmann::json& state, const std::string& method)
{
    requireHasKey(state, "enabled", method);
    requireHasKey(state, "preampDb", method);
    requireHasKey(state, "dspHeadroomDb", method);
    requireHasKey(state, "dspSafetyLimiterEnabled", method);
    requireHasKey(state, "presetId", method);
    requireHasKey(state, "presetName", method);
    requireHasKey(state, "clippingRisk", method);
    requireHasKey(state, "bands", method);
    require(state["bands"].is_array(), method + " bands is array");
    require(state["bands"].size() == static_cast<size_t>(echo::eqBandCount), method + " has 31 EQ bands");
    for (size_t i = 0; i < state["bands"].size(); ++i)
        requireEqBandStructure(state["bands"][i], method + " band " + std::to_string(i));
}

nlohmann::json makeFullEqStateParams()
{
    nlohmann::json bands = nlohmann::json::array();
    for (int i = 0; i < echo::eqBandCount; ++i)
    {
        nlohmann::json band;
        band["frequencyHz"] = echo::eqFrequenciesHz[static_cast<size_t>(i)];
        band["gainDb"] = (i == 2) ? 1.5 : 0.0;
        band["q"] = 1.0;
        band["filterType"] = (i == 0) ? "lowShelf" : "peaking";
        band["enabled"] = i != 3;
        bands.push_back(band);
    }
    return nlohmann::json::array({ nlohmann::json::object({ {"enabled", true}, {"preampDb", -1.25}, {"bands", bands} }) });
}

void requireChannelBalanceState(const nlohmann::json& state, const std::string& method)
{
    for (const auto* key : { "enabled", "balance", "leftGainDb", "rightGainDb", "bandGains", "leftDelayMs", "rightDelayMs", "swapLeftRight", "monoMode", "invertLeft", "invertRight", "constantPower", "clippingRisk" })
        requireHasKey(state, key, method);
    for (const auto* band : { "low", "mid", "high" })
    {
        requireHasKey(state["bandGains"], band, method);
        requireHasKey(state["bandGains"][band], "leftGainDb", method);
        requireHasKey(state["bandGains"][band], "rightGainDb", method);
    }
}

void requireRoomCorrectionState(const nlohmann::json& state, const std::string& method)
{
    for (const auto* key : { "enabled", "status", "irId", "irName", "channelMode", "sampleRate", "tapCount", "trimDb", "latencySamples", "clippingRisk", "error" })
        requireHasKey(state, key, method);
}

void testNoIdNotificationsReturnEmpty()
{
    ProtocolFixture fixture;
    for (const auto& method : { "rpc.ready", "rpc.shuttingDown", "unknown.method" })
    {
        nlohmann::json notification;
        notification["jsonrpc"] = "2.0";
        notification["method"] = method;
        require(fixture.raw(notification).empty(), std::string(method) + " no-id notification returns empty");
    }
}

void testOutboundNotificationHasNoId()
{
    const auto text = echo::JsonRpcProtocol::createJsonRpcNotification(
        "audio.position",
        nlohmann::json::object({ {"framesPlayed", 128}, {"bufferedFrames", 256}, {"inputEnded", false} }));
    const auto notification = nlohmann::json::parse(text);
    require(notification.value("jsonrpc", "") == "2.0", "notification has jsonrpc version");
    require(notification.value("method", "") == "audio.position", "notification has method");
    require(notification.contains("params"), "notification has params");
    require(notification["params"].value("framesPlayed", 0) == 128, "audio.position freezes framesPlayed field");
    require(notification["params"].value("bufferedFrames", 0) == 256, "audio.position freezes bufferedFrames field");
    require(notification["params"].value("inputEnded", true) == false, "audio.position freezes inputEnded field");
    require(! notification.contains("id"), "notification has no id field");
}

void testJsonRpcPlaybackMethodNamesStayStable()
{
    ProtocolFixture fixture;

    const std::vector<std::string> controlMethods {
        "audio.openFile",
        "audio.openSource",
        "audio.play",
        "audio.pause",
        "audio.resume",
        "audio.seek",
        "audio.stop",
        "audio.prefetch",
        "audio.setVolume",
        "rpc.ping",
        "rpc.shutdown",
    };

    for (const auto& method : controlMethods)
    {
        auto response = fixture.call(method);
        require(response.contains("result") || response.contains("error"), method + " dispatches through JSON-RPC method table");
        if (response.contains("error"))
            require(response["error"].value("code", 0) != -32601, method + " is registered, not method-not-found");
    }
}

void testOpenSourceObjectContract()
{
    ProtocolFixture fixture;
    nlohmann::json receivedSource;
    int receivedSampleRate = 0;
    double receivedStartSeconds = 0.0;
    echo::JsonRpcProtocol::setOpenSourceCallback(
        [&](const nlohmann::json& source, int sampleRate, double startSeconds, nlohmann::json& result) {
            receivedSource = source;
            receivedSampleRate = sampleRate;
            receivedStartSeconds = startSeconds;
            result = {
                {"status", "decoding"},
                {"operationId", 7},
                {"filePath", source.value("uri", "")},
            };
            return true;
        });

    const nlohmann::json source = {
        {"kind", "http"},
        {"uri", "https://media.example.test/song.flac"},
        {"headers", {
            {"Cookie", "MUSIC_U=secret"},
            {"Referer", "https://music.163.com/"},
        }},
        {"mimeType", "audio/flac"},
    };
    const auto result = resultFor(fixture, "audio.openSource", nlohmann::json::array({{
        {"source", source},
        {"sampleRate", 44100},
        {"startSeconds", 12.5},
    }}));

    require(result.value("operationId", 0) == 7, "audio.openSource returns callback result");
    require(receivedSource == source, "audio.openSource forwards the exact source object");
    require(receivedSampleRate == 44100, "audio.openSource forwards sample rate");
    require(std::abs(receivedStartSeconds - 12.5) < 0.0001, "audio.openSource forwards start offset");
}

void testQueueSnapshotObjectContract()
{
    ProtocolFixture fixture;
    nlohmann::json receivedItems;
    std::string receivedRepeatMode;
    std::string receivedCurrentItemId;
    uint64_t receivedRevision = 0;
    echo::JsonRpcProtocol::setQueueSetCallback(
        [&](const nlohmann::json& items, const std::string& repeatMode, uint64_t revision,
            const std::string& currentItemId) {
            receivedItems = items;
            receivedRepeatMode = repeatMode;
            receivedRevision = revision;
            receivedCurrentItemId = currentItemId;
            return true;
        });

    const nlohmann::json snapshot = {
        {"revision", 7},
        {"currentItemId", "queue-1"},
        {"repeatMode", "all"},
        {"items", nlohmann::json::array({{
            {"itemId", "queue-1"},
            {"trackId", "track-1"},
            {"filePath", "track.flac"},
            {"sampleRate", 48000},
            {"startSeconds", 0.0},
        }})},
    };
    const auto result = resultFor(fixture, "queue.set", snapshot);

    require(result.value("queueRevision", 0) == 7, "queue.set acknowledges the applied revision");
    require(receivedRevision == 7, "queue.set forwards revision");
    require(receivedRepeatMode == "all", "queue.set forwards repeat mode");
    require(receivedCurrentItemId == "queue-1", "queue.set forwards current item identity");
    require(receivedItems.size() == 1 && receivedItems[0].value("trackId", "") == "track-1",
        "queue.set forwards atomic queue items");
}

void testGaplessPrepareContract()
{
    ProtocolFixture fixture;
    nlohmann::json received;
    echo::JsonRpcProtocol::setGaplessPrepareCallback(
        [&](const nlohmann::json& request, nlohmann::json& result) {
            received = request;
            result = {{"prepared", true}, {"operationId", 9}};
            return true;
        });

    const nlohmann::json request = {
        {"filePath", "next.flac"},
        {"trackId", "track-next"},
        {"sampleRate", 48000},
        {"following", nlohmann::json::array({
            {{"filePath", "third.flac"}, {"trackId", "track-third"}},
        })},
    };
    const auto result = resultFor(fixture, "audio.gaplessPrepare", request);

    require(result.value("prepared", false), "audio.gaplessPrepare acknowledges a primed next deck");
    require(result.value("operationId", 0) == 9, "audio.gaplessPrepare returns the current operation identity");
    require(received == request, "audio.gaplessPrepare forwards the exact request object");
}

void testEqMethodsReturnDocumentedStateShapes()
{
    ProtocolFixture fixture;
    const std::vector<std::pair<std::string, nlohmann::json>> methods {
        { "eq.getState", nlohmann::json() },
        { "eq.setEnabled", nlohmann::json::array({ true }) },
        { "eq.setBandGain", nlohmann::json::array({ nlohmann::json::object({ {"band", 3}, {"gainDb", 4.5} }) }) },
        { "eq.setBandFrequency", nlohmann::json::array({ nlohmann::json::object({ {"band", 3}, {"frequencyHz", 360.0} }) }) },
        { "eq.setBandQ", nlohmann::json::array({ nlohmann::json::object({ {"band", 3}, {"q", 3.5} }) }) },
        { "eq.setBandFilterType", nlohmann::json::array({ nlohmann::json::object({ {"band", 3}, {"filterType", "notch"} }) }) },
        { "eq.setBandEnabled", nlohmann::json::array({ nlohmann::json::object({ {"band", 3}, {"enabled", false} }) }) },
        { "eq.setPreamp", nlohmann::json::array({ -2.0 }) },
        { "eq.setPreset", nlohmann::json::array({ "flat" }) },
        { "eq.reset", nlohmann::json() },
        { "eq.setState", makeFullEqStateParams() },
    };

    for (const auto& method : methods)
        requireFullEqState(resultFor(fixture, method.first, method.second), method.first);

    auto state = resultFor(fixture, "eq.getState");
    state["strengthPercent"] = 25.0f;
    const auto scaled = resultFor(fixture, "eq.setState", nlohmann::json::array({ state }));
    require(scaled.value("strengthPercent", 100.0f) == 25.0f, "strength survives the full-state RPC");
    require(scaled["bands"] == state["bands"], "strength preserves original band parameters");
    state["tiltDb"] = 4.0f;
    const auto tilted = resultFor(fixture, "eq.setState", nlohmann::json::array({ state }));
    require(tilted.value("tiltDb", 0.0f) == 4.0f, "tilt survives the full-state RPC");
    require(tilted["bands"] == state["bands"], "tilt preserves original band parameters");
    const auto monitor = nlohmann::json::array({ nlohmann::json::object({
        {"active", true}, {"frequencyHz", 1000.0f}, {"q", 2.0f}
    }) });
    require(fixture.call("eq.audition", monitor, false).contains("result"), "base EQ audition is available without Pro");
    require(resultFor(fixture, "eq.audition", monitor).value("active", false), "audition start is acknowledged");
    auto stop = monitor;
    stop[0]["active"] = false;
    require(!fixture.call("eq.audition", stop, false)["result"].value("active", true), "stop remains available after entitlement loss");
    require(resultFor(fixture, "eq.getState")["bands"] == state["bands"], "audition does not mutate EQ");

    const auto sync = resultFor(fixture, "eq.syncState");
    requireHasKey(sync, "enabled", "eq.syncState");
    requireHasKey(sync, "preampDb", "eq.syncState");
    require(! sync.contains("bands"), "eq.syncState is intentionally compact");
}

void testProDspEntitlementGate()
{
    ProtocolFixture fixture;

    const auto baseEq = fixture.call("eq.setEnabled", nlohmann::json::array({ true }), false);
    require(baseEq.contains("result") && baseEq["result"].value("enabled", false),
        "base host enables EQ without Pro");
    const auto baseEqState = fixture.call("eq.setState", nlohmann::json::array({
        nlohmann::json::object({ {"enabled", true}, {"preampDb", -3.0f} }) }), false);
    require(baseEqState.contains("result") && baseEqState["result"].value("enabled", false),
        "base host accepts full EQ and headphone-correction state");
    const auto baseHeadroom = fixture.call("dsp.setHeadroom", nlohmann::json::array({ 3.0f }), false);
    require(baseHeadroom.contains("result") && baseHeadroom["result"].value("headroomDb", 0.0f) == 3.0f,
        "base host accepts nonzero headroom");

    const auto deniedCompressor = fixture.call(
        "compressor.setState",
        nlohmann::json::array({ nlohmann::json::object({ {"enabled", true} }) }),
        false);
    require(deniedCompressor.contains("error") && deniedCompressor["error"].value("code", 0) == -32003,
        "unentitled host rejects enabling compressor");

    const auto baseReplayGain = fixture.call(
        "replayGain.setConfig",
        nlohmann::json::array({ nlohmann::json::object({ {"mode", 1} }) }),
        false);
    require(baseReplayGain.contains("result") && baseReplayGain["result"].value("mode", 0) == 1,
        "base host enables ReplayGain without Pro");

    for (const auto* method : { "crossfeed.setState", "stereoField.setState", "channelMatrix.setState", "channelBalance.setState", "workshopAudioEffect.setState" })
    {
        const auto denied = fixture.call(method, nlohmann::json::array({ nlohmann::json::object({ {"enabled", true} }) }), false);
        require(denied.contains("error") && denied["error"].value("code", 0) == -32003,
            "base host rejects advanced DSP processors");
    }
    require(fixture.call("roomCorrection.setEnabled", nlohmann::json::array({ true }), false).contains("error"),
        "room correction remains Pro gated");

    const auto defaultLimiter = fixture.call("dsp.setSafetyLimiter", nlohmann::json::array(), false);
    require(defaultLimiter.contains("result"),
        "unentitled host still accepts the default-on safety limiter");
    const auto enabledLimiter = fixture.call("dsp.setSafetyLimiter", nlohmann::json::array({ true }), false);
    require(enabledLimiter.contains("result"),
        "unentitled host still accepts enabling the safety limiter");

    const auto safeReset = fixture.call("eq.setEnabled", nlohmann::json::array({ false }), false);
    require(safeReset.contains("result") && ! safeReset["result"].value("enabled", true),
        "unentitled host still accepts the safe EQ reset");
    const auto basePlaybackControl = fixture.call("playbackRate.setRate", nlohmann::json::array({ 1.0 }), false);
    require(basePlaybackControl.contains("result"), "base playback control remains available without Pro");
}

void testDspAndPlaybackControlMethodShapes()
{
    ProtocolFixture fixture;

    auto dsp = resultFor(fixture, "dsp.getState");
    requireHasKey(dsp, "headroomDb", "dsp.getState");
    requireHasKey(dsp, "safetyLimiterEnabled", "dsp.getState");
    dsp = resultFor(fixture, "dsp.setHeadroom", nlohmann::json::array({ -6.0 }));
    require(dsp["headroomDb"].is_number(), "dsp.setHeadroom returns headroom");
    dsp = resultFor(fixture, "dsp.setSafetyLimiter", nlohmann::json::array({ true }));
    require(dsp["safetyLimiterEnabled"].is_boolean(), "dsp.setSafetyLimiter returns limiter flag");

    auto rack = resultFor(fixture, "dspRack.getState");
    require(rack.value("schemaVersion", 0) == 3, "dspRack.getState returns schema version");
    require(rack["order"] == nlohmann::json::array({
        "equalizer", "convolution", "replayGain", "compressor",
        "crossfeed", "stereoField", "channelMatrix", "channelBalance" }),
        "dspRack.getState returns the default order");
    require(rack["fixedPostStages"] == nlohmann::json::array({ "headroom", "truePeakLimiter", "playbackRate", "levelMeter" }),
        "dspRack.getState identifies fixed output safety stages");

    const auto reordered = nlohmann::json::array({
        "replayGain", "crossfeed", "compressor", "equalizer",
        "channelMatrix", "channelBalance", "stereoField", "convolution" });
    rack = resultFor(fixture, "dspRack.setState", nlohmann::json::array({ nlohmann::json::object({ {"order", reordered} }) }));
    require(rack["order"] == reordered, "dspRack.setState applies a complete unique order");

    const auto invalidRack = errorFor(fixture, "dspRack.setState", nlohmann::json::array({ nlohmann::json::object({
        {"order", nlohmann::json::array({
            "equalizer", "equalizer", "replayGain", "compressor",
            "crossfeed", "stereoField", "channelMatrix", "channelBalance" })}
    }) }));
    require(invalidRack.value("code", 0) == -32004, "dspRack.setState rejects duplicate modules");
    require(resultFor(fixture, "dspRack.getState")["order"] == reordered,
        "an invalid DSP rack update preserves the last valid order");

    auto crossfeed = resultFor(fixture, "crossfeed.setState", nlohmann::json::array({ nlohmann::json::object({
        {"enabled", true}, {"amount", 2.0}, {"cutoffHz", 20.0}
    }) }));
    require(crossfeed.value("enabled", false), "crossfeed.setState enables processing");
    require(crossfeed.value("amount", 0.0f) == 1.0f, "crossfeed amount is clamped");
    require(crossfeed.value("cutoffHz", 0.0f) == 100.0f, "crossfeed cutoff is clamped");

    auto stereoField = resultFor(fixture, "stereoField.setState", nlohmann::json::array({ nlohmann::json::object({
        {"enabled", true}, {"width", 3.0}, {"centerGainDb", 30.0}, {"sideGainDb", -30.0}
    }) }));
    require(stereoField.value("width", 0.0f) == 2.0f, "stereo field width is clamped");
    require(stereoField.value("centerGainDb", 0.0f) == 18.0f, "stereo field center gain is clamped");
    require(stereoField.value("sideGainDb", 0.0f) == -18.0f, "stereo field side gain is clamped");

    auto matrix = resultFor(fixture, "channelMatrix.setState", nlohmann::json::array({ nlohmann::json::object({
        {"enabled", true}, {"leftToLeft", 3.0}, {"rightToLeft", -3.0},
        {"leftToRight", 0.25}, {"rightToRight", 0.75}
    }) }));
    require(matrix.value("leftToLeft", 0.0f) == 2.0f, "channel matrix coefficient is clamped");
    require(matrix.value("rightToLeft", 0.0f) == -2.0f, "negative channel matrix coefficient is clamped");
    require(matrix.value("clippingRisk", false), "channel matrix reports row-sum clipping risk");

    const auto playbackRate = resultFor(fixture, "playbackRate.configure", nlohmann::json::array({
        nlohmann::json::object({ {"rate", 1.25}, {"mode", "daycore"} })
    }));
    require(playbackRate.value("rate", 0.0f) == 1.25f, "playbackRate.configure returns the atomic rate");
    require(playbackRate.value("mode", "") == "daycore", "playbackRate.configure returns the atomic mode");
    const auto unitySpeed = resultFor(fixture, "playbackRate.configure", nlohmann::json::array({
        nlohmann::json::object({ {"rate", 1.0}, {"mode", "speed"} })
    }));
    require(unitySpeed.value("rate", 0.0f) == 1.0f, "unity tempo-only mode remains a no-processing bypass");
    require(unitySpeed.value("mode", "") == "speed", "unity tempo-only bypass preserves the dormant mode");
    const auto doubletime = resultFor(fixture, "playbackRate.configure", nlohmann::json::array({
        nlohmann::json::object({ {"rate", 1.5}, {"mode", "speed"} })
    }));
    require(doubletime.value("rate", 0.0f) == 1.5f, "tempo-only mode returns the accepted rate");
    require(doubletime.value("mode", "") == "speed", "tempo-only mode returns the accepted mode");
    require(resultFor(fixture, "levelMeter.setInterval", nlohmann::json::array({ 750 })).value("intervalMs", 0) == 750, "levelMeter.setInterval returns interval");
}

void testWorkshopAudioEffectContract()
{
    ProtocolFixture fixture;
    const auto state = resultFor(fixture, "workshopAudioEffect.getState");
    require(state.value("enabled", true) == false, "Workshop audio effect defaults disabled");
    require(state.value("effect", "") == "bitcrusher", "Workshop audio effect names the bounded processor");

    const nlohmann::json requested = {
        {"enabled", true},
        {"effect", "bitcrusher"},
        {"bitDepth", 8},
        {"sampleRateHz", 11025},
        {"mix", 0.75},
        {"outputGainDb", -1.0}
    };
    const auto applied = resultFor(fixture, "workshopAudioEffect.setState", nlohmann::json::array({ requested }));
    require(applied.value("enabled", false), "Workshop audio effect enable is acknowledged by host");
    require(applied.value("bitDepth", 0) == 8, "Workshop audio effect bit depth round-trips");
    require(std::abs(applied.value("mix", 0.0) - 0.75) < 0.0001, "Workshop audio effect mix round-trips");

    const nlohmann::json chiptune = {
        {"enabled", true},
        {"effect", "chiptune"},
        {"bitDepth", 8},
        {"sampleRateHz", 8000},
        {"mix", 1.0},
        {"outputGainDb", -5.0},
        {"pulseMix", 0.82},
        {"triangleMix", 0.5},
        {"noiseMix", 0.22},
        {"drive", 3.2}
    };
    const auto chipApplied = resultFor(fixture, "workshopAudioEffect.setState", nlohmann::json::array({ chiptune }));
    require(chipApplied.value("effect", "") == "chiptune", "Workshop chiptune type round-trips");
    require(std::abs(chipApplied.value("pulseMix", 0.0) - 0.82) < 0.0001,
        "Workshop chiptune pulse layer round-trips");
    require(std::abs(chipApplied.value("drive", 0.0) - 3.2) < 0.0001,
        "Workshop chiptune drive round-trips");

    const nlohmann::json vocalCut = {
        {"enabled", true},
        {"effect", "vocalCut"},
        {"mix", 1.0},
        {"outputGainDb", 0.0},
        {"vocalCutStrength", 0.85},
        {"vocalCutBassPreserveHz", 160.0}
    };
    const auto vocalCutApplied = resultFor(
        fixture, "workshopAudioEffect.setState", nlohmann::json::array({ vocalCut }));
    require(vocalCutApplied.value("effect", "") == "vocalCut", "Workshop vocal cut type round-trips");
    require(std::abs(vocalCutApplied.value("vocalCutStrength", 0.0) - 0.85) < 0.0001,
        "Workshop vocal cut strength round-trips");
    require(std::abs(vocalCutApplied.value("vocalCutBassPreserveHz", 0.0) - 160.0) < 0.0001,
        "Workshop vocal cut bass preservation round-trips");

    const auto denied = fixture.call("workshopAudioEffect.setState", nlohmann::json::array({ requested }), false);
    require(denied.contains("error") && denied["error"].value("code", 0) == -32003,
        "Workshop audio processing remains fail-closed behind Pro entitlement");

    const auto invalid = fixture.call("workshopAudioEffect.setState", nlohmann::json::array({
        nlohmann::json::object({ {"enabled", true}, {"effect", "bitcrusher"}, {"bitDepth", 2} })
    }));
    require(invalid.contains("error") && invalid["error"].value("code", 0) == -32602,
        "invalid Workshop audio effect parameters fail closed");

    const auto unsupported = fixture.call("workshopAudioEffect.setState", nlohmann::json::array({
        nlohmann::json::object({ {"enabled", true}, {"effect", "arbitrary-native-code"} })
    }));
    require(unsupported.contains("error") && unsupported["error"].value("code", 0) == -32004,
        "unbounded Workshop audio effect types remain rejected");
}

void testCompressorStateAndTelemetryContract()
{
    ProtocolFixture fixture;
    auto compressor = resultFor(fixture, "compressor.getState");
    for (const auto* key : {
        "enabled", "thresholdDb", "ratio", "attackMs", "releaseMs", "kneeDb", "makeupDb", "mix",
        "detectorMode", "sidechainHighpassEnabled", "sidechainHighpassHz", "autoRelease", "rangeDb", "stereoLink",
        "inputPeakDb", "inputRmsDb", "outputPeakDb", "outputRmsDb", "gainReductionDb",
        "gainReductionDbByChannel", "outputHeadroomDb", "clippingRisk" })
        requireHasKey(compressor, key, "compressor.getState");

    compressor = resultFor(fixture, "compressor.setState", nlohmann::json::array({ nlohmann::json::object({
        {"enabled", true}, {"thresholdDb", -200.0}, {"ratio", 100.0}, {"attackMs", 0.0},
        {"releaseMs", 9000.0}, {"kneeDb", 40.0}, {"makeupDb", 30.0}, {"mix", 2.0},
        {"detectorMode", "rms"}, {"sidechainHighpassEnabled", true}, {"sidechainHighpassHz", 1000.0},
        {"autoRelease", true}, {"rangeDb", 100.0}, {"stereoLink", -1.0}
    }) }));
    require(compressor.value("enabled", false), "compressor.setState enables processing");
    require(compressor.value("thresholdDb", 0.0f) == -72.0f, "compressor threshold is clamped");
    require(compressor.value("ratio", 0.0f) == 40.0f, "compressor ratio is clamped");
    require(compressor.value("attackMs", 0.0f) == 0.1f, "compressor attack is clamped");
    require(compressor.value("releaseMs", 0.0f) == 5000.0f, "compressor release is clamped");
    require(compressor.value("kneeDb", 0.0f) == 24.0f, "compressor knee is clamped");
    require(compressor.value("makeupDb", 0.0f) == 24.0f, "compressor makeup is clamped");
    require(compressor.value("mix", 0.0f) == 1.0f, "compressor mix is clamped");
    require(compressor.value("detectorMode", "") == "rms", "compressor detector mode is applied");
    require(compressor.value("sidechainHighpassEnabled", false), "compressor sidechain high-pass is enabled");
    require(compressor.value("sidechainHighpassHz", 0.0f) == 400.0f, "compressor sidechain high-pass is clamped");
    require(compressor.value("autoRelease", false), "compressor auto release is enabled");
    require(compressor.value("rangeDb", 0.0f) == 72.0f, "compressor range is clamped");
    require(compressor.value("stereoLink", 1.0f) == 0.0f, "compressor stereo link is clamped");
}

void testChannelBalanceRoomCorrectionReplayGainAndPresets()
{
    ProtocolFixture fixture;
    requireChannelBalanceState(resultFor(fixture, "channelBalance.getState"), "channelBalance.getState");
    requireChannelBalanceState(resultFor(fixture, "channelBalance.setState", nlohmann::json::array({ nlohmann::json::object({ {"enabled", true}, {"balance", 0.25}, {"monoMode", "sum"} }) })), "channelBalance.setState");
    requireChannelBalanceState(resultFor(fixture, "channelBalance.reset"), "channelBalance.reset");

    requireRoomCorrectionState(resultFor(fixture, "roomCorrection.getState"), "roomCorrection.getState");
    const auto missingIr = resultFor(fixture, "roomCorrection.loadIr", nlohmann::json::array({ nlohmann::json::object({ {"path", "/definitely/missing/echo-ir.wav"}, {"irId", "missing"}, {"irName", "Missing"} }) }));
    requireRoomCorrectionState(missingIr, "roomCorrection.loadIr missing-file state");
    require(missingIr.value("status", "") == "error", "roomCorrection.loadIr missing file returns error state");
    require(resultFor(fixture, "roomCorrection.setEnabled", nlohmann::json::array({ true })).contains("enabled"), "roomCorrection.setEnabled returns state");
    require(resultFor(fixture, "roomCorrection.setTrim", nlohmann::json::array({ -3.0 })).contains("trimDb"), "roomCorrection.setTrim returns state");
    requireRoomCorrectionState(resultFor(fixture, "roomCorrection.clear"), "roomCorrection.clear");

    auto rg = resultFor(fixture, "replayGain.getConfig");
    for (const auto* key : { "trackGainDb", "albumGainDb", "peak", "mode", "preampDb", "preventClipping", "appliedGainDb", "active" })
        requireHasKey(rg, key, "replayGain.getConfig");
    rg = resultFor(fixture, "replayGain.setConfig", nlohmann::json::array({ nlohmann::json::object({ {"trackGainDb", -4.0}, {"albumGainDb", -3.0}, {"peak", 0.8}, {"mode", 1}, {"preampDb", 1.0}, {"preventClipping", true} }) }));
    require(rg.value("mode", 0) == 1, "replayGain.setConfig echoes mode");

    const auto presets = resultFor(fixture, "preset.list");
    require(presets.is_array() && ! presets.empty(), "preset.list returns built-in preset array");
    requireFullEqState(nlohmann::json::object({ {"enabled", true}, {"preampDb", 0}, {"dspHeadroomDb", 0}, {"dspSafetyLimiterEnabled", true}, {"presetId", "probe"}, {"presetName", "probe"}, {"clippingRisk", false}, {"bands", presets[0]["bands"]} }), "preset.list first preset bands");
    require(errorFor(fixture, "preset.save", nlohmann::json::array({ nlohmann::json::object({ {"id", "custom"}, {"name", "Custom"}, {"preampDb", 0.0} }) })).value("message", "") == "Invalid preset data", "preset.save current validation shape");
    require(resultFor(fixture, "preset.delete", nlohmann::json::array({ "flat" })).is_array(), "preset.delete returns preset list unchanged");
}

void testProfileStubsAndLifecycle()
{
    ProtocolFixture fixture;
    require(resultFor(fixture, "profile.list").empty(), "profile.list returns []");
    const auto saveParams = nlohmann::json::array({ nlohmann::json::object({ {"id", "p1"}, {"name", "Profile"} }) });
    require(resultFor(fixture, "profile.save", saveParams) == saveParams, "profile.save echoes params");
    require(resultFor(fixture, "profile.apply", nlohmann::json::array({ "p1" })).empty(), "profile.apply returns {}");
    require(resultFor(fixture, "profile.delete", nlohmann::json::array({ "p1" })).empty(), "profile.delete returns []");
    const auto bind = resultFor(fixture, "profile.bind", nlohmann::json::object({ {"deviceId", "dac"} }));
    require(bind.value("profileId", "not-empty").empty(), "profile.bind returns empty profileId");
    require(bind["target"] == nlohmann::json::object({ {"deviceId", "dac"} }), "profile.bind echoes target");
    require(resultFor(fixture, "profile.getBinding", nlohmann::json::object()).is_null(), "profile.getBinding returns null");
    require(resultFor(fixture, "profile.applyBound", nlohmann::json::object()).empty(), "profile.applyBound returns {}");

    require(resultFor(fixture, "rpc.ping") == "pong", "rpc.ping returns pong");
    require(resultFor(fixture, "rpc.shutdown") == "ok", "rpc.shutdown returns ok");
}

void testAudioPlaybackControlWithoutCallbacks()
{
    ProtocolFixture fixture;
    require(errorFor(fixture, "audio.openFile", nlohmann::json::array({ nlohmann::json::object({ {"filePath", "/definitely/missing/song.flac"}, {"sampleRate", 48000} }) })).contains("message"), "audio.openFile without callback falls back to probe error");
    require(resultFor(fixture, "audio.play") == true, "audio.play without callback currently returns true");
    require(resultFor(fixture, "audio.pause") == true, "audio.pause without callback currently returns true");
    require(resultFor(fixture, "audio.resume") == true, "audio.resume without callback currently returns true");
    require(errorFor(fixture, "audio.seek", nlohmann::json::array({ nlohmann::json::object({ {"positionSeconds", 12.0} }) })).value("message", "") == "seek failed", "audio.seek requires callback");
    require(resultFor(fixture, "audio.stop") == true, "audio.stop without callback currently returns true");
    require(errorFor(fixture, "audio.prefetch", nlohmann::json::array({ nlohmann::json::object({ {"filePath", "/tmp/song.flac"}, {"sampleRate", 48000} }) })).value("message", "") == "prefetch failed", "audio.prefetch requires callback");
    const auto clampedVolume = resultFor(fixture, "audio.setVolume", nlohmann::json::array({ nlohmann::json::object({ {"volume", 2.0} }) })).value("volume", -1.0f);
    require(std::abs(clampedVolume - 1.0f) <= 0.0f, "audio.setVolume clamps and returns volume without callback");
}

void testRawStdinEofLifecycleDrainsSource()
{
    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor channelBalance;
    PcmRingAudioSource source(2, 32, 0, 0, 1.0f, eq, channelBalance);
    source.beginSession();
    source.prepareForNativeRender(16, 48000.0);
    require(! source.isDrained(), "raw stdin source is not drained before EOF");
    source.markInputEnded();
    require(source.isDrained(), "raw stdin EOF markInputEnded lets main loop observe isDrained");
}

void testRawPcmPipePreservesPartialFrames()
{
    int pipeFds[2] { -1, -1 };
#ifdef _WIN32
    require(_pipe(pipeFds, 4096, _O_BINARY) == 0, "raw PCM test pipe opens");
#else
    require(pipe(pipeFds) == 0, "raw PCM test pipe opens");
#endif

    std::mutex mutex;
    std::condition_variable receivedSignal;
    std::vector<float> received;
    std::string readerError;
    RawPcmInputReader reader(
        pipeFds[0],
        2,
        [&](const float* samples, int frames)
        {
            std::lock_guard<std::mutex> lock(mutex);
            received.insert(received.end(), samples, samples + frames * 2);
            receivedSignal.notify_all();
            return true;
        },
        [&](const std::string& error)
        {
            std::lock_guard<std::mutex> lock(mutex);
            readerError = error;
            receivedSignal.notify_all();
        });
    reader.start();

    const float expected[] { 0.25f, -0.5f, 0.75f, -1.0f };
    const auto* bytes = reinterpret_cast<const char*>(expected);
#ifdef _WIN32
    require(_write(pipeFds[1], bytes, 3) == 3, "raw PCM partial prefix writes");
    require(_write(pipeFds[1], bytes + 3, static_cast<unsigned int>(sizeof(expected) - 3)) == sizeof(expected) - 3,
        "raw PCM partial suffix writes");
#else
    require(write(pipeFds[1], bytes, 3) == 3, "raw PCM partial prefix writes");
    require(write(pipeFds[1], bytes + 3, sizeof(expected) - 3) == static_cast<ssize_t>(sizeof(expected) - 3),
        "raw PCM partial suffix writes");
#endif

    {
        std::unique_lock<std::mutex> lock(mutex);
        require(receivedSignal.wait_for(lock, std::chrono::seconds(2), [&] { return received.size() == 4 || ! readerError.empty(); }),
            "raw PCM reader accepts complete frames");
        require(readerError.empty(), "raw PCM reader reports no error");
        require(received == std::vector<float>(std::begin(expected), std::end(expected)),
            "raw PCM reader preserves samples across partial reads");
        require(reader.bytesConsumed() == sizeof(expected), "raw PCM reader reports consumed byte barrier");
        require(reader.waitUntilBytesConsumed(sizeof(expected), std::chrono::milliseconds(1)),
            "raw PCM consumed byte barrier resolves");
        require(! reader.waitUntilBytesConsumed(sizeof(expected) + 8, std::chrono::milliseconds(1)),
            "raw PCM consumed byte barrier does not acknowledge missing bytes");
    }

    reader.stop();
#ifdef _WIN32
    _close(pipeFds[0]);
    _close(pipeFds[1]);
#else
    close(pipeFds[0]);
    close(pipeFds[1]);
#endif
}

void testAutomixV2Contracts()
{
    ProtocolFixture fixture;
    nlohmann::json receivedPrepare;
    std::string receivedCancel;
    echo::JsonRpcProtocol::setAutomixPrepareCallback(
        [&](const nlohmann::json& request, nlohmann::json& result) {
            receivedPrepare = request;
            result = {
                {"acknowledged", true},
                {"state", "armed"},
                {"planId", request["plan"].value("planId", "")},
                {"operationId", 9},
                {"reason", nullptr},
            };
            return true;
        });
    echo::JsonRpcProtocol::setAutomixCancelCallback(
        [&](const std::string& planId, nlohmann::json& result) {
            receivedCancel = planId;
            result = {
                {"acknowledged", true},
                {"state", "idle"},
                {"planId", planId},
            };
            return true;
        });
    echo::JsonRpcProtocol::setAutomixStateCallback([] {
        return nlohmann::json{
            {"state", "armed"},
            {"planId", "plan-12"},
            {"queueRevision", 12},
            {"operationId", 9},
            {"reason", nullptr},
        };
    });

    const nlohmann::json request = {
        {"plan", {
            {"version", 2},
            {"planId", "plan-12"},
            {"queueRevision", 12},
        }},
        {"nextSource", {
            {"kind", "local"},
            {"uri", "next.flac"},
        }},
    };
    const auto prepare = resultFor(fixture, "automix.prepare", request);
    require(prepare.value("acknowledged", false), "automix.prepare must return an acknowledgement");
    require(prepare.value("planId", "") == "plan-12", "automix.prepare must preserve plan identity");
    require(receivedPrepare == request, "automix.prepare forwards the exact immutable request");

    const auto cancel = resultFor(fixture, "automix.cancel", {{"planId", "plan-12"}});
    require(cancel.value("acknowledged", false), "automix.cancel must return an acknowledgement");
    require(receivedCancel == "plan-12", "automix.cancel must target the exact plan");

    const auto state = resultFor(fixture, "automix.state");
    require(state.value("state", "") == "armed", "automix.state returns daemon-owned state");
    require(state.value("queueRevision", 0) == 12, "automix.state returns queue identity");
}

void testRawDsdPipePreservesByteFrames()
{
    int pipeFds[2] { -1, -1 };
#ifdef _WIN32
    require(_pipe(pipeFds, 4096, _O_BINARY) == 0, "raw DSD test pipe opens");
#else
    require(pipe(pipeFds) == 0, "raw DSD test pipe opens");
#endif

    std::mutex mutex;
    std::condition_variable receivedSignal;
    std::vector<uint8_t> received;
    std::string readerError;
    RawPcmInputReader reader(
        pipeFds[0],
        2,
        1,
        [&](const uint8_t* samples, int frames)
        {
            std::lock_guard<std::mutex> lock(mutex);
            received.insert(received.end(), samples, samples + frames * 2);
            receivedSignal.notify_all();
            return true;
        },
        [&](const std::string& error)
        {
            std::lock_guard<std::mutex> lock(mutex);
            readerError = error;
            receivedSignal.notify_all();
        });
    reader.start();

    const uint8_t expected[] { 0x69u, 0x96u, 0xa5u, 0x5au };
#ifdef _WIN32
    require(_write(pipeFds[1], expected, 1) == 1, "raw DSD partial prefix writes");
    require(_write(pipeFds[1], expected + 1, static_cast<unsigned int>(sizeof(expected) - 1)) == sizeof(expected) - 1,
        "raw DSD partial suffix writes");
#else
    require(write(pipeFds[1], expected, 1) == 1, "raw DSD partial prefix writes");
    require(write(pipeFds[1], expected + 1, sizeof(expected) - 1) == static_cast<ssize_t>(sizeof(expected) - 1),
        "raw DSD partial suffix writes");
#endif

    {
        std::unique_lock<std::mutex> lock(mutex);
        require(receivedSignal.wait_for(lock, std::chrono::seconds(2), [&] { return received.size() == 4 || ! readerError.empty(); }),
            "raw DSD reader accepts complete byte frames");
        require(readerError.empty(), "raw DSD reader reports no error");
        require(received == std::vector<uint8_t>(std::begin(expected), std::end(expected)),
            "raw DSD reader preserves bytes across partial reads");
        require(reader.bytesConsumed() == sizeof(expected), "raw DSD reader reports consumed byte barrier");
    }

    reader.stop();
#ifdef _WIN32
    _close(pipeFds[0]);
    _close(pipeFds[1]);
#else
    close(pipeFds[0]);
    close(pipeFds[1]);
#endif
}

void testRawPcmAbortDiscardUnblocksPendingPush()
{
    int pipeFds[2] { -1, -1 };
#ifdef _WIN32
    require(_pipe(pipeFds, 4096, _O_BINARY) == 0, "raw PCM abort test pipe opens");
#else
    require(pipe(pipeFds) == 0, "raw PCM abort test pipe opens");
#endif

    std::mutex mutex;
    std::condition_variable pushSignal;
    bool pushEntered = false;
    bool releasePush = false;
    std::string readerError;
    RawPcmInputReader reader(
        pipeFds[0],
        2,
        [&](const float*, int)
        {
            std::unique_lock<std::mutex> lock(mutex);
            pushEntered = true;
            pushSignal.notify_all();
            pushSignal.wait(lock, [&] { return releasePush; });
            return false;
        },
        [&](const std::string& error)
        {
            std::lock_guard<std::mutex> lock(mutex);
            readerError = error;
            pushSignal.notify_all();
        });
    reader.start();

    const float samples[] { 0.1f, -0.1f, 0.2f, -0.2f };
#ifdef _WIN32
    require(_write(pipeFds[1], reinterpret_cast<const char*>(samples), sizeof(samples)) == sizeof(samples),
        "raw PCM abort payload writes");
#else
    require(write(pipeFds[1], reinterpret_cast<const char*>(samples), sizeof(samples)) == static_cast<ssize_t>(sizeof(samples)),
        "raw PCM abort payload writes");
#endif

    {
        std::unique_lock<std::mutex> lock(mutex);
        require(pushSignal.wait_for(lock, std::chrono::seconds(2), [&] { return pushEntered; }),
            "raw PCM push blocks before abort");
    }
    reader.discardThrough(sizeof(samples));
    {
        std::lock_guard<std::mutex> lock(mutex);
        releasePush = true;
    }
    pushSignal.notify_all();
    require(reader.waitUntilBytesConsumed(sizeof(samples), std::chrono::seconds(1)),
        "raw PCM abort establishes discard barrier without a later pipe write");
    require(readerError.empty(), "raw PCM abort does not report a stopped-input error");

    reader.stop();
#ifdef _WIN32
    _close(pipeFds[0]);
    _close(pipeFds[1]);
#else
    close(pipeFds[0]);
    close(pipeFds[1]);
#endif
}

}

int main(int argc, char** argv)
{
    const std::vector<std::pair<std::string, void (*)()>> tests {
        { "no-id notifications return empty", testNoIdNotificationsReturnEmpty },
        { "outbound notifications omit id", testOutboundNotificationHasNoId },
        { "JSON-RPC playback method names stay stable", testJsonRpcPlaybackMethodNamesStayStable },
        { "audio.openSource object contract", testOpenSourceObjectContract },
        { "queue snapshot object contract", testQueueSnapshotObjectContract },
        { "gapless prepare contract", testGaplessPrepareContract },
        { "AutoMix V2 contracts", testAutomixV2Contracts },
        { "EQ methods return documented state shapes", testEqMethodsReturnDocumentedStateShapes },
        { "Pro DSP entitlement gate", testProDspEntitlementGate },
        { "DSP and playback-rate method shapes", testDspAndPlaybackControlMethodShapes },
        { "Workshop audio effect contract", testWorkshopAudioEffectContract },
        { "compressor state and telemetry contract", testCompressorStateAndTelemetryContract },
        { "channel balance room correction replay gain presets", testChannelBalanceRoomCorrectionReplayGainAndPresets },
        { "profile stubs and lifecycle", testProfileStubsAndLifecycle },
        { "audio playback control without callbacks", testAudioPlaybackControlWithoutCallbacks },
        { "raw stdin EOF lifecycle drains source", testRawStdinEofLifecycleDrainsSource },
        { "raw PCM pipe preserves partial frames", testRawPcmPipePreservesPartialFrames },
        { "raw DSD pipe preserves byte frames", testRawDsdPipePreservesByteFrames },
        { "raw PCM abort discard unblocks pending push", testRawPcmAbortDiscardUnblocksPendingPush },
    };

    const std::string filter = argc > 1 && argv[1] != nullptr ? argv[1] : "";
    int executed = 0;
    try
    {
        for (const auto& test : tests)
        {
            if (!filter.empty() && test.first.find(filter) == std::string::npos)
                continue;
            ++executed;
            test.second();
            std::cout << "[jsonrpc-protocol-characterization] PASS " << test.first << '\n';
        }
    }
    catch (const std::exception& error)
    {
        std::cerr << "[jsonrpc-protocol-characterization] FAIL " << error.what() << '\n';
        return 1;
    }

    if (executed == 0)
    {
        std::cerr << "[jsonrpc-protocol-characterization] FAIL no tests matched filter: " << filter << '\n';
        return 1;
    }

    return 0;
}
