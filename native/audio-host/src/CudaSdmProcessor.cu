#include "CudaSdmProcessor.h"
#include "CudaDeviceSelection.h"
#include "../../audio-engine/SdmExtraLoop.h"
#include "../../audio-engine/SdmPreModulator.h"

#include <cuda_runtime.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <limits>
#include <utility>

namespace
{
constexpr int maximumSdmChannels = 2;
constexpr int maximumSdmOrder = 8;
constexpr int sdmWarpSize = 32;
constexpr int sdmKernelThreads = maximumSdmChannels * sdmWarpSize;
constexpr uint8_t dsdSilenceByte = 0x69;

__device__ __forceinline__ uint8_t reverseDsdByteForDop(uint8_t byte)
{
    byte = static_cast<uint8_t>(((byte & 0xf0u) >> 4u) | ((byte & 0x0fu) << 4u));
    byte = static_cast<uint8_t>(((byte & 0xccu) >> 2u) | ((byte & 0x33u) << 2u));
    return static_cast<uint8_t>(((byte & 0xaau) >> 1u) | ((byte & 0x55u) << 1u));
}

enum class SdmOutputFormat : int
{
    Dop = 0,
    NativeDsd = 1,
};

struct DeviceSdmConfig
{
    int channels = 0;
    int order = 0;
    int transitionRampFrames = 0;
    uint32_t idleLockFrames = 0;
    double feedbackNumerator[maximumSdmOrder] {};
    double feedbackDenominator[maximumSdmOrder] {};
    double ditherAmplitude = 0.0;
    double inputLimit = 0.0;
    double stabilityLimit = 0.0;
    double profileHeadroomGain = 0.0;
    double idleLockThreshold = 0.0;
    double idleUnlockThreshold = 0.0;
    double feedbackStateLimit = 0.0;
    int preModulatorStages = 0;
    int preModulatorFactor = 1;
    int preModulatorBitsPerCarrier = 16;
    int decisionMode = static_cast<int>(echo::SdmDecisionMode::Greedy);
    int adaptiveLoopGainEnabled = 0;
    int extendedCompensationEnabled = 0;
    double adaptiveEnvelopeReleaseDecay = 1.0;
    double adaptiveEnvelopeThreshold = 0.5;
    double adaptiveOverdriveRangeInverse = 0.0;
    // Adaptive v5 relaxed pole set (same zeros, lower peak gain), run as a
    // second error-feedback recursion; zero unless the adaptive brake is
    // enabled. Mirrors SdmProcessor::adaptiveRelaxed*Coefficients_.
    double relaxedFeedbackNumerator[maximumSdmOrder] {};
    double relaxedFeedbackDenominator[maximumSdmOrder] {};
    double ecCorrelationAlpha = 0.0;
    double ecBiasWeight = 0.0;
    double ecActivationLimit = 0.0;
    double ecMaxDecisionBias = 0.0;
    int ecEnvelopeGated = 0;
    double halfbandCenterTap = 0.5;
    double halfbandEvenTaps[echo::sdmPreModulatorEvenTapCount] {};
};

struct DeviceSdmState
{
    int transitionRampPosition = 0;
    int gainRampFramesRemaining = 0;
    double currentUserGain = 1.0;
    double targetUserGain = 1.0;
    double errorHistory[maximumSdmChannels * maximumSdmOrder] {};
    double feedbackHistory[maximumSdmChannels * maximumSdmOrder] {};
    double relaxedFeedbackHistory[maximumSdmChannels * maximumSdmOrder] {};
    uint32_t ditherState[maximumSdmChannels] {};
    uint32_t idleRunFrames[maximumSdmChannels] {};
    uint8_t idleLocked[maximumSdmChannels] {};
    float previousSamples[maximumSdmChannels] {};
    double adaptiveEnvelope[maximumSdmChannels] {};
    double ecCorrelatedError[maximumSdmChannels] {};
    double preModulatorHistory[maximumSdmChannels * echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength] {};
    double channelPeakFeedback[maximumSdmChannels] {};
    uint64_t channelStabilityRecoveries[maximumSdmChannels] {};
    uint64_t dopFrameIndex = 0;
    uint64_t targetGainRevision = 0;
};

struct DeviceSdmTelemetry
{
    double channelPeakFeedback[maximumSdmChannels] {};
    uint64_t channelStabilityRecoveries[maximumSdmChannels] {};
};

__device__ double clampDevice(double value, double minimum, double maximum)
{
    return fmin(maximum, fmax(minimum, value));
}

__device__ double historySampleDevice(const double* history, double current, int delay)
{
    if (delay <= 0)
        return current;
    return history[delay - 1];
}

__device__ void processHalfbandStageDevice(
    const DeviceSdmConfig& config,
    double* history,
    double input,
    double& even,
    double& odd)
{
    constexpr int centerDelay = (echo::sdmPreModulatorHalfbandTapCount - 1) / 4;
    even = 0.0;
    for (int tap = 0; tap < echo::sdmPreModulatorEvenTapCount; ++tap)
    {
        even = __dadd_rn(
            even,
            __dmul_rn(
                config.halfbandEvenTaps[tap],
                historySampleDevice(history, input, tap)));
    }
    odd = __dmul_rn(
        __dmul_rn(2.0, config.halfbandCenterTap),
        historySampleDevice(history, input, centerDelay));
    for (int index = echo::sdmPreModulatorHistoryLength - 1; index > 0; --index)
        history[index] = history[index - 1];
    history[0] = input;
}

__device__ void upsampleCarriersDevice(
    const DeviceSdmConfig& config,
    double* channelHistory,
    double input,
    double* carriers)
{
    if (config.preModulatorStages <= 0)
    {
        carriers[0] = input;
        return;
    }
    double stageInput[echo::sdmPreModulatorMaxFactor] { input };
    int count = 1;
    for (int stage = 0; stage < config.preModulatorStages; ++stage)
    {
        double stageOutput[echo::sdmPreModulatorMaxFactor] {};
        for (int index = 0; index < count; ++index)
        {
            double even = 0.0;
            double odd = 0.0;
            processHalfbandStageDevice(
                config,
                channelHistory + stage * echo::sdmPreModulatorHistoryLength,
                stageInput[index],
                even,
                odd);
            stageOutput[index * 2] = even;
            stageOutput[index * 2 + 1] = odd;
        }
        count *= 2;
        for (int index = 0; index < count; ++index)
            stageInput[index] = stageOutput[index];
    }
    for (int index = 0; index < config.preModulatorFactor; ++index)
        carriers[index] = stageInput[index];
}

__global__ void processSdmKernel(
    const float* input,
    uint8_t* output,
    const DeviceSdmState* committedState,
    DeviceSdmState* pendingState,
    DeviceSdmTelemetry* telemetry,
    DeviceSdmConfig config,
    double requestedTargetGain,
    uint64_t requestedTargetGainRevision,
    int frames,
    SdmOutputFormat outputFormat)
{
    const int lane = static_cast<int>(threadIdx.x) & (sdmWarpSize - 1);
    const int channel = static_cast<int>(threadIdx.x) / sdmWarpSize;
    if (blockIdx.x != 0
        || channel >= config.channels
        || channel >= maximumSdmChannels)
        return;
    constexpr unsigned int warpMask = 0xffffffffu;

    const int historyBase = channel * maximumSdmOrder;
    double errorHistory =
        lane < config.order ? committedState->errorHistory[historyBase + lane] : 0.0;
    double feedbackHistory =
        lane < config.order ? committedState->feedbackHistory[historyBase + lane] : 0.0;
    double relaxedFeedbackHistory =
        lane < config.order ? committedState->relaxedFeedbackHistory[historyBase + lane] : 0.0;
    uint32_t ditherState =
        lane == 0 ? committedState->ditherState[channel] : 0u;
    uint32_t idleRunFrames =
        lane == 0 ? committedState->idleRunFrames[channel] : 0u;
    uint8_t idleLocked =
        lane == 0 && committedState->idleLocked[channel] != 0 ? 1 : 0;
    float previousSample =
        lane == 0 ? committedState->previousSamples[channel] : 0.0f;
    double adaptiveEnvelope =
        lane == 0 ? committedState->adaptiveEnvelope[channel] : 0.0;
    double ecCorrelatedError =
        lane == 0 ? committedState->ecCorrelatedError[channel] : 0.0;
    double preModulatorHistory[echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength] {};
    if (lane == 0)
    {
        const int firBase =
            channel * echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength;
        for (int index = 0; index < echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength; ++index)
            preModulatorHistory[index] = committedState->preModulatorHistory[firBase + index];
    }
    double channelPeakFeedback =
        lane == 0 ? committedState->channelPeakFeedback[channel] : 0.0;
    uint64_t channelStabilityRecoveries =
        lane == 0 ? committedState->channelStabilityRecoveries[channel] : 0u;
    double currentUserGain =
        lane == 0 ? committedState->currentUserGain : 1.0;
    double targetUserGain = committedState->targetUserGain;
    int gainRampFramesRemaining =
        lane == 0 ? committedState->gainRampFramesRemaining : 0;
    int transitionRampPosition =
        lane == 0 ? committedState->transitionRampPosition : 0;
    if (lane == 0
        && committedState->targetGainRevision != requestedTargetGainRevision)
    {
        targetUserGain = requestedTargetGain;
        gainRampFramesRemaining = config.transitionRampFrames;
    }
    const uint64_t initialDopFrameIndex = committedState->dopFrameIndex;

    for (int frame = 0; frame < frames; ++frame)
    {
        double sample = 0.0;
        double previous = 0.0;
        int emitIdleSilence = 0;
        int resetChannelState = 0;
        if (lane == 0)
        {
            if (gainRampFramesRemaining > 0)
            {
                currentUserGain +=
                    (targetUserGain - currentUserGain)
                    / static_cast<double>(gainRampFramesRemaining);
                --gainRampFramesRemaining;
            }
            else
            {
                currentUserGain = targetUserGain;
            }
            const double transitionGain =
                transitionRampPosition < config.transitionRampFrames
                    ? static_cast<double>(++transitionRampPosition)
                        / static_cast<double>(config.transitionRampFrames)
                    : 1.0;
            const double protectedGain =
                config.profileHeadroomGain * currentUserGain * transitionGain;
            const float protectedSample = static_cast<float>(
                static_cast<double>(
                    input[frame * config.channels + channel])
                * protectedGain);
            sample = clampDevice(
                static_cast<double>(protectedSample),
                -config.inputLimit,
                config.inputLimit);
            previous = clampDevice(
                static_cast<double>(previousSample),
                -config.inputLimit,
                config.inputLimit);

            const double magnitude = fabs(sample);
            if (idleLocked != 0)
            {
                if (magnitude < config.idleUnlockThreshold)
                {
                    emitIdleSilence = 1;
                }
                else
                {
                    idleLocked = 0;
                    idleRunFrames = 0;
                    previousSample = 0.0f;
                    resetChannelState = 1;
                }
            }
            else if (magnitude <= config.idleLockThreshold)
            {
                idleRunFrames =
                    min(config.idleLockFrames, idleRunFrames + 1u);
                if (idleRunFrames >= config.idleLockFrames)
                {
                    idleLocked = 1;
                    previousSample = 0.0f;
                    emitIdleSilence = 1;
                }
            }
            else
            {
                idleRunFrames = 0;
            }
        }
        sample = __shfl_sync(warpMask, sample, 0);
        previous = __shfl_sync(warpMask, previous, 0);
        emitIdleSilence = __shfl_sync(
            warpMask, emitIdleSilence, 0);
        resetChannelState = __shfl_sync(
            warpMask, resetChannelState, 0);
        if (emitIdleSilence != 0 || resetChannelState != 0)
        {
            errorHistory = 0.0;
            feedbackHistory = 0.0;
            relaxedFeedbackHistory = 0.0;
            if (lane == 0)
            {
                for (int index = 0; index < echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength; ++index)
                    preModulatorHistory[index] = 0.0;
            }
        }

        uint8_t firstByte = 0;
        uint8_t secondByte = 0;
        if (emitIdleSilence != 0)
        {
            if (lane == 0)
            {
                firstByte = dsdSilenceByte;
                secondByte = dsdSilenceByte;
            }
        }
        else
        {
            double carriers[echo::sdmPreModulatorMaxFactor] {};
            if (lane == 0)
                upsampleCarriersDevice(config, preModulatorHistory, sample, carriers);
            for (int carrier = 0; carrier < echo::sdmPreModulatorMaxFactor; ++carrier)
                carriers[carrier] = __shfl_sync(warpMask, carriers[carrier], 0);
            const int factor = config.preModulatorFactor > 0 ? config.preModulatorFactor : 1;
            const int bitsPerCarrier =
                config.preModulatorBitsPerCarrier > 0 ? config.preModulatorBitsPerCarrier : 16;
            int bitIndex = 0;
            for (int carrier = 0; carrier < factor; ++carrier)
            {
                const double current = clampDevice(carriers[carrier], -config.inputLimit, config.inputLimit);
                if (lane == 0)
                    previous = clampDevice(static_cast<double>(previousSample), -config.inputLimit, config.inputLimit);
                previous = __shfl_sync(warpMask, previous, 0);
                for (int bit = 0; bit < bitsPerCarrier; ++bit)
                {
                    const double phase =
                        static_cast<double>(bit + 1) / static_cast<double>(bitsPerCarrier);
                    const double bitSample = __dadd_rn(
                        previous,
                        __dmul_rn(__dsub_rn(current, previous), phase));
                    double feedback = 0.0;
                    // Adaptive v5: the relaxed pole set runs as a second
                    // recursion on the same error history (mirrors
                    // SdmProcessor); lane 0 blends the two feedbacks at the
                    // injection point below.
                    double relaxedFeedback = 0.0;
                    const double feedbackNumeratorProduct = lane < config.order
                        ? __dmul_rn(config.feedbackNumerator[lane], errorHistory)
                        : 0.0;
                    const double feedbackDenominatorProduct = lane < config.order
                        ? __dmul_rn(config.feedbackDenominator[lane], feedbackHistory)
                        : 0.0;
                    const double relaxedNumeratorProduct =
                        config.adaptiveLoopGainEnabled != 0 && lane < config.order
                        ? __dmul_rn(config.relaxedFeedbackNumerator[lane], errorHistory)
                        : 0.0;
                    const double relaxedDenominatorProduct =
                        config.adaptiveLoopGainEnabled != 0 && lane < config.order
                        ? __dmul_rn(config.relaxedFeedbackDenominator[lane], relaxedFeedbackHistory)
                        : 0.0;
                    // Accumulate in the same tap order as SdmProcessor. A
                    // reduction tree changes FP64 rounding and can turn that
                    // tiny difference into another 1-bit decision.
                    for (int tap = 0; tap < maximumSdmOrder; ++tap)
                    {
                        const double numeratorProduct = __shfl_sync(
                            warpMask, feedbackNumeratorProduct, tap);
                        const double denominatorProduct = __shfl_sync(
                            warpMask, feedbackDenominatorProduct, tap);
                        if (lane == 0 && tap < config.order)
                        {
                            feedback = __dadd_rn(feedback, numeratorProduct);
                            feedback = __dsub_rn(feedback, denominatorProduct);
                        }
                    }
                    if (config.adaptiveLoopGainEnabled != 0)
                    {
                        for (int tap = 0; tap < maximumSdmOrder; ++tap)
                        {
                            const double numeratorProduct = __shfl_sync(
                                warpMask, relaxedNumeratorProduct, tap);
                            const double denominatorProduct = __shfl_sync(
                                warpMask, relaxedDenominatorProduct, tap);
                            if (lane == 0 && tap < config.order)
                            {
                                relaxedFeedback = __dadd_rn(relaxedFeedback, numeratorProduct);
                                relaxedFeedback = __dsub_rn(relaxedFeedback, denominatorProduct);
                            }
                        }
                    }
                    int resetHistory = 0;
                    double quantizationError = 0.0;
                    int one = 0;
                    double extraFeedbackTail = 0.0;
                    double horizonFeedbackTail = 0.0;
                    if (config.decisionMode != static_cast<int>(echo::SdmDecisionMode::Greedy))
                    {
                        if (lane == 0)
                            extraFeedbackTail = -config.feedbackDenominator[0] * feedback;
                        if (lane + 1 < config.order)
                        {
                            extraFeedbackTail +=
                                config.feedbackNumerator[lane + 1] * errorHistory
                                - config.feedbackDenominator[lane + 1] * feedbackHistory;
                        }
                        for (int offset = maximumSdmOrder / 2; offset > 0; offset /= 2)
                        {
                            extraFeedbackTail += __shfl_down_sync(
                                warpMask,
                                extraFeedbackTail,
                                offset);
                        }
                    }
                    if (config.decisionMode == static_cast<int>(echo::SdmDecisionMode::Horizon))
                    {
                        if (lane == 0 && config.order > 1)
                            horizonFeedbackTail = -config.feedbackDenominator[1] * feedback;
                        if (lane + 2 < config.order)
                        {
                            horizonFeedbackTail +=
                                config.feedbackNumerator[lane + 2] * errorHistory
                                - config.feedbackDenominator[lane + 2] * feedbackHistory;
                        }
                        for (int offset = maximumSdmOrder / 2; offset > 0; offset /= 2)
                        {
                            horizonFeedbackTail += __shfl_down_sync(
                                warpMask,
                                horizonFeedbackTail,
                                offset);
                        }
                    }
                    if (lane == 0)
                    {
                        if (!isfinite(feedback)
                            || fabs(feedback) > config.feedbackStateLimit)
                        {
                            feedback = 0.0;
                            // SdmProcessor zeroes every history before it
                            // evaluates the relaxed recursion, so a reset
                            // leaves that feedback at exactly 0.0 as well.
                            relaxedFeedback = 0.0;
                            resetHistory = 1;
                            ++channelStabilityRecoveries;
                        }
                        channelPeakFeedback =
                            fmax(channelPeakFeedback, fabs(feedback));
                        double injectedFeedback = feedback;
                        if (config.adaptiveLoopGainEnabled != 0)
                        {
                            if (!isfinite(relaxedFeedback)
                                || fabs(relaxedFeedback) > config.feedbackStateLimit)
                            {
                                feedback = 0.0;
                                relaxedFeedback = 0.0;
                                resetHistory = 1;
                                ++channelStabilityRecoveries;
                            }
                            double envelope =
                                __dmul_rn(
                                    adaptiveEnvelope,
                                    config.adaptiveEnvelopeReleaseDecay);
                            const double magnitude = fabs(bitSample);
                            if (magnitude > envelope)
                                envelope = magnitude;
                            adaptiveEnvelope = envelope;
                            double mix = 0.0;
                            const double overdrive =
                                envelope - config.adaptiveEnvelopeThreshold;
                            if (overdrive > 0.0)
                            {
                                mix = __dmul_rn(
                                    overdrive,
                                    config.adaptiveOverdriveRangeInverse);
                                if (mix > 1.0)
                                    mix = 1.0;
                            }
                            injectedFeedback = __dadd_rn(
                                feedback,
                                __dmul_rn(mix, __dsub_rn(relaxedFeedback, feedback)));
                            channelPeakFeedback =
                                fmax(channelPeakFeedback, fabs(relaxedFeedback));
                        }
                        double ecBias = 0.0;
                        bool ecEngaged = false;
                        if (config.extendedCompensationEnabled != 0)
                        {
                            const double ecMagnitude = fabs(bitSample);
                            if (config.ecEnvelopeGated != 0)
                            {
                                if (config.adaptiveLoopGainEnabled == 0)
                                {
                                    double envelope = __dmul_rn(
                                        adaptiveEnvelope,
                                        config.adaptiveEnvelopeReleaseDecay);
                                    if (ecMagnitude > envelope)
                                        envelope = ecMagnitude;
                                    adaptiveEnvelope = envelope;
                                }
                                ecEngaged =
                                    adaptiveEnvelope <= config.ecActivationLimit;
                            }
                            else
                            {
                                ecEngaged = ecMagnitude <= config.ecActivationLimit;
                            }
                            if (ecEngaged)
                            {
                                ecBias = clampDevice(
                                    __dmul_rn(
                                        config.ecBiasWeight,
                                        ecCorrelatedError),
                                    -config.ecMaxDecisionBias,
                                    config.ecMaxDecisionBias);
                            }
                        }
                        if (ditherState == 0)
                            ditherState = 0x9e3779b9u;
                        ditherState = ditherState * 1664525u + 1013904223u;
                        const double first =
                            static_cast<double>(ditherState) / 4294967296.0;
                        ditherState = ditherState * 1664525u + 1013904223u;
                        const double second =
                            static_cast<double>(ditherState) / 4294967296.0;
                        const double dither = __dmul_rn(
                            __dsub_rn(__dadd_rn(first, second), 1.0),
                            config.ditherAmplitude);
                        // v5 dither placement (mirrors SdmProcessor): the
                        // clamped loop input excludes dither; dither only
                        // perturbs the selection, and the fed-back error is
                        // loopInput - y so the dither is NTF-shaped.
                        const double loopInput = clampDevice(
                            __dadd_rn(bitSample, injectedFeedback),
                            -config.stabilityLimit,
                            config.stabilityLimit);
                        const double decision = __dadd_rn(loopInput, dither);
                        if (config.decisionMode == static_cast<int>(echo::SdmDecisionMode::Horizon))
                        {
                            double nextBitSample = current;
                            if (bit + 1 < bitsPerCarrier)
                            {
                                const double nextPhase =
                                    static_cast<double>(bit + 2) / static_cast<double>(bitsPerCarrier);
                                nextBitSample = __dadd_rn(
                                    previous,
                                    __dmul_rn(
                                        __dsub_rn(current, previous),
                                        nextPhase));
                            }
                            else if (carrier + 1 < factor)
                            {
                                const double nextCurrent = clampDevice(
                                    carriers[carrier + 1],
                                    -config.inputLimit,
                                    config.inputLimit);
                                const double nextPhase = 1.0 / static_cast<double>(bitsPerCarrier);
                                nextBitSample = __dadd_rn(
                                    current,
                                    __dmul_rn(
                                        __dsub_rn(nextCurrent, current),
                                        nextPhase));
                            }
                            uint32_t predictedDitherState = ditherState;
                            predictedDitherState = predictedDitherState * 1664525u + 1013904223u;
                            const double predictedFirst =
                                static_cast<double>(predictedDitherState) / 4294967296.0;
                            predictedDitherState = predictedDitherState * 1664525u + 1013904223u;
                            const double predictedSecond =
                                static_cast<double>(predictedDitherState) / 4294967296.0;
                            const double predictedDither = __dmul_rn(
                                __dsub_rn(
                                    __dadd_rn(predictedFirst, predictedSecond),
                                    1.0),
                                config.ditherAmplitude);
                            double predictiveError = 0.0;
                            one = echo::chooseSdmHorizonBitFromTails(
                                decision,
                                nextBitSample + predictedDither,
                                config.stabilityLimit,
                                config.feedbackNumerator[0],
                                config.feedbackDenominator[0],
                                config.order > 1 ? config.feedbackNumerator[1] : 0.0,
                                extraFeedbackTail,
                                horizonFeedbackTail,
                                predictiveError,
                                ecBias)
                                ? 1
                                : 0;
                        }
                        else if (config.decisionMode == static_cast<int>(echo::SdmDecisionMode::Extra))
                        {
                            const bool greedy = decision >= 0.0;
                            const double greedyValue = greedy ? 1.0 : -1.0;
                            const double greedyError = clampDevice(
                                decision - greedyValue,
                                -config.stabilityLimit,
                                config.stabilityLimit);
                            const double greedyNext =
                                extraFeedbackTail
                                + config.feedbackNumerator[0] * greedyError;
                            const double greedyCost =
                                fabs(greedyNext)
                                + echo::sdmExtraQuantErrorWeight * fabs(greedyError)
                                + ecBias * greedyError;
                            const bool other = !greedy;
                            const double otherValue = other ? 1.0 : -1.0;
                            const double otherError = clampDevice(
                                decision - otherValue,
                                -config.stabilityLimit,
                                config.stabilityLimit);
                            const double otherNext =
                                extraFeedbackTail
                                + config.feedbackNumerator[0] * otherError;
                            const double otherCost =
                                fabs(otherNext)
                                + echo::sdmExtraQuantErrorWeight * fabs(otherError)
                                + ecBias * otherError;
                            const bool selected = otherCost < greedyCost
                                ? other
                                : greedy;
                            one = selected ? 1 : 0;
                        }
                        else
                        {
                            one = config.extendedCompensationEnabled != 0
                                ? (decision + 0.5 * ecBias >= 0.0 ? 1 : 0)
                                : (decision >= 0.0 ? 1 : 0);
                        }
                        quantizationError = clampDevice(
                            __dsub_rn(loopInput, one != 0 ? 1.0 : -1.0),
                            -config.stabilityLimit,
                            config.stabilityLimit);
                        if (ecEngaged)
                        {
                            ecCorrelatedError = __dadd_rn(
                                ecCorrelatedError,
                                __dmul_rn(
                                    config.ecCorrelationAlpha,
                                    __dsub_rn(
                                        quantizationError,
                                        ecCorrelatedError)));
                        }
                        if (one != 0)
                        {
                            if (bitIndex < 8)
                                firstByte = static_cast<uint8_t>(
                                    firstByte
                                    | static_cast<uint8_t>(1u << bitIndex));
                            else
                                secondByte = static_cast<uint8_t>(
                                    secondByte
                                    | static_cast<uint8_t>(1u << (bitIndex - 8)));
                        }
                    }
                    feedback = __shfl_sync(warpMask, feedback, 0);
                    relaxedFeedback = __shfl_sync(warpMask, relaxedFeedback, 0);
                    quantizationError = __shfl_sync(
                        warpMask, quantizationError, 0);
                    resetHistory = __shfl_sync(
                        warpMask, resetHistory, 0);
                    if (resetHistory != 0)
                    {
                        errorHistory = 0.0;
                        feedbackHistory = 0.0;
                        relaxedFeedbackHistory = 0.0;
                        if (lane == 0)
                        {
                            for (int index = 0; index < echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength; ++index)
                                preModulatorHistory[index] = 0.0;
                        }
                    }
                    const double previousError =
                        __shfl_up_sync(warpMask, errorHistory, 1);
                    const double previousFeedback =
                        __shfl_up_sync(warpMask, feedbackHistory, 1);
                    const double previousRelaxedFeedback =
                        __shfl_up_sync(warpMask, relaxedFeedbackHistory, 1);
                    if (lane < config.order)
                    {
                        errorHistory =
                            lane == 0 ? quantizationError : previousError;
                        feedbackHistory =
                            lane == 0 ? feedback : previousFeedback;
                        if (config.adaptiveLoopGainEnabled != 0)
                        {
                            relaxedFeedbackHistory =
                                lane == 0 ? relaxedFeedback : previousRelaxedFeedback;
                        }
                    }
                    ++bitIndex;
                }
                if (lane == 0)
                    previousSample = static_cast<float>(current);
            }
        }

        if (lane == 0 && outputFormat == SdmOutputFormat::Dop)
        {
            const uint32_t marker =
                ((initialDopFrameIndex + static_cast<uint64_t>(frame)) & 1u) == 0u
                    ? 0x05u
                    : 0xfau;
            reinterpret_cast<uint32_t*>(output)[
                frame * config.channels + channel] =
                static_cast<uint32_t>(reverseDsdByteForDop(secondByte))
                | (static_cast<uint32_t>(reverseDsdByteForDop(firstByte)) << 8u)
                | (marker << 16u);
        }
        else if (lane == 0)
        {
            const int outputBase = frame * config.channels * 2;
            output[outputBase + channel] = firstByte;
            output[outputBase + config.channels + channel] = secondByte;
        }
    }

    if (lane < config.order)
    {
        pendingState->errorHistory[historyBase + lane] = errorHistory;
        pendingState->feedbackHistory[historyBase + lane] = feedbackHistory;
        pendingState->relaxedFeedbackHistory[historyBase + lane] = relaxedFeedbackHistory;
    }
    if (lane == 0)
    {
        pendingState->ditherState[channel] = ditherState;
        pendingState->idleRunFrames[channel] = idleRunFrames;
        pendingState->idleLocked[channel] = idleLocked;
        pendingState->previousSamples[channel] = previousSample;
        pendingState->adaptiveEnvelope[channel] = adaptiveEnvelope;
        pendingState->ecCorrelatedError[channel] = ecCorrelatedError;
        {
            const int firBase =
                channel * echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength;
            for (int index = 0; index < echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength; ++index)
                pendingState->preModulatorHistory[firBase + index] = preModulatorHistory[index];
        }
        pendingState->channelPeakFeedback[channel] = channelPeakFeedback;
        pendingState->channelStabilityRecoveries[channel] =
            channelStabilityRecoveries;
        telemetry->channelPeakFeedback[channel] = channelPeakFeedback;
        telemetry->channelStabilityRecoveries[channel] =
            channelStabilityRecoveries;
        if (channel == 0)
        {
            pendingState->transitionRampPosition = transitionRampPosition;
            pendingState->gainRampFramesRemaining = gainRampFramesRemaining;
            pendingState->currentUserGain = currentUserGain;
            pendingState->targetUserGain = targetUserGain;
            pendingState->targetGainRevision = requestedTargetGainRevision;
            pendingState->dopFrameIndex =
                initialDopFrameIndex + static_cast<uint64_t>(frames);
        }
    }
}

std::string cudaFailure(const char* operation, cudaError_t status)
{
    return std::string("native_cuda_sdm_") + operation + ":"
        + cudaGetErrorString(status);
}

bool checked(cudaError_t status, const char* operation, std::string& error)
{
    if (status == cudaSuccess)
        return true;
    error = cudaFailure(operation, status);
    return false;
}

size_t alignUp(size_t value, size_t alignment) noexcept
{
    return (value + alignment - 1u) & ~(alignment - 1u);
}

} // namespace

