#include "daemon_queue_identity_tests.h"
#include "../src/AudioDaemon.h"
#include <filesystem>
#include <sstream>
#include <stdexcept>

namespace {
void require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}

void verifyStaleSync(bool ranged, bool beforeFirstFrame = false, const std::string& changedTarget = {}) {
    const auto path = (std::filesystem::path(__FILE__).parent_path() / "fixtures" / "pcm-3point0.wav").string();
    std::atomic<bool> shutdown { false };
    AudioDaemon::SourceHooks hooks;
    hooks.continueSessionAfterDrain = [] {};
    hooks.markInputEnded = [] {};
    hooks.requestStop = [] {};
    hooks.setPaused = [](bool) {};
    hooks.pushForGeneration = [](const float*, int, uint64_t) { return true; };
    hooks.generation = [] { return uint64_t { 1 }; };
    hooks.decoderSampleRateFor = [](int rate) { return rate; };
    std::string notifications;
    auto writer = std::make_shared<RpcOutputWriter>(-1, [&](const char* data, std::size_t bytes) {
        if (notifications.size() + bytes <= 65536) notifications.append(data, bytes);
        return static_cast<std::ptrdiff_t>(bytes);
    });
    AudioDaemon daemon(std::move(hooks), 48000, writer, shutdown, AudioDaemon::DecodePath::StreamingLibav, 3);
    auto queue = nlohmann::json::array();
    for (int i = 0; i < 3; ++i) {
        const auto id = std::to_string(i);
        nlohmann::json source = {{"kind", "local"}, {"uri", path}};
        if (ranged) source["range"] = {{"startSeconds", i * 0.002}, {"endSeconds", (i + 1) * 0.002}};
        queue.push_back({{"itemId", id}, {"trackId", "track-" + id}, {"filePath", ranged ? "album.cue#cueTrack=" + id : path},
            {"sampleRate", 48000}, {"source", source}});
    }
    require(daemon.onQueueSet(queue, "off", 1, "0"), "initial queue accepted");
    nlohmann::json result;
    require(daemon.openSourceForTests(queue[0]["source"], 48000, 0.0, result), "first source opens");
    daemon.emitEnded();
    require(daemon.hasPendingQueueAdvanceForTests(), "second occurrence awaits rendered PCM");
    if (!beforeFirstFrame) {
        daemon.emitPosition(1, 0, false);
        require(daemon.automixPairIsCurrentBoundary("1", "2", "track-2"), "native advancement owns the second occurrence");
    }
    // Simulate a snapshot sent before advancement but applied afterwards.
    require(daemon.onQueueSet(queue, "off", 2, "0"), "stale cursor queue edit accepted");
    if (beforeFirstFrame) {
        require(daemon.hasPendingQueueAdvanceForTests(), "stale sync before first PCM must preserve the pending advance");
        if (!changedTarget.empty()) {
            if (changedTarget == "itemId") queue[1]["itemId"] = "replacement";
            if (changedTarget == "sampleRate") queue[1]["sampleRate"] = 44100;
            if (changedTarget == "startSeconds") queue[1]["startSeconds"] = 0.001;
            if (changedTarget == "range") queue[1]["source"]["range"]["endSeconds"] = 0.005;
            require(daemon.onQueueSet(queue, "off", 3, "0"), "changed target queue accepted");
            require(!daemon.hasPendingQueueAdvanceForTests(), "changed target decode intent must cancel the pending advance");
            daemon.emitPosition(1, 0, false);
            require(!daemon.hasPendingQueueAdvanceForTests(), "first PCM cannot resurrect an invalidated target");
            writer->close();
            require(notifications.find("daemon_pending_queue_advance_cancelled_by_queue_set") != std::string::npos,
                "invalidated target retains the explicit cancellation error");
            require(notifications.find("\"queueAdvance\":true") == std::string::npos,
                "invalidated target must not publish a successful advance");
            return;
        }
        // Reordering the same source occurrences and hydrating metadata must
        // retain the exact target and publish its latest index/revision once.
        queue = nlohmann::json::array({queue[1], queue[2], queue[0]});
        queue[0]["metadata"] = {{"title", "Updated target"}};
        require(daemon.onQueueSet(queue, "off", 3, "0"), "reordered queue sync accepted before first PCM");
        require(daemon.hasPendingQueueAdvanceForTests(), "queue edit must retain the already-open exact occurrence");
        daemon.emitPosition(0, 0, false);
        require(daemon.hasPendingQueueAdvanceForTests(), "zero rendered frames still cannot commit");
        daemon.emitPosition(1, 0, false);
        require(!daemon.hasPendingQueueAdvanceForTests(), "first rendered frame commits the preserved advance");
        daemon.emitPosition(2, 0, false);
    }
    writer->close();
    int advances = 0;
    std::istringstream lines(notifications);
    std::string line;
    while (std::getline(lines, line)) {
        const auto event = nlohmann::json::parse(line);
        require(event.value("method", "") != "audio.error", "valid queue sync must not emit an audio error");
        if (event.value("method", "") != "audio.ended") continue;
        const auto& params = event["params"];
        require(params.value("queueAdvance", false), "valid sync must publish an advance rather than a terminal end");
        require(params.value("nextItemId", "") == "1", "advance keeps the exact duplicate/CUE occurrence");
        require(params.value("queueRevision", 0) == (beforeFirstFrame ? 3 : 1), "advance uses the revision at first-frame commit");
        if (beforeFirstFrame) {
            require(params.value("queueIndex", -1) == 0, "advance uses the target's new queue index");
            require(params["nextMetadata"].value("title", "") == "Updated target", "advance uses refreshed target metadata");
        }
        ++advances;
    }
    require(advances == 1, "queue sync must not lose or duplicate the first-frame advance");
    if (beforeFirstFrame) queue = nlohmann::json::array({queue[2], queue[0], queue[1]});
    require(daemon.automixPairIsCurrentBoundary("1", "2", "track-2"), "stale cursor must not rewind the audible occurrence");
    daemon.stopForTests(result);
    require(daemon.onQueueSet(queue, "off", 4, "0"), "stopped operation accepts manual queue intent");
    require(daemon.automixPairIsCurrentBoundary("0", "1", "track-1"), "manual playback retains exact occurrence selection");
    require(daemon.openSourceForTests(queue[0]["source"], 48000, 0.0, result), "first manual occurrence opens");
    const auto previousOperation = result.value("operationId", uint64_t { 0 });
    require(daemon.openSourceForTests(queue[1]["source"], 48000, 0.0, result), "another occurrence opens");
    const auto selectedOperation = result.value("operationId", uint64_t { 0 });
    require(selectedOperation != previousOperation, "manual opens have distinct operation identities");
    require(daemon.onQueueSet(queue, "off", 5, "1", selectedOperation), "acknowledged open binds its occurrence");
    require(daemon.automixPairIsCurrentBoundary("1", "2", "track-2"), "same-file open does not retain the old occurrence");
    require(daemon.onQueueSet(queue, "off", 6, "0", previousOperation), "old operation snapshot remains an edit");
    require(daemon.automixPairIsCurrentBoundary("1", "2", "track-2"), "old operation cannot rewind the new occurrence");
}
}

void testDaemonQueueIdentitySurvivesStaleSync() {
    verifyStaleSync(false);
    verifyStaleSync(true);
}

void testDaemonPendingAdvanceSurvivesQueueSync() {
    verifyStaleSync(false, true);
    verifyStaleSync(true, true);
    verifyStaleSync(false, true, "itemId");
    verifyStaleSync(false, true, "sampleRate");
    verifyStaleSync(false, true, "startSeconds");
    verifyStaleSync(true, true, "range");
}
