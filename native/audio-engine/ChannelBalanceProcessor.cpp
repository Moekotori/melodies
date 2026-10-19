#include "ChannelBalanceProcessor.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr double pi = 3.14159265358979323846;

double dbToGain(double db)
{
    return std::pow(10.0, db / 20.0);
}

double moveTowards(double current, double target, double step)
{
    if (std::abs(target - current) <= std::abs(step))
        return target;

    return current + (target > current ? std::abs(step) : -std::abs(step));
}

double sanitize(double value)
{
    return std::isfinite(value) ? value : 0.0;
}

ChannelBalanceMonoMode monoModeFromInt(int value)
{
    switch (value)
    {
        case static_cast<int>(ChannelBalanceMonoMode::SumToMono): return ChannelBalanceMonoMode::SumToMono;
        case static_cast<int>(ChannelBalanceMonoMode::LeftOnly): return ChannelBalanceMonoMode::LeftOnly;
        case static_cast<int>(ChannelBalanceMonoMode::RightOnly): return ChannelBalanceMonoMode::RightOnly;
        default: return ChannelBalanceMonoMode::Off;
    }
}

void applyMono(ChannelBalanceMonoMode mode, double left, double right, double& outputLeft, double& outputRight)
{
    switch (mode)
    {
        case ChannelBalanceMonoMode::SumToMono:
        {
            const double mono = (left + right) * 0.5;
            outputLeft = mono;
            outputRight = mono;
            break;
        }
        case ChannelBalanceMonoMode::LeftOnly:
            outputLeft = left;
            outputRight = 0.0;
            break;
        case ChannelBalanceMonoMode::RightOnly:
            outputLeft = 0.0;
            outputRight = right;
            break;
        case ChannelBalanceMonoMode::Off:
        default:
            outputLeft = left;
            outputRight = right;
            break;
    }
}
} // namespace

float clampChannelBalance(float value)
{
    if (! std::isfinite(value))
        return 0.0f;

    return std::max<double>(channelBalanceMinBalance, std::min<double>(channelBalanceMaxBalance, value));
}

float clampChannelGainDb(float value)
{
    if (! std::isfinite(value))
        return 0.0f;

    return std::max<double>(channelBalanceMinGainDb, std::min<double>(channelBalanceMaxGainDb, value));
}

float clampChannelBandGainDb(float value)
{
    if (! std::isfinite(value))
        return 0.0f;

    return std::max<double>(channelBalanceBandMinGainDb, std::min<double>(channelBalanceBandMaxGainDb, value));
}

float clampChannelDelayMs(float value)
{
    if (! std::isfinite(value))
        return 0.0f;

    return std::max<double>(channelBalanceMinDelayMs, std::min<double>(channelBalanceMaxDelayMs, value));
}

ChannelBalanceProcessor::ChannelBalanceProcessor()
{
    for (int band = 0; band < channelBalanceBandCount; ++band)
    {
        atomicLeftBandGainsDb[static_cast<size_t>(band)].store(0.0f, std::memory_order_relaxed);
        atomicRightBandGainsDb[static_cast<size_t>(band)].store(0.0f, std::memory_order_relaxed);
    }
}

void ChannelBalanceProcessor::prepare(double sampleRate, int maximumBlockSize, int channelCount)
{
    currentSampleRate = sampleRate > 0.0 ? sampleRate : 44100.0;
    preparedChannels = std::max<double>(1, channelCount);
    preparedBlockSize = std::max<double>(1, maximumBlockSize);
    const int maxDelaySamples = std::max<double>(1, static_cast<int>(std::ceil(currentSampleRate * channelBalanceMaxDelayMs / 1000.0f)));
    delayBufferLength = maxDelaySamples + 2;
    delayHistory.assign(static_cast<size_t>(std::min<double>(2, preparedChannels)), std::vector<double>(static_cast<size_t>(delayBufferLength), 0.0f));
    delayWriteIndex = 0;
    updateSmoothingSteps();
    reset();
}