class CudaSdmProcessor::Impl
{
public:
    ~Impl()
    {
        release();
    }

    // True once selectDevice() succeeded; every allocation happens after that.
    bool touchedRuntime() const noexcept
    {
        return deviceIndex >= 0 || stream != nullptr
            || deviceInput != nullptr || deviceOutput != nullptr
            || deviceState[0] != nullptr || deviceState[1] != nullptr
            || hostInput != nullptr || hostOutput != nullptr || hostState != nullptr;
    }

    void release()
    {
        // NativeSdmProcessor::configure() releases unconditionally on every
        // playback configuration. Even cudaFree(nullptr) initialises the CUDA
        // runtime and creates the primary context (hundreds of MB of host
        // commit), so never touch the runtime when nothing was ever selected
        // or allocated.
        if (touchedRuntime())
        {
            if (deviceIndex >= 0)
                cudaSetDevice(deviceIndex);
            if (stream != nullptr)
                cudaStreamSynchronize(stream);
            cudaFree(deviceInput);
            cudaFree(deviceOutput);
            cudaFree(deviceState[0]);
            cudaFree(deviceState[1]);
            cudaFreeHost(hostInput);
            cudaFreeHost(hostOutput);
            cudaFreeHost(hostState);
            if (stream != nullptr)
                cudaStreamDestroy(stream);
            CudaDeviceSelector::releaseRuntime();
        }
        deviceInput = nullptr;
        deviceOutput = nullptr;
        deviceState[0] = nullptr;
        deviceState[1] = nullptr;
        hostInput = nullptr;
        hostOutput = nullptr;
        hostState = nullptr;
        stream = nullptr;
        inputCapacitySamples = 0;
        outputCapacityBytes = 0;
        deviceIndex = -1;
        selectedDeviceName.clear();
        committedStateIndex = 0;
        latestTelemetry = {};
        requestedTargetGain = 1.0;
        requestedTargetGainRevision = 0;
        warmupMilliseconds = 0.0;
        configured = false;
    }

