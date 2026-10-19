#pragma once
#include "NativeFirProcessor.h"
#include "NativeSdmProcessor.h"
#include "NativeProcessingLimits.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <functional>
#include <iomanip>
#include <limits>
#include <sstream>

namespace echo_audio_host {

struct SdmRealtimeAdmissionPcmDsp
{
    std::function<bool(std::vector<double>&, std::string&)> process;
    std::function<void()> reset;
};

inline bool requiresSdmRealtimeAdmission(
    int targetSampleRate,
    int oversamplingFactor)
{
    const bool dsd128Target =
        targetSampleRate == 352'800 || targetSampleRate == 384'000;
    const bool dsd512Target =
        targetSampleRate == 1'411'200 || targetSampleRate == 1'536'000;
    const bool dsd256Target =
        targetSampleRate == 705'600 || targetSampleRate == 768'000;
    const bool dsd512Path = dsd512Target || oversamplingFactor >= 32;
    const bool dsd256Path = dsd256Target || oversamplingFactor >= 16;
    const bool dsd128Path = dsd128Target || oversamplingFactor >= 8;
    const bool dsd64Path = targetSampleRate == 176'400 || targetSampleRate == 192'000 || oversamplingFactor >= 4;
    return dsd64Path || dsd512Path || dsd256Path || dsd128Path;
}

inline bool admitSdmRealtime(
    NativeFirProcessor& oversampler,
    NativeSdmProcessor& modulator,
    int channels,
    int sourceSampleRate,
    int targetSampleRate,
    int oversamplingFactor,
    double& realtimeRatio,
    std::string& warning,
    std::string& error,
    const SdmRealtimeAdmissionPcmDsp& pcmDsp = {},
    bool nativeDsdOutput = false)
{
    realtimeRatio = 0.0;
    warning.clear();
    error.clear();
    if (!requiresSdmRealtimeAdmission(
            targetSampleRate,
            oversamplingFactor))
        return true;

    // Measure the same source block size used by the playback producer. A
    // smaller probe can miss the stereo-parallel SDM path and reject a route
    // that meets realtime once playback starts processing full blocks.
    constexpr int admissionFrames = sdmRealtimeAdmissionSourceFrames;
    static_assert(
        admissionFrames >= echo::sdmStereoParallelMinimumFrames,
        "SDM realtime admission must exercise the stereo-parallel path");
    constexpr double maximumRealtimeRatio = 0.8;
    std::vector<double> input(
        static_cast<size_t>(admissionFrames * std::max(1, channels)),
        0.0f);
    for (int frame = 0; frame < admissionFrames; ++frame)
    {
        for (int channel = 0; channel < std::max(1, channels); ++channel)
        {
            const double phase =
                static_cast<double>(frame)
                * (0.031 + static_cast<double>(channel) * 0.013);
            input[static_cast<size_t>(frame * std::max(1, channels) + channel)] =
                0.29 * std::sin(phase);
        }
    }

    // Reset all recursive histories on both success and failure; probe audio
    // and its DSP tails must never become the first block of real playback.
    struct ResetOnExit
    {
        NativeFirProcessor& fir;
        NativeSdmProcessor& sdm;
        const SdmRealtimeAdmissionPcmDsp& dsp;
        void reset() const
        {
            fir.reset();
            sdm.reset();
            if (dsp.reset) dsp.reset();
        }
        ~ResetOnExit() { reset(); }
    } resetOnExit { oversampler, modulator, pcmDsp };

    double measuredMilliseconds = std::numeric_limits<double>::infinity();
    for (int iteration = 0; iteration < 2; ++iteration)
    {
        resetOnExit.reset();
        const auto started = std::chrono::steady_clock::now();
        std::vector<double> carrierPcm = input;
        if (pcmDsp.process && !pcmDsp.process(carrierPcm, error))
        {
            if (error.empty()) error = "native_sdm_realtime_admission_pcm_dsp_failed";
            return false;
        }
        int modulatorFrames = admissionFrames;
        if (oversampler.active())
        {
            carrierPcm = oversampler.processDouble(carrierPcm.data(), admissionFrames);
            if (carrierPcm.empty())
            {
                error = "native_sdm_realtime_admission_processing_failed";
                return false;
            }
            modulatorFrames = static_cast<int>(
                carrierPcm.size() / static_cast<size_t>(std::max(1, channels)));
        }
        if (nativeDsdOutput)
            (void)modulator.processNativeDsd(carrierPcm.data(), modulatorFrames);
        else
            (void)modulator.processDop(carrierPcm.data(), modulatorFrames);
        if (!modulator.processingError().empty())
        {
            error = modulator.processingError();
            return false;
        }
        measuredMilliseconds = std::min(
            measuredMilliseconds,
            std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - started).count());
    }
    const double deadlineMilliseconds =
        static_cast<double>(admissionFrames)
        * 1'000.0
        / static_cast<double>(std::max(
            1,
            sourceSampleRate > 0 ? sourceSampleRate : targetSampleRate));
    const double budgetMilliseconds =
        deadlineMilliseconds * maximumRealtimeRatio;
    realtimeRatio = measuredMilliseconds / deadlineMilliseconds;
    if (measuredMilliseconds < budgetMilliseconds)
        return true;

    std::ostringstream reason;
    reason << std::fixed << std::setprecision(3)
           << "native_sdm_realtime_budget_exceeded:fir_backend="
           << (oversampler.backend() == NativeFirProcessor::Backend::Cuda
                   ? "cuda"
                   : "cpu")
           << ",modulator_backend="
           << (modulator.backend() == NativeSdmProcessor::Backend::Cuda
                   ? "cuda"
                   : "cpu")
           << ",budget=" << budgetMilliseconds
           << "ms,measured=" << measuredMilliseconds
           << "ms,ratio=" << realtimeRatio;
    error = "native_sdm_realtime_admission_failed:" + reason.str();
    return false;
}
} // namespace echo_audio_host