void ChannelBalanceProcessor::reset()
{
    const auto target = readTargetSnapshot();
    smoothedBalance = target.balance;
    smoothedLeftGainDb = target.leftGainDb;
    smoothedRightGainDb = target.rightGainDb;
    smoothedLeftBandGainsDb = target.leftBandGainsDb;
    smoothedRightBandGainsDb = target.rightBandGainsDb;
    smoothedLeftDelayMs = target.leftDelayMs;
    smoothedRightDelayMs = target.rightDelayMs;
    enabledMix = target.enabled ? 1.0f : 0.0f;
    swapMix = target.swapLeftRight ? 1.0f : 0.0f;
    monoMix = 1.0f;
    invertLeftMix = target.invertLeft ? 1.0f : 0.0f;
    invertRightMix = target.invertRight ? 1.0f : 0.0f;
    constantPowerMix = target.constantPower ? 1.0f : 0.0f;
    previousMonoMode = target.monoMode;
    activeMonoMode = target.monoMode;
    targetMonoMode = target.monoMode;
    for (auto& channelHistory : delayHistory)
        std::fill(channelHistory.begin(), channelHistory.end(), 0.0f);
    lowBandState = {};
    highLowpassState = {};
    delayWriteIndex = 0;
    clippingRisk.store(false, std::memory_order_release);
    delayTailFrames.store(0, std::memory_order_release);
    filterTailActive.store(false, std::memory_order_release);
}