    bool selectDevice(std::string& error)
    {
        CudaDeviceSelection selection;
        if (!CudaDeviceSelector::select(
                "native_cuda_sdm_",
                selection,
                error,
                CudaDeviceWorkload::DoublePrecision))
            return false;
        CudaDeviceSelector::retainRuntime();
        deviceIndex = selection.index;
        selectedDeviceName = std::move(selection.name);
        return true;
    }

    bool stateToDevice(
        const echo::SdmProcessorState& source,
        DeviceSdmState& target,
        std::string& error) const
    {
        const size_t channels = static_cast<size_t>(config.channels);
        const size_t historySize =
            channels * static_cast<size_t>(config.order);
        if (source.errorHistory.size() != historySize
            || source.feedbackHistory.size() != historySize
            || source.ditherState.size() != channels
            || source.idleRunFrames.size() != channels
            || source.idleLocked.size() != channels
            || source.previousSamples.size() != channels
            || (!source.adaptiveEnvelope.empty()
                && source.adaptiveEnvelope.size() != channels)
            || (!source.adaptiveRelaxedFeedbackHistory.empty()
                && source.adaptiveRelaxedFeedbackHistory.size() != historySize)
            || (!source.ecCorrelatedError.empty()
                && source.ecCorrelatedError.size() != channels))
        {
            error = "native_cuda_sdm_state_shape_mismatch";
            return false;
        }
        // Same legacy contract as SdmProcessor::restoreState: a missing
        // relaxed history restores at rest.
        const bool restoreRelaxed = config.adaptiveLoopGainEnabled != 0
            && !source.adaptiveRelaxedFeedbackHistory.empty();
        target = {};
        target.transitionRampPosition = source.transitionRampPosition;
        target.gainRampFramesRemaining = source.gainRampFramesRemaining;
        target.currentUserGain = source.currentUserGain;
        target.targetUserGain = source.targetUserGain;
        for (int channel = 0; channel < config.channels; ++channel)
        {
            for (int tap = 0; tap < config.order; ++tap)
            {
                const size_t sourceIndex =
                    static_cast<size_t>(channel * config.order + tap);
                const int targetIndex = channel * maximumSdmOrder + tap;
                target.errorHistory[targetIndex] =
                    source.errorHistory[sourceIndex];
                target.feedbackHistory[targetIndex] =
                    source.feedbackHistory[sourceIndex];
                target.relaxedFeedbackHistory[targetIndex] = restoreRelaxed
                    ? source.adaptiveRelaxedFeedbackHistory[sourceIndex]
                    : 0.0;
            }
            target.ditherState[channel] =
                source.ditherState[static_cast<size_t>(channel)];
            target.idleRunFrames[channel] =
                source.idleRunFrames[static_cast<size_t>(channel)];
            target.idleLocked[channel] =
                source.idleLocked[static_cast<size_t>(channel)];
            target.previousSamples[channel] =
                source.previousSamples[static_cast<size_t>(channel)];
            const double defaultEnvelope = config.adaptiveLoopGainEnabled == 0
                    && config.extendedCompensationEnabled != 0
                    && config.ecEnvelopeGated != 0
                ? 1.0
                : 0.0;
            target.adaptiveEnvelope[channel] = source.adaptiveEnvelope.empty()
                ? defaultEnvelope
                : std::max(0.0, source.adaptiveEnvelope[static_cast<size_t>(channel)]);
            const double restoredEcError = source.ecCorrelatedError.empty()
                ? 0.0
                : source.ecCorrelatedError[static_cast<size_t>(channel)];
            target.ecCorrelatedError[channel] = std::isfinite(restoredEcError)
                ? restoredEcError
                : 0.0;
            const size_t firSize = static_cast<size_t>(
                config.preModulatorStages * echo::sdmPreModulatorHistoryLength);
            const size_t firSourceBase = static_cast<size_t>(channel) * firSize;
            const int firTargetBase =
                channel * echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength;
            for (int index = 0; index < echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength; ++index)
            {
                target.preModulatorHistory[firTargetBase + index] =
                    source.preModulatorHistory.size() == channels * firSize
                        && static_cast<size_t>(index) < firSize
                    ? source.preModulatorHistory[firSourceBase + static_cast<size_t>(index)]
                    : 0.0;
            }
            target.channelPeakFeedback[channel] =
                source.peakFeedbackState;
        }
        target.channelStabilityRecoveries[0] =
            source.stabilityRecoveryCount;
        target.dopFrameIndex = source.dopFrameIndex;
        error.clear();
        return true;
    }

