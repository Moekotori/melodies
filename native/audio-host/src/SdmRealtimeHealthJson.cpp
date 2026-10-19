#include "SdmRealtimeHealthJson.h"
namespace echo_audio_host {
namespace {
const char* name(SdmHealthState state) {
    switch (state) {
        case SdmHealthState::Inactive: return "inactive";
        case SdmHealthState::Provisional: return "provisional";
        case SdmHealthState::Healthy: return "healthy";
        case SdmHealthState::AtRisk: return "at-risk";
        case SdmHealthState::Failed: return "failed";
    }
    return "unknown";
}
const char* name(SdmStreamState state) {
    switch (state) {
        case SdmStreamState::Inactive: return "inactive";
        case SdmStreamState::Starting: return "starting";
        case SdmStreamState::Prebuffering: return "prebuffering";
        case SdmStreamState::Running: return "running";
        case SdmStreamState::Paused: return "paused";
        case SdmStreamState::Draining: return "draining";
        case SdmStreamState::Stopped: return "stopped";
    }
    return "unknown";
}
Json timing(const SdmTimingSummary& value) {
    return {{"samples", value.samples}, {"median", value.median}, {"p95", value.p95},
        {"p99", value.p99}, {"max", value.maximum}};
}
}
Json sdmRealtimeHealthJson(const SdmRealtimeSnapshot& value) {
    const auto& transport = value.transport;
    return {{"scope", "source-block-producer-window"}, {"state", name(value.health)}, {"stream", name(value.stream)},
        {"windowCapacity", SdmRealtimeHealth::capacity}, {"independentlyCalibrated", value.independentlyCalibrated},
        {"executionMatchesObservation", value.executionMatchesObservation},
        {"p99IsMaximum", value.p99IsMaximum}, {"sourceSampleRate", value.sourceSampleRate},
        {"carrierSampleRate", value.carrierSampleRate}, {"dsdBitRate", uint64_t(value.carrierSampleRate) * 16u},
        {"blocks", value.blocks}, {"sourceFrames", value.sourceFrames}, {"carrierFrames", value.carrierFrames},
        {"outputDsdBitsPerChannel", value.outputDsdBits}, {"budget08Overruns", value.budgetOverruns},
        {"deadlineOverruns", value.deadlineOverruns}, {"consecutiveBudget08Overruns", value.consecutiveBudgetOverruns},
        {"longestBudget08Overruns", value.longestBudgetOverruns}, {"flowControlledBlocks", value.flowControlledBlocks},
        {"processingMs", timing(value.processingMs)}, {"enqueueWallMs", timing(value.enqueueWallMs)},
        {"wholeWallMs", timing(value.wholeWallMs)}, {"producerLockWaitMs", timing(value.producerLockWaitMs)},
        {"serviceRatio", timing(value.serviceRatio)}, {"transport", {
            {"unit", "dsd-bits-per-channel"}, {"generation", transport.generation},
            {"ready", transport.readyDsdBits}, {"capacity", transport.capacityDsdBits},
            {"minimumObservedAtProducer", value.observedQueueMinimumDsdBits}, {"played", transport.playedDsdBits},
            {"renderCallbackUnderruns", transport.underrunCallbacks}, {"renderCallbackMissingBits", transport.underrunDsdBits},
            {"driverUnderruns", nullptr}, {"paused", transport.paused}, {"prebuffering", transport.prebuffering},
            {"inputEnded", transport.inputEnded}, {"stopped", transport.stopped}}}};
}
}