template <typename Sample>
void ChannelBalanceProcessor::processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0)
        return;

    const int channelCount = std::min<double>(buffer.getNumChannels(), preparedChannels);

    if (channelCount <= 0)
        return;

    constexpr double tailThreshold = 1.0e-7;
    bool inputActive = false;
    for (int channel = 0; channel < std::min<double>(2, channelCount) && !inputActive; ++channel)
    {
        const auto* samples = buffer.getReadPointer(channel, startSample);
        for (int sample = 0; sample < numSamples; ++sample)
        {
            if (std::abs(samples[sample]) > tailThreshold)
            {
                inputActive = true;
                break;
            }
        }
    }

    const auto target = readTargetSnapshot();
    updateSwitchTargets(target);
    const bool physicalSoloActive = target.enabled
        && (target.monoMode == ChannelBalanceMonoMode::LeftOnly || target.monoMode == ChannelBalanceMonoMode::RightOnly);

    balanceStep = (target.balance - smoothedBalance) / static_cast<double>(parameterSmoothingSamples);
    leftGainStepDb = (target.leftGainDb - smoothedLeftGainDb) / static_cast<double>(parameterSmoothingSamples);
    rightGainStepDb = (target.rightGainDb - smoothedRightGainDb) / static_cast<double>(parameterSmoothingSamples);
    for (int band = 0; band < channelBalanceBandCount; ++band)
    {
        leftBandGainStepsDb[static_cast<size_t>(band)] =
            (target.leftBandGainsDb[static_cast<size_t>(band)] - smoothedLeftBandGainsDb[static_cast<size_t>(band)]) / static_cast<double>(parameterSmoothingSamples);
        rightBandGainStepsDb[static_cast<size_t>(band)] =
            (target.rightBandGainsDb[static_cast<size_t>(band)] - smoothedRightBandGainsDb[static_cast<size_t>(band)]) / static_cast<double>(parameterSmoothingSamples);
    }
    leftDelayStepMs = (target.leftDelayMs - smoothedLeftDelayMs) / static_cast<double>(parameterSmoothingSamples);
    rightDelayStepMs = (target.rightDelayMs - smoothedRightDelayMs) / static_cast<double>(parameterSmoothingSamples);

    bool risk = false;

    if (channelCount == 1)
    {
        auto* leftSamples = buffer.getWritePointer(0, startSample);

        for (int sample = 0; sample < numSamples; ++sample)
        {
            smoothedBalance = moveTowards(smoothedBalance, target.balance, balanceStep);
            smoothedLeftGainDb = moveTowards(smoothedLeftGainDb, target.leftGainDb, leftGainStepDb);
            for (int band = 0; band < channelBalanceBandCount; ++band)
                smoothedLeftBandGainsDb[static_cast<size_t>(band)] = moveTowards(
                    smoothedLeftBandGainsDb[static_cast<size_t>(band)],
                    target.leftBandGainsDb[static_cast<size_t>(band)],
                    leftBandGainStepsDb[static_cast<size_t>(band)]);
            smoothedLeftDelayMs = moveTowards(smoothedLeftDelayMs, target.leftDelayMs, leftDelayStepMs);
            enabledMix = moveTowards(enabledMix, target.enabled ? 1.0 : 0.0, enabledStep);
            invertLeftMix = moveTowards(invertLeftMix, target.invertLeft ? 1.0 : 0.0, invertLeftStep);
            constantPowerMix = moveTowards(constantPowerMix, target.constantPower ? 1.0 : 0.0, constantPowerStep);

            double linearLeft = 1.0;
            double linearRight = 1.0;
            double constantLeft = 1.0;
            double constantRight = 1.0;
            calculateBalanceGains(smoothedBalance, false, linearLeft, linearRight);
            calculateBalanceGains(smoothedBalance, true, constantLeft, constantRight);
            const double balanceLeft = linearLeft + (constantLeft - linearLeft) * constantPowerMix;

            const double dry = leftSamples[sample];
            const double inverted = dry * (1.0 - (2.0 * invertLeftMix));
            const double wet = applyBandCompensation(0, inverted * balanceLeft * dbToGain(smoothedLeftGainDb), smoothedLeftBandGainsDb);
            pushDelaySample(0, wet);
            const double delayedWet = readDelaySample(0, smoothedLeftDelayMs);
            delayWriteIndex = (delayWriteIndex + 1) % delayBufferLength;
            const double mixed = dry + (delayedWet - dry) * enabledMix;
            leftSamples[sample] = sanitize(mixed);

            if (std::abs(leftSamples[sample]) > 0.98)
                risk = true;
        }

        clippingRisk.store(risk, std::memory_order_release);
        const int64_t delayFrames = inputActive
            ? static_cast<int64_t>(std::ceil(currentSampleRate * smoothedLeftDelayMs / 1000.0))
            : std::max<int64_t>(0, delayTailFrames.load(std::memory_order_relaxed) - numSamples);
        delayTailFrames.store(delayFrames, std::memory_order_release);
        filterTailActive.store(
            (enabledMix > 0.0 || target.enabled)
                && (std::abs(lowBandState[0]) > tailThreshold
                    || std::abs(highLowpassState[0]) > tailThreshold),
            std::memory_order_release);
        return;
    }

    auto* leftSamples = buffer.getWritePointer(0, startSample);
    auto* rightSamples = buffer.getWritePointer(1, startSample);

    for (int sample = 0; sample < numSamples; ++sample)
    {
        smoothedBalance = moveTowards(smoothedBalance, target.balance, balanceStep);
        smoothedLeftGainDb = moveTowards(smoothedLeftGainDb, target.leftGainDb, leftGainStepDb);
        smoothedRightGainDb = moveTowards(smoothedRightGainDb, target.rightGainDb, rightGainStepDb);
        for (int band = 0; band < channelBalanceBandCount; ++band)
        {
            smoothedLeftBandGainsDb[static_cast<size_t>(band)] = moveTowards(
                smoothedLeftBandGainsDb[static_cast<size_t>(band)],
                target.leftBandGainsDb[static_cast<size_t>(band)],
                leftBandGainStepsDb[static_cast<size_t>(band)]);
            smoothedRightBandGainsDb[static_cast<size_t>(band)] = moveTowards(
                smoothedRightBandGainsDb[static_cast<size_t>(band)],
                target.rightBandGainsDb[static_cast<size_t>(band)],
                rightBandGainStepsDb[static_cast<size_t>(band)]);
        }
        smoothedLeftDelayMs = moveTowards(smoothedLeftDelayMs, target.leftDelayMs, leftDelayStepMs);
        smoothedRightDelayMs = moveTowards(smoothedRightDelayMs, target.rightDelayMs, rightDelayStepMs);
        enabledMix = moveTowards(enabledMix, target.enabled ? 1.0 : 0.0, enabledStep);
        swapMix = moveTowards(swapMix, target.swapLeftRight ? 1.0 : 0.0, swapStep);
        monoMix = moveTowards(monoMix, 1.0, monoStep);
        invertLeftMix = moveTowards(invertLeftMix, target.invertLeft ? 1.0 : 0.0, invertLeftStep);
        invertRightMix = moveTowards(invertRightMix, target.invertRight ? 1.0 : 0.0, invertRightStep);
        constantPowerMix = moveTowards(constantPowerMix, target.constantPower ? 1.0 : 0.0, constantPowerStep);

        double linearLeft = 1.0;
        double linearRight = 1.0;
        double constantLeft = 1.0;
        double constantRight = 1.0;
        calculateBalanceGains(smoothedBalance, false, linearLeft, linearRight);
        calculateBalanceGains(smoothedBalance, true, constantLeft, constantRight);
        const double balanceLeft = linearLeft + (constantLeft - linearLeft) * constantPowerMix;
        const double balanceRight = linearRight + (constantRight - linearRight) * constantPowerMix;

        const double dryLeft = leftSamples[sample];
        const double dryRight = rightSamples[sample];

        const double swappedLeft = dryLeft + (dryRight - dryLeft) * swapMix;
        const double swappedRight = dryRight + (dryLeft - dryRight) * swapMix;
        const double invertedLeft = swappedLeft * (1.0 - (2.0 * invertLeftMix));
        const double invertedRight = swappedRight * (1.0 - (2.0 * invertRightMix));
        const double balancedLeft = applyBandCompensation(0, invertedLeft * balanceLeft * dbToGain(smoothedLeftGainDb), smoothedLeftBandGainsDb);
        const double balancedRight = applyBandCompensation(1, invertedRight * balanceRight * dbToGain(smoothedRightGainDb), smoothedRightBandGainsDb);

        double previousMonoLeft = balancedLeft;
        double previousMonoRight = balancedRight;
        double activeMonoLeft = balancedLeft;
        double activeMonoRight = balancedRight;
        applyMono(previousMonoMode, balancedLeft, balancedRight, previousMonoLeft, previousMonoRight);
        applyMono(activeMonoMode, balancedLeft, balancedRight, activeMonoLeft, activeMonoRight);

        const double wetLeft = previousMonoLeft + (activeMonoLeft - previousMonoLeft) * monoMix;
        const double wetRight = previousMonoRight + (activeMonoRight - previousMonoRight) * monoMix;
        pushDelaySample(0, wetLeft);
        pushDelaySample(1, wetRight);
        const double delayedWetLeft = readDelaySample(0, smoothedLeftDelayMs);
        const double delayedWetRight = readDelaySample(1, smoothedRightDelayMs);
        delayWriteIndex = (delayWriteIndex + 1) % delayBufferLength;
        const double outputLeft = physicalSoloActive ? delayedWetLeft : dryLeft + (delayedWetLeft - dryLeft) * enabledMix;
        const double outputRight = physicalSoloActive ? delayedWetRight : dryRight + (delayedWetRight - dryRight) * enabledMix;

        leftSamples[sample] = sanitize(outputLeft);
        rightSamples[sample] = sanitize(outputRight);

        if (std::abs(leftSamples[sample]) > 0.98 || std::abs(rightSamples[sample]) > 0.98)
            risk = true;
    }

    // First version intentionally affects only channel 0/1. Additional channels
    // are preserved for future expansion into a full channel matrix.
    clippingRisk.store(risk, std::memory_order_release);
    const int64_t delayFrames = inputActive
        ? static_cast<int64_t>(std::ceil(
            currentSampleRate * std::max<double>(smoothedLeftDelayMs, smoothedRightDelayMs) / 1000.0))
        : std::max<int64_t>(0, delayTailFrames.load(std::memory_order_relaxed) - numSamples);
    delayTailFrames.store(delayFrames, std::memory_order_release);
    filterTailActive.store(
        (enabledMix > 0.0 || target.enabled)
            && (std::abs(lowBandState[0]) > tailThreshold
                || std::abs(lowBandState[1]) > tailThreshold
                || std::abs(highLowpassState[0]) > tailThreshold
                || std::abs(highLowpassState[1]) > tailThreshold),
        std::memory_order_release);
}