    bool deviceToState(
        const DeviceSdmState& source,
        echo::SdmProcessorState& target) const
    {
        if (!configured)
            return false;
        target = {};
        target.transitionRampPosition = source.transitionRampPosition;
        target.gainRampFramesRemaining = source.gainRampFramesRemaining;
        target.currentUserGain = source.currentUserGain;
        target.targetUserGain = source.targetUserGain;
        target.errorHistory.resize(
            static_cast<size_t>(config.channels * config.order));
        target.feedbackHistory.resize(
            static_cast<size_t>(config.channels * config.order));
        target.ditherState.resize(static_cast<size_t>(config.channels));
        target.idleRunFrames.resize(static_cast<size_t>(config.channels));
        target.idleLocked.resize(static_cast<size_t>(config.channels));
        target.previousSamples.resize(static_cast<size_t>(config.channels));
        target.adaptiveEnvelope.resize(static_cast<size_t>(config.channels));
        target.ecCorrelatedError.resize(static_cast<size_t>(config.channels));
        if (config.adaptiveLoopGainEnabled != 0)
        {
            target.adaptiveRelaxedFeedbackHistory.resize(
                static_cast<size_t>(config.channels * config.order));
        }
        for (int channel = 0; channel < config.channels; ++channel)
        {
            for (int tap = 0; tap < config.order; ++tap)
            {
                const size_t targetIndex =
                    static_cast<size_t>(channel * config.order + tap);
                const int sourceIndex = channel * maximumSdmOrder + tap;
                target.errorHistory[targetIndex] =
                    source.errorHistory[sourceIndex];
                target.feedbackHistory[targetIndex] =
                    source.feedbackHistory[sourceIndex];
                if (config.adaptiveLoopGainEnabled != 0)
                {
                    target.adaptiveRelaxedFeedbackHistory[targetIndex] =
                        source.relaxedFeedbackHistory[sourceIndex];
                }
            }
            target.ditherState[static_cast<size_t>(channel)] =
                source.ditherState[channel];
            target.idleRunFrames[static_cast<size_t>(channel)] =
                source.idleRunFrames[channel];
            target.idleLocked[static_cast<size_t>(channel)] =
                source.idleLocked[channel];
            target.previousSamples[static_cast<size_t>(channel)] =
                source.previousSamples[channel];
            target.adaptiveEnvelope[static_cast<size_t>(channel)] =
                source.adaptiveEnvelope[channel];
            target.ecCorrelatedError[static_cast<size_t>(channel)] =
                source.ecCorrelatedError[channel];
            const size_t firSize = static_cast<size_t>(
                config.preModulatorStages * echo::sdmPreModulatorHistoryLength);
            target.preModulatorHistory.resize(
                static_cast<size_t>(config.channels) * firSize);
            const int firSourceBase =
                channel * echo::sdmPreModulatorMaxStages * echo::sdmPreModulatorHistoryLength;
            const size_t firTargetBase = static_cast<size_t>(channel) * firSize;
            for (size_t index = 0; index < firSize; ++index)
            {
                target.preModulatorHistory[firTargetBase + index] =
                    source.preModulatorHistory[firSourceBase + static_cast<int>(index)];
            }
            target.peakFeedbackState = std::max(
                target.peakFeedbackState,
                static_cast<double>(source.channelPeakFeedback[channel]));
            target.stabilityRecoveryCount +=
                source.channelStabilityRecoveries[channel];
        }
        target.dopFrameIndex = source.dopFrameIndex;
        return true;
    }

    bool process(
        const float* interleaved,
        int frames,
        SdmOutputFormat format,
        size_t outputBytes,
        std::string& error)
    {
        if (!configured || interleaved == nullptr || frames <= 0)
        {
            error = "native_cuda_sdm_invalid_input";
            return false;
        }
        const size_t inputSamples =
            static_cast<size_t>(frames)
            * static_cast<size_t>(config.channels);
        const size_t telemetryOffset =
            alignUp(outputBytes, alignof(DeviceSdmTelemetry));
        const size_t transferBytes =
            telemetryOffset + sizeof(DeviceSdmTelemetry);
        if (inputSamples > inputCapacitySamples
            || transferBytes > outputCapacityBytes)
        {
            error = "native_cuda_sdm_block_too_large";
            return false;
        }
        if (!CudaDeviceSelector::activate(
                deviceIndex,
                "native_cuda_sdm_",
                error))
            return false;

        const int pendingStateIndex = 1 - committedStateIndex;
        std::memcpy(
            hostInput,
            interleaved,
            inputSamples * sizeof(float));
        if (!checked(cudaMemcpyAsync(
                         deviceInput,
                         hostInput,
                         inputSamples * sizeof(float),
                         cudaMemcpyHostToDevice,
                         stream),
                     "copy_input_failed",
                     error))
            return false;

        auto* deviceTelemetry = reinterpret_cast<DeviceSdmTelemetry*>(
            deviceOutput + telemetryOffset);
        processSdmKernel<<<1, sdmKernelThreads, 0, stream>>>(
            deviceInput,
            deviceOutput,
            deviceState[committedStateIndex],
            deviceState[pendingStateIndex],
            deviceTelemetry,
            config,
            requestedTargetGain,
            requestedTargetGainRevision,
            frames,
            format);
        if (!checked(cudaPeekAtLastError(), "kernel_launch_failed", error)
            || !checked(cudaMemcpyAsync(
                            hostOutput,
                            deviceOutput,
                            transferBytes,
                            cudaMemcpyDeviceToHost,
                            stream),
                        "copy_output_failed",
                        error)
            || !checked(cudaStreamSynchronize(stream), "stream_sync_failed", error))
            return false;

        latestTelemetry = *reinterpret_cast<const DeviceSdmTelemetry*>(
            hostOutput + telemetryOffset);
        committedStateIndex = pendingStateIndex;
        error.clear();
        return true;
    }