int64_t ChannelBalanceProcessor::tailFramesRemaining() const noexcept
{
    return std::max<int64_t>(
        delayTailFrames.load(std::memory_order_acquire),
        filterTailActive.load(std::memory_order_acquire) ? 1 : 0);
}

void ChannelBalanceProcessor::setState(const ChannelBalanceState& state)
{
    targetEnabled.store(state.enabled, std::memory_order_release);
    atomicBalance.store(clampChannelBalance(state.balance), std::memory_order_release);
    atomicLeftGainDb.store(clampChannelGainDb(state.leftGainDb), std::memory_order_release);
    atomicRightGainDb.store(clampChannelGainDb(state.rightGainDb), std::memory_order_release);
    for (int band = 0; band < channelBalanceBandCount; ++band)
    {
        atomicLeftBandGainsDb[static_cast<size_t>(band)].store(clampChannelBandGainDb(state.leftBandGainsDb[static_cast<size_t>(band)]), std::memory_order_release);
        atomicRightBandGainsDb[static_cast<size_t>(band)].store(clampChannelBandGainDb(state.rightBandGainsDb[static_cast<size_t>(band)]), std::memory_order_release);
    }
    atomicLeftDelayMs.store(clampChannelDelayMs(state.leftDelayMs), std::memory_order_release);
    atomicRightDelayMs.store(clampChannelDelayMs(state.rightDelayMs), std::memory_order_release);
    targetSwapLeftRight.store(state.swapLeftRight, std::memory_order_release);
    atomicMonoMode.store(static_cast<int>(state.monoMode), std::memory_order_release);
    targetInvertLeft.store(state.invertLeft, std::memory_order_release);
    targetInvertRight.store(state.invertRight, std::memory_order_release);
    targetConstantPower.store(state.constantPower, std::memory_order_release);
}