    DeviceSdmConfig config {};
    int deviceIndex = -1;
    cudaStream_t stream = nullptr;
    float* deviceInput = nullptr;
    uint8_t* deviceOutput = nullptr;
    DeviceSdmState* deviceState[2] { nullptr, nullptr };
    float* hostInput = nullptr;
    uint8_t* hostOutput = nullptr;
    DeviceSdmState* hostState = nullptr;
    size_t inputCapacitySamples = 0;
    size_t outputCapacityBytes = 0;
    int committedStateIndex = 0;
    DeviceSdmTelemetry latestTelemetry {};
    double requestedTargetGain = 1.0;
    uint64_t requestedTargetGainRevision = 0;
    std::string selectedDeviceName;
    double warmupMilliseconds = 0.0;
    bool configured = false;
};

CudaSdmProcessor::CudaSdmProcessor() : impl_(std::make_unique<Impl>()) {}
CudaSdmProcessor::~CudaSdmProcessor() = default;
CudaSdmProcessor::CudaSdmProcessor(CudaSdmProcessor&&) noexcept = default;
CudaSdmProcessor& CudaSdmProcessor::operator=(CudaSdmProcessor&&) noexcept = default;

void CudaSdmProcessor::release()
{
    impl_->release();
    // Also drops a context left behind by supportsRealtimeDoublePrecision()
    // when the admission probe decided against CUDA.
    CudaDeviceSelector::resetIfIdle();
}