ChannelBalanceState ChannelBalanceProcessor::getState() const
{
    ChannelBalanceState state;
    state.enabled = targetEnabled.load(std::memory_order_acquire);
    state.balance = atomicBalance.load(std::memory_order_acquire);
    state.leftGainDb = atomicLeftGainDb.load(std::memory_order_acquire);
    state.rightGainDb = atomicRightGainDb.load(std::memory_order_acquire);
    for (int band = 0; band < channelBalanceBandCount; ++band)
    {
        state.leftBandGainsDb[static_cast<size_t>(band)] = atomicLeftBandGainsDb[static_cast<size_t>(band)].load(std::memory_order_acquire);
        state.rightBandGainsDb[static_cast<size_t>(band)] = atomicRightBandGainsDb[static_cast<size_t>(band)].load(std::memory_order_acquire);
    }
    state.leftDelayMs = atomicLeftDelayMs.load(std::memory_order_acquire);
    state.rightDelayMs = atomicRightDelayMs.load(std::memory_order_acquire);
    state.swapLeftRight = targetSwapLeftRight.load(std::memory_order_acquire);
    state.monoMode = monoModeFromInt(atomicMonoMode.load(std::memory_order_acquire));
    state.invertLeft = targetInvertLeft.load(std::memory_order_acquire);
    state.invertRight = targetInvertRight.load(std::memory_order_acquire);
    state.constantPower = targetConstantPower.load(std::memory_order_acquire);
    return state;
}

void ChannelBalanceProcessor::resetToDefault()
{
    setState(ChannelBalanceState {});
}

bool ChannelBalanceProcessor::isEnabled() const
{
    return targetEnabled.load(std::memory_order_acquire);
}

bool ChannelBalanceProcessor::hasClippingRisk() const
{
    return clippingRisk.load(std::memory_order_acquire);
}

void ChannelBalanceProcessor::updateSmoothingSteps()
{
    parameterSmoothingSamples = std::max<double>(1, static_cast<int>(currentSampleRate * 0.02));
    switchSmoothingSamples = std::max<double>(1, static_cast<int>(currentSampleRate * 0.012));
}

ChannelBalanceProcessor::TargetSnapshot ChannelBalanceProcessor::readTargetSnapshot() const
{
    TargetSnapshot target;
    target.enabled = targetEnabled.load(std::memory_order_acquire);
    target.balance = clampChannelBalance(atomicBalance.load(std::memory_order_acquire));
    target.leftGainDb = clampChannelGainDb(atomicLeftGainDb.load(std::memory_order_acquire));
    target.rightGainDb = clampChannelGainDb(atomicRightGainDb.load(std::memory_order_acquire));
    for (int band = 0; band < channelBalanceBandCount; ++band)
    {
        target.leftBandGainsDb[static_cast<size_t>(band)] = clampChannelBandGainDb(atomicLeftBandGainsDb[static_cast<size_t>(band)].load(std::memory_order_acquire));
        target.rightBandGainsDb[static_cast<size_t>(band)] = clampChannelBandGainDb(atomicRightBandGainsDb[static_cast<size_t>(band)].load(std::memory_order_acquire));
    }
    target.leftDelayMs = clampChannelDelayMs(atomicLeftDelayMs.load(std::memory_order_acquire));
    target.rightDelayMs = clampChannelDelayMs(atomicRightDelayMs.load(std::memory_order_acquire));
    target.swapLeftRight = targetSwapLeftRight.load(std::memory_order_acquire);
    target.monoMode = monoModeFromInt(atomicMonoMode.load(std::memory_order_acquire));
    target.invertLeft = targetInvertLeft.load(std::memory_order_acquire);
    target.invertRight = targetInvertRight.load(std::memory_order_acquire);
    target.constantPower = targetConstantPower.load(std::memory_order_acquire);
    return target;
}

void ChannelBalanceProcessor::updateSwitchTargets(const TargetSnapshot& target)
{
    if (targetMonoMode != target.monoMode)
    {
        if (target.monoMode == ChannelBalanceMonoMode::LeftOnly || target.monoMode == ChannelBalanceMonoMode::RightOnly)
        {
            targetMonoMode = target.monoMode;
            activeMonoMode = target.monoMode;
            previousMonoMode = target.monoMode;
            monoMix = 1.0;
        }
        else
        {
            previousMonoMode = activeMonoMode;
            targetMonoMode = target.monoMode;
            activeMonoMode = target.monoMode;
            monoMix = 0.0;
        }
    }

    enabledStep = ((target.enabled ? 1.0 : 0.0) - enabledMix) / static_cast<double>(switchSmoothingSamples);
    swapStep = ((target.swapLeftRight ? 1.0 : 0.0) - swapMix) / static_cast<double>(switchSmoothingSamples);
    monoStep = (1.0 - monoMix) / static_cast<double>(switchSmoothingSamples);
    invertLeftStep = ((target.invertLeft ? 1.0 : 0.0) - invertLeftMix) / static_cast<double>(switchSmoothingSamples);
    invertRightStep = ((target.invertRight ? 1.0 : 0.0) - invertRightMix) / static_cast<double>(switchSmoothingSamples);
    constantPowerStep = ((target.constantPower ? 1.0 : 0.0) - constantPowerMix) / static_cast<double>(switchSmoothingSamples);
}