bool CudaSdmProcessor::supportsRealtimeDoublePrecision(std::string& error)
{
    CudaDeviceSelection selection;
    if (!CudaDeviceSelector::select(
            "native_cuda_sdm_",
            selection,
            error,
            CudaDeviceWorkload::DoublePrecision))
        return false;
    // FP32:FP64 throughput is only a device-selection hint. Consumer GPUs can
    // report a large nominal ratio while still completing this small,
    // stateful two-channel workload inside its deadline. NativeSdmProcessor
    // performs the authoritative admission with real CPU/CUDA timings, a
    // 0.8 realtime budget, and bit-identical output before activation.
    error.clear();
    return true;
}

bool CudaSdmProcessor::configure(
    const echo::SdmProcessorConfig& config,
    const echo::SdmProcessorState& initialState,
    int maximumInputFrames,
    std::string& error)
{
    impl_->release();
    const int order =
        static_cast<int>(config.feedbackNumeratorCoefficients.size());
    if (config.channels <= 0 || config.channels > maximumSdmChannels
        || order <= 0 || order > maximumSdmOrder
        || config.feedbackDenominatorCoefficients.size()
            != config.feedbackNumeratorCoefficients.size()
        || maximumInputFrames <= 0)
    {
        error = "native_cuda_sdm_invalid_configuration";
        return false;
    }
    if (!impl_->selectDevice(error)
        || !checked(cudaStreamCreateWithFlags(
                        &impl_->stream, cudaStreamNonBlocking),
                    "stream_create_failed",
                    error))
    {
        impl_->release();
        return false;
    }

    impl_->config.channels = config.channels;
    impl_->config.order = order;
    impl_->config.transitionRampFrames = config.transitionRampFrames;
    impl_->config.idleLockFrames = config.idleLockFrames;
    impl_->config.ditherAmplitude = config.ditherAmplitude;
    impl_->config.inputLimit = config.inputLimit;
    impl_->config.stabilityLimit = config.stabilityLimit;
    impl_->config.profileHeadroomGain = config.profileHeadroomGain;
    impl_->config.idleLockThreshold = config.idleLockThreshold;
    impl_->config.idleUnlockThreshold = config.idleUnlockThreshold;
    impl_->config.feedbackStateLimit = config.feedbackStateLimit;
    impl_->config.preModulatorFactor =
        std::max(1, config.preModulatorUpsampleFactor);
    impl_->config.preModulatorStages =
        impl_->config.preModulatorFactor <= 1
            ? 0
            : impl_->config.preModulatorFactor == 2 ? 1 : 2;
    impl_->config.preModulatorBitsPerCarrier =
        echo::sdmBitsPerTransportSample / impl_->config.preModulatorFactor;
    impl_->config.decisionMode = static_cast<int>(config.decisionMode);
    impl_->config.adaptiveLoopGainEnabled = config.adaptiveLoopGainEnabled ? 1 : 0;
    impl_->config.extendedCompensationEnabled = config.extendedCompensationEnabled ? 1 : 0;
    impl_->config.adaptiveEnvelopeReleaseDecay = config.adaptiveEnvelopeReleaseDecay;
    impl_->config.adaptiveEnvelopeThreshold = config.adaptiveEnvelopeThreshold;
    impl_->config.adaptiveOverdriveRangeInverse = config.adaptiveOverdriveRangeInverse;
    impl_->config.ecCorrelationAlpha = config.ecTuning.correlationAlpha;
    impl_->config.ecBiasWeight = config.ecTuning.biasWeight;
    impl_->config.ecActivationLimit = config.ecTuning.activationLimit;
    impl_->config.ecMaxDecisionBias = config.ecTuning.maxDecisionBias;
    impl_->config.ecEnvelopeGated = config.ecTuning.envelopeGated ? 1 : 0;
    impl_->config.halfbandCenterTap = 0.5;
    for (double& evenTap : impl_->config.halfbandEvenTaps)
        evenTap = 0.0;
    if (config.preModulatorHalfbandTaps.size() >= echo::sdmPreModulatorHalfbandTapCount)
    {
        impl_->config.halfbandCenterTap =
            config.preModulatorHalfbandTaps[(echo::sdmPreModulatorHalfbandTapCount - 1) / 2];
        for (int tap = 0; tap < echo::sdmPreModulatorEvenTapCount; ++tap)
        {
            impl_->config.halfbandEvenTaps[tap] =
                2.0 * config.preModulatorHalfbandTaps[static_cast<size_t>(tap * 2)];
        }
    }
    for (int tap = 0; tap < order; ++tap)
    {
        impl_->config.feedbackNumerator[tap] =
            config.feedbackNumeratorCoefficients[static_cast<size_t>(tap)];
        impl_->config.feedbackDenominator[tap] =
            config.feedbackDenominatorCoefficients[static_cast<size_t>(tap)];
        impl_->config.relaxedFeedbackNumerator[tap] = 0.0;
        impl_->config.relaxedFeedbackDenominator[tap] = 0.0;
    }
    if (config.adaptiveLoopGainEnabled)
    {
        // The relaxed set must be a complete same-order design; a partial
        // vector would silently blend toward a different modulator.
        if (config.adaptiveRelaxedNumeratorCoefficients.size() != static_cast<size_t>(order)
            || config.adaptiveRelaxedDenominatorCoefficients.size() != static_cast<size_t>(order))
        {
            impl_->release();
            error = "native_cuda_sdm_invalid_adaptive_relaxed_ntf";
            return false;
        }
        for (int tap = 0; tap < order; ++tap)
        {
            impl_->config.relaxedFeedbackNumerator[tap] =
                config.adaptiveRelaxedNumeratorCoefficients[static_cast<size_t>(tap)];
            impl_->config.relaxedFeedbackDenominator[tap] =
                config.adaptiveRelaxedDenominatorCoefficients[static_cast<size_t>(tap)];
        }
    }

    impl_->inputCapacitySamples =
        static_cast<size_t>(maximumInputFrames)
        * static_cast<size_t>(config.channels);
    impl_->outputCapacityBytes =
        alignUp(
            impl_->inputCapacitySamples * sizeof(uint32_t),
            alignof(DeviceSdmTelemetry))
        + sizeof(DeviceSdmTelemetry);
    if (!checked(cudaMalloc(
                     &impl_->deviceInput,
                     impl_->inputCapacitySamples * sizeof(float)),
                 "allocate_input_failed",
                 error)
        || !checked(cudaMalloc(
                        &impl_->deviceOutput,
                        impl_->outputCapacityBytes),
                    "allocate_output_failed",
                    error)
        || !checked(cudaMalloc(
                        &impl_->deviceState[0],
                        sizeof(DeviceSdmState)),
                    "allocate_state_a_failed",
                    error)
        || !checked(cudaMalloc(
                        &impl_->deviceState[1],
                        sizeof(DeviceSdmState)),
                    "allocate_state_b_failed",
                    error)
        || !checked(cudaMallocHost(
                        &impl_->hostInput,
                        impl_->inputCapacitySamples * sizeof(float)),
                    "allocate_pinned_input_failed",
                    error)
        || !checked(cudaMallocHost(
                        &impl_->hostOutput,
                        impl_->outputCapacityBytes),
                    "allocate_pinned_output_failed",
                    error)
        || !checked(cudaMallocHost(
                        &impl_->hostState,
                        sizeof(DeviceSdmState)),
                    "allocate_state_snapshot_failed",
                    error))
    {
        impl_->release();
        return false;
    }

    impl_->configured = true;
    if (!reset(initialState, error))
    {
        impl_->release();
        return false;
    }
    const int warmupFrames = std::min(maximumInputFrames, 64);
    std::vector<float> warmupInput(
        static_cast<size_t>(warmupFrames * config.channels),
        0.0f);
    std::vector<uint32_t> warmupOutput;
    const auto started = std::chrono::steady_clock::now();
    if (!processDop(
            warmupInput.data(), warmupFrames, warmupOutput, error))
    {
        error = error.empty()
            ? "native_cuda_sdm_warmup_failed"
            : "native_cuda_sdm_warmup_failed:" + error;
        impl_->release();
        return false;
    }
    const auto finished = std::chrono::steady_clock::now();
    const double warmupMilliseconds =
        std::chrono::duration<double, std::milli>(finished - started).count();
    if (!reset(initialState, error))
    {
        impl_->release();
        return false;
    }
    impl_->warmupMilliseconds = warmupMilliseconds;
    error.clear();
    return true;
}

bool CudaSdmProcessor::reset(
    const echo::SdmProcessorState& state,
    std::string& error)
{
    if (!impl_->configured)
    {
        error = "native_cuda_sdm_not_configured";
        return false;
    }
    DeviceSdmState converted {};
    if (!impl_->stateToDevice(state, converted, error))
        return false;
    if (!CudaDeviceSelector::activate(
            impl_->deviceIndex,
            "native_cuda_sdm_",
            error))
        return false;
    ++impl_->requestedTargetGainRevision;
    if (impl_->requestedTargetGainRevision == 0)
        ++impl_->requestedTargetGainRevision;
    impl_->requestedTargetGain = converted.targetUserGain;
    converted.targetGainRevision = impl_->requestedTargetGainRevision;
    *impl_->hostState = converted;
    if (!checked(cudaMemcpyAsync(
                     impl_->deviceState[0],
                     impl_->hostState,
                     sizeof(DeviceSdmState),
                     cudaMemcpyHostToDevice,
                     impl_->stream),
                 "reset_state_a_failed",
                 error)
        || !checked(cudaMemcpyAsync(
                        impl_->deviceState[1],
                        impl_->hostState,
                        sizeof(DeviceSdmState),
                        cudaMemcpyHostToDevice,
                        impl_->stream),
                    "reset_state_b_failed",
                    error)
        || !checked(cudaStreamSynchronize(
                        impl_->stream),
                    "reset_stream_sync_failed",
                    error))
        return false;
    impl_->committedStateIndex = 0;
    impl_->latestTelemetry = {};
    for (int channel = 0; channel < impl_->config.channels; ++channel)
    {
        impl_->latestTelemetry.channelPeakFeedback[channel] =
            converted.channelPeakFeedback[channel];
        impl_->latestTelemetry.channelStabilityRecoveries[channel] =
            converted.channelStabilityRecoveries[channel];
    }
    error.clear();
    return true;
}

void CudaSdmProcessor::setTargetGain(double gain)
{
    if (!impl_->configured)
        return;
    const double normalized =
        std::max(0.0, std::min(1.0, std::isfinite(gain) ? gain : 1.0));
    if (std::abs(normalized - impl_->requestedTargetGain)
        <= std::numeric_limits<double>::epsilon())
        return;
    impl_->requestedTargetGain = normalized;
    ++impl_->requestedTargetGainRevision;
    if (impl_->requestedTargetGainRevision == 0)
        ++impl_->requestedTargetGainRevision;
}

bool CudaSdmProcessor::processDop(
    const float* interleaved,
    int frames,
    std::vector<uint32_t>& output,
    std::string& error)
{
    output.clear();
    const size_t outputSamples =
        static_cast<size_t>(std::max(0, frames))
        * static_cast<size_t>(impl_->config.channels);
    const size_t outputBytes = outputSamples * sizeof(uint32_t);
    if (!impl_->process(
            interleaved,
            frames,
            SdmOutputFormat::Dop,
            outputBytes,
            error))
        return false;
    const auto* data = reinterpret_cast<const uint32_t*>(impl_->hostOutput);
    output.assign(data, data + outputSamples);
    return true;
}

bool CudaSdmProcessor::processNativeDsd(
    const float* interleaved,
    int frames,
    std::vector<uint8_t>& output,
    std::string& error)
{
    output.clear();
    const size_t outputBytes =
        static_cast<size_t>(std::max(0, frames))
        * static_cast<size_t>(impl_->config.channels)
        * 2u;
    if (!impl_->process(
            interleaved,
            frames,
            SdmOutputFormat::NativeDsd,
            outputBytes,
            error))
        return false;
    output.assign(impl_->hostOutput, impl_->hostOutput + outputBytes);
    return true;
}

bool CudaSdmProcessor::copyLatestState(
    echo::SdmProcessorState& state) const
{
    if (!impl_->configured)
        return false;
    std::string error;
    if (!CudaDeviceSelector::activate(
            impl_->deviceIndex,
            "native_cuda_sdm_",
            error)
        || cudaMemcpy(
               impl_->hostState,
               impl_->deviceState[impl_->committedStateIndex],
               sizeof(DeviceSdmState),
               cudaMemcpyDeviceToHost) != cudaSuccess)
        return false;
    if (impl_->hostState->targetGainRevision
        != impl_->requestedTargetGainRevision)
    {
        impl_->hostState->targetUserGain = impl_->requestedTargetGain;
        impl_->hostState->gainRampFramesRemaining =
            impl_->config.transitionRampFrames;
        impl_->hostState->targetGainRevision =
            impl_->requestedTargetGainRevision;
    }
    return impl_->deviceToState(*impl_->hostState, state);
}

bool CudaSdmProcessor::active() const noexcept
{
    return impl_->configured;
}

std::string CudaSdmProcessor::deviceName() const
{
    return impl_->selectedDeviceName;
}

double CudaSdmProcessor::warmupMilliseconds() const noexcept
{
    return impl_->warmupMilliseconds;
}

double CudaSdmProcessor::peakFeedbackState() const noexcept
{
    if (!impl_->configured)
        return 0.0;
    double peak = 0.0;
    for (int channel = 0; channel < impl_->config.channels; ++channel)
        peak = std::max(
            peak,
            static_cast<double>(
                impl_->latestTelemetry.channelPeakFeedback[channel]));
    return peak;
}

uint64_t CudaSdmProcessor::stabilityRecoveryCount() const noexcept
{
    if (!impl_->configured)
        return 0;
    uint64_t count = 0;
    for (int channel = 0; channel < impl_->config.channels; ++channel)
        count += impl_->latestTelemetry.channelStabilityRecoveries[channel];
    return count;
}