void ChannelBalanceProcessor::calculateBalanceGains(double balance, bool constantPower, double& leftGain, double& rightGain)
{
    const double safeBalance = clampChannelBalance(balance);

    if (! constantPower)
    {
        leftGain = safeBalance > 0.0 ? 1.0 - safeBalance : 1.0;
        rightGain = safeBalance < 0.0 ? 1.0 + safeBalance : 1.0;
        return;
    }

    const double pan = (safeBalance + 1.0) * pi * 0.25;
    const double compensation = std::sqrt(2.0);
    leftGain = std::min<double>(1.0, std::cos(pan) * compensation);
    rightGain = std::min<double>(1.0, std::sin(pan) * compensation);
}

double ChannelBalanceProcessor::applyBandCompensation(int channel, double sample, const std::array<double, channelBalanceBandCount>& bandGainsDb)
{
    const int safeChannel = channel <= 0 ? 0 : 1;
    const double lowCutoffHz = 200.0;
    const double highCutoffHz = 2000.0;
    const double lowAlpha = 1.0 - std::exp((-2.0 * pi * lowCutoffHz) / static_cast<double>(currentSampleRate));
    const double highAlpha = 1.0 - std::exp((-2.0 * pi * highCutoffHz) / static_cast<double>(currentSampleRate));

    lowBandState[static_cast<size_t>(safeChannel)] += lowAlpha * (sample - lowBandState[static_cast<size_t>(safeChannel)]);
    highLowpassState[static_cast<size_t>(safeChannel)] += highAlpha * (sample - highLowpassState[static_cast<size_t>(safeChannel)]);

    const double low = lowBandState[static_cast<size_t>(safeChannel)];
    const double high = sample - highLowpassState[static_cast<size_t>(safeChannel)];
    const double mid = sample - low - high;

    return sanitize(
        low * dbToGain(bandGainsDb[0])
        + mid * dbToGain(bandGainsDb[1])
        + high * dbToGain(bandGainsDb[2]));
}

double ChannelBalanceProcessor::readDelaySample(int channel, double delayMs) const
{
    if (channel < 0 || channel >= static_cast<int>(delayHistory.size()) || delayBufferLength <= 0)
        return 0.0;

    const double safeDelayMs = clampChannelDelayMs(delayMs);
    const double delaySamples = safeDelayMs * static_cast<double>(currentSampleRate) / 1000.0;
    const int wholeSamples = std::min<double>(delayBufferLength - 2, std::max<double>(0, static_cast<int>(std::floor(delaySamples))));
    const double fraction = delaySamples - static_cast<double>(wholeSamples);
    const auto& history = delayHistory[static_cast<size_t>(channel)];

    const auto wrapIndex = [this](int index) {
        while (index < 0)
            index += delayBufferLength;
        return index % delayBufferLength;
    };

    const double newer = history[static_cast<size_t>(wrapIndex(delayWriteIndex - wholeSamples))];
    const double older = history[static_cast<size_t>(wrapIndex(delayWriteIndex - wholeSamples - 1))];
    return sanitize(newer + (older - newer) * fraction);
}

void ChannelBalanceProcessor::pushDelaySample(int channel, double sample)
{
    if (channel < 0 || channel >= static_cast<int>(delayHistory.size()) || delayBufferLength <= 0)
        return;

    delayHistory[static_cast<size_t>(channel)][static_cast<size_t>(delayWriteIndex)] = sanitize(sample);
}

template void ChannelBalanceProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int);
template void ChannelBalanceProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int);
} // namespace echo
