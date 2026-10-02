#include "SdmPreModulator.h"

#include <algorithm>
#include <cmath>
#include <complex>

namespace echo {
namespace {

constexpr double pi = 3.14159265358979323846;

constexpr double besselI0(double value)
{
    double sum = 1.0;
    double term = 1.0;
    const double squared = (value * value) / 4.0;
    for (int index = 1; index <= 24; ++index)
    {
        term *= squared / static_cast<double>(index * index);
        sum += term;
        if (term < 1.0e-14)
            break;
    }
    return sum;
}

double sinc(double value)
{
    if (std::abs(value) < 1.0e-12)
        return 1.0;
    return std::sin(pi * value) / (pi * value);
}

std::array<double, sdmPreModulatorHalfbandTapCount> createHalfbandTaps()
{
    std::array<double, sdmPreModulatorHalfbandTapCount> taps {};
    const double i0Beta = 1.0 / besselI0(sdmPreModulatorKaiserBeta);
    const int center = (sdmPreModulatorHalfbandTapCount - 1) / 2;
    double oddSum = 0.0;
    for (int index = 0; index < sdmPreModulatorHalfbandTapCount; ++index)
    {
        const int offset = index - center;
        const double position = static_cast<double>(index) / static_cast<double>(sdmPreModulatorHalfbandTapCount - 1);
        const double centered = 2.0 * position - 1.0;
        const double window = besselI0(
            sdmPreModulatorKaiserBeta * std::sqrt(std::max(0.0, 1.0 - centered * centered))) * i0Beta;
        double tap = 0.5 * sinc(0.5 * static_cast<double>(offset)) * window;
        if (offset == 0)
            tap = 0.5;
        else if ((offset % 2) == 0)
            tap = 0.0;
        taps[static_cast<std::size_t>(index)] = tap;
        if ((offset % 2) != 0)
            oddSum += tap;
    }
    if (std::abs(oddSum) > 1.0e-12)
    {
        const double oddScale = 0.5 / oddSum;
        for (int index = 0; index < sdmPreModulatorHalfbandTapCount; ++index)
        {
            if (((index - center) % 2) != 0)
                taps[static_cast<std::size_t>(index)] *= oddScale;
        }
    }
    return taps;
}

double historySample(
    const double* history,
    double current,
    int delay)
{
    if (delay <= 0)
        return current;
    return history[delay - 1];
}

int nextPowerOfTwo(int value)
{
    int power = 1;
    while (power < value)
        power *= 2;
    return power;
}

void fftInPlace(std::vector<std::complex<double>>& values, bool inverse)
{
    const std::size_t size = values.size();
    for (std::size_t index = 1, reversed = 0; index < size; ++index)
    {
        std::size_t bit = size >> 1u;
        for (; (reversed & bit) != 0u; bit >>= 1u)
            reversed ^= bit;
        reversed ^= bit;
        if (index < reversed)
            std::swap(values[index], values[reversed]);
    }
    for (std::size_t length = 2; length <= size; length <<= 1u)
    {
        const double angle = (inverse ? 2.0 : -2.0) * pi / static_cast<double>(length);
        const std::complex<double> step(std::cos(angle), std::sin(angle));
        for (std::size_t start = 0; start < size; start += length)
        {
            std::complex<double> current(1.0, 0.0);
            for (std::size_t offset = 0; offset < length / 2u; ++offset)
            {
                const auto even = values[start + offset];
                const auto odd = values[start + offset + length / 2u] * current;
                values[start + offset] = even + odd;
                values[start + offset + length / 2u] = even - odd;
                current *= step;
            }
        }
    }
    if (inverse)
    {
        for (auto& value : values)
            value /= static_cast<double>(size);
    }
}

std::vector<double> createMinimumPhaseTaps(const std::vector<double>& linearTaps)
{
    const int fftSize = nextPowerOfTwo(std::max(8, static_cast<int>(linearTaps.size()) * 8));
    std::vector<std::complex<double>> spectrum(static_cast<std::size_t>(fftSize));
    for (std::size_t index = 0; index < linearTaps.size(); ++index)
        spectrum[index] = { linearTaps[index], 0.0 };
    fftInPlace(spectrum, false);
    for (auto& value : spectrum)
        value = { std::log(std::max(1.0e-15, std::abs(value))), 0.0 };
    fftInPlace(spectrum, true);
    for (int index = 1; index < fftSize / 2; ++index)
        spectrum[static_cast<std::size_t>(index)] *= 2.0;
    spectrum[static_cast<std::size_t>(fftSize / 2)] = { 0.0, 0.0 };
    for (int index = fftSize / 2 + 1; index < fftSize; ++index)
        spectrum[static_cast<std::size_t>(index)] = { 0.0, 0.0 };
    fftInPlace(spectrum, false);
    for (auto& value : spectrum)
        value = std::exp(value);
    fftInPlace(spectrum, true);
    std::vector<double> taps(linearTaps.size(), 0.0);
    for (std::size_t index = 0; index < taps.size(); ++index)
        taps[index] = spectrum[index].real();
    return taps;
}

std::vector<double> createUltimateTaps(SdmPreModulatorProfile profile)
{
    std::vector<double> taps(sdmUltimatePreModulatorTapCount, 0.0);
    const int center = (sdmUltimatePreModulatorTapCount - 1) / 2;
    const double cutoff = 1.0 / (2.0 * static_cast<double>(sdmUltimatePreModulatorFactor));
    const double i0Beta = 1.0 / besselI0(sdmUltimatePreModulatorKaiserBeta);
    for (int index = 0; index < sdmUltimatePreModulatorTapCount; ++index)
    {
        const int offset = index - center;
        const double position = static_cast<double>(index)
            / static_cast<double>(sdmUltimatePreModulatorTapCount - 1);
        const double centered = 2.0 * position - 1.0;
        const double window = besselI0(
            sdmUltimatePreModulatorKaiserBeta
                * std::sqrt(std::max(0.0, 1.0 - centered * centered))) * i0Beta;
        taps[static_cast<std::size_t>(index)] =
            2.0 * cutoff * sinc(2.0 * cutoff * static_cast<double>(offset)) * window;
    }
    if (profile == SdmPreModulatorProfile::UltimateTransient)
        taps = createMinimumPhaseTaps(taps);
    double sum = 0.0;
    for (const double tap : taps)
        sum += tap;
    if (std::abs(sum) > 1.0e-15)
    {
        const double scale = static_cast<double>(sdmUltimatePreModulatorFactor) / sum;
        for (double& tap : taps)
            tap *= scale;
    }
    return taps;
}

} // namespace

// Halfband FIR stages before the final linear interpolation to the 1-bit
// carrier. DSD64-class transports (176.4/192 kHz) run two stages (x4 FIR +
// x4 linear), DSD128/DSD256-class transports (352.8/384/705.6/768 kHz) run
// one stage (x2 FIR + x8 linear). DSD512 (1.4112/1.536 MHz) intentionally
// stays at pure x16 linear by default: its first image already sits above
// 1.39 MHz and an extra FIR would spend the realtime budget guarded by the
// 0.8 admission gate. The experimental heavyPreMod opt-in allows one stage
// (x2 FIR + x8 linear) on DSD512-class transports; configure still runs the
// full-chain realtime admission and refuses fail-closed. Must stay in sync
// with resolveSdmPreModulatorStageCount in src/main/audio/SdmFormatPlan.ts.
int resolveSdmPreModulatorStageCount(int transportSampleRate, bool heavyPreMod)
{
    const int safeRate = std::max(1, transportSampleRate);
    if (safeRate <= 200'000)
        return 2;
    if (safeRate <= 800'000)
        return 1;
    return heavyPreMod ? 1 : 0;
}

int resolveSdmPreModulatorUpsampleFactor(int transportSampleRate, bool heavyPreMod)
{
    return 1 << resolveSdmPreModulatorStageCount(transportSampleRate, heavyPreMod);
}

int resolveSdmPreModulatorDelayBits(int stageCount)
{
    const int safeStages = std::clamp(stageCount, 0, sdmPreModulatorMaxStages);
    const int center = (sdmPreModulatorHalfbandTapCount - 1) / 2;
    int delayBits = 0;
    int stageFactor = 1;
    for (int stage = 0; stage < safeStages; ++stage)
    {
        stageFactor *= 2;
        delayBits += center * (sdmBitsPerTransportSample / stageFactor);
    }
    return delayBits;
}

void SdmHalfbandUpsampler::configure(int channels, int stages)
{
    channels_ = std::max(1, channels);
    stages_ = std::clamp(stages, 0, sdmPreModulatorMaxStages);
    factor_ = 1 << stages_;
    delayBits_ = resolveSdmPreModulatorDelayBits(stages_);
    taps_ = createHalfbandTaps();
    for (int index = 0; index < sdmPreModulatorEvenTapCount; ++index)
        evenTaps_[static_cast<std::size_t>(index)] = 2.0 * taps_[static_cast<std::size_t>(index * 2)];
    history_.assign(
        static_cast<std::size_t>(channels_) * static_cast<std::size_t>(stages_) * sdmPreModulatorHistoryLength,
        0.0);
}

void SdmHalfbandUpsampler::reset()
{
    std::fill(history_.begin(), history_.end(), 0.0);
}

void SdmHalfbandUpsampler::resetChannel(int channel)
{
    if (channel < 0 || channel >= channels_ || stages_ == 0)
        return;
    const std::size_t base =
        static_cast<std::size_t>(channel) * static_cast<std::size_t>(stages_) * sdmPreModulatorHistoryLength;
    std::fill(
        history_.begin() + static_cast<std::ptrdiff_t>(base),
        history_.begin() + static_cast<std::ptrdiff_t>(base + static_cast<std::size_t>(stages_) * sdmPreModulatorHistoryLength),
        0.0);
}

void SdmHalfbandUpsampler::processStage(
    int channel,
    int stage,
    double input,
    double& even,
    double& odd)
{
    double* history = history_.data()
        + (static_cast<std::size_t>(channel) * static_cast<std::size_t>(stages_)
            + static_cast<std::size_t>(stage)) * sdmPreModulatorHistoryLength;
    constexpr int centerIndex = (sdmPreModulatorHalfbandTapCount - 1) / 2;
    constexpr int centerDelay = centerIndex / 2;
    even = 0.0;
    for (int tap = 0; tap < sdmPreModulatorEvenTapCount; ++tap)
        even += evenTaps_[static_cast<std::size_t>(tap)] * historySample(history, input, tap);
    odd = 2.0 * taps_[static_cast<std::size_t>(centerIndex)] * historySample(history, input, centerDelay);
    for (int index = sdmPreModulatorHistoryLength - 1; index > 0; --index)
        history[index] = history[index - 1];
    history[0] = input;
}

void SdmHalfbandUpsampler::processSample(int channel, double input, double* output)
{
    if (output == nullptr)
        return;
    if (stages_ <= 0 || channel < 0 || channel >= channels_)
    {
        output[0] = input;
        return;
    }

    double stageInput[sdmPreModulatorMaxFactor] { input };
    int count = 1;
    for (int stage = 0; stage < stages_; ++stage)
    {
        double stageOutput[sdmPreModulatorMaxFactor] {};
        for (int index = 0; index < count; ++index)
        {
            double even = 0.0;
            double odd = 0.0;
            processStage(channel, stage, stageInput[index], even, odd);
            stageOutput[index * 2] = even;
            stageOutput[index * 2 + 1] = odd;
        }
        count *= 2;
        for (int index = 0; index < count; ++index)
            stageInput[index] = stageOutput[index];
    }
    for (int index = 0; index < factor_; ++index)
        output[index] = stageInput[index];
}

void SdmHalfbandUpsampler::exportHistory(std::vector<double>& history) const
{
    history = history_;
}

bool SdmHalfbandUpsampler::importHistory(const std::vector<double>& history)
{
    if (history.size() != history_.size())
        return false;
    history_ = history;
    return true;
}

void SdmUltimatePolyphaseUpsampler::configure(int channels, SdmPreModulatorProfile profile)
{
    channels_ = std::max(1, channels);
    profile_ = profile;
    taps_.clear();
    for (auto& phase : phases_)
        phase.fill(0.0);
    history_.clear();
    delayBits_ = 0;
    if (!active())
        return;

    taps_ = createUltimateTaps(profile_);
    for (int phase = 0; phase < sdmUltimatePreModulatorFactor; ++phase)
    {
        for (int tap = 0; tap < sdmUltimatePreModulatorPhaseTapCount; ++tap)
        {
            const int tapIndex = phase + tap * sdmUltimatePreModulatorFactor;
            const double coefficient = tapIndex < static_cast<int>(taps_.size())
                ? taps_[static_cast<std::size_t>(tapIndex)]
                : 0.0;
            phases_[static_cast<std::size_t>(phase)][static_cast<std::size_t>(tap)] = coefficient;
        }
    }
    taps_.assign(sdmUltimatePreModulatorTapCount, 0.0);
    for (int phase = 0; phase < sdmUltimatePreModulatorFactor; ++phase)
    {
        for (int tap = 0; tap < sdmUltimatePreModulatorPhaseTapCount; ++tap)
        {
            const int tapIndex = phase + tap * sdmUltimatePreModulatorFactor;
            if (tapIndex < static_cast<int>(taps_.size()))
                taps_[static_cast<std::size_t>(tapIndex)] =
                    phases_[static_cast<std::size_t>(phase)][static_cast<std::size_t>(tap)];
        }
    }
    const auto peak = std::max_element(
        taps_.begin(), taps_.end(), [](double left, double right)
        {
            return std::abs(left) < std::abs(right);
        });
    delayBits_ = peak == taps_.end() ? 0 : static_cast<int>(std::distance(taps_.begin(), peak));
    history_.assign(
        static_cast<std::size_t>(channels_) * sdmUltimatePreModulatorPhaseTapCount,
        0.0);
}

void SdmUltimatePolyphaseUpsampler::reset()
{
    std::fill(history_.begin(), history_.end(), 0.0);
}

void SdmUltimatePolyphaseUpsampler::resetChannel(int channel)
{
    if (!active() || channel < 0 || channel >= channels_)
        return;
    const std::size_t base = static_cast<std::size_t>(channel) * sdmUltimatePreModulatorPhaseTapCount;
    std::fill(
        history_.begin() + static_cast<std::ptrdiff_t>(base),
        history_.begin() + static_cast<std::ptrdiff_t>(base + sdmUltimatePreModulatorPhaseTapCount),
        0.0);
}

void SdmUltimatePolyphaseUpsampler::processSample(int channel, double input, double* output)
{
    if (output == nullptr)
        return;
    if (!active() || channel < 0 || channel >= channels_)
    {
        output[0] = input;
        return;
    }
    double* history = history_.data()
        + static_cast<std::size_t>(channel) * sdmUltimatePreModulatorPhaseTapCount;
    for (int tap = sdmUltimatePreModulatorPhaseTapCount - 1; tap > 0; --tap)
        history[tap] = history[tap - 1];
    history[0] = input;
    for (int phase = 0; phase < sdmUltimatePreModulatorFactor; ++phase)
    {
        double value = 0.0;
        const auto& coefficients = phases_[static_cast<std::size_t>(phase)];
        for (int tap = 0; tap < sdmUltimatePreModulatorPhaseTapCount; ++tap)
            value += coefficients[static_cast<std::size_t>(tap)] * history[tap];
        output[phase] = value;
    }
}

void SdmUltimatePolyphaseUpsampler::exportHistory(std::vector<double>& history) const
{
    history = history_;
}

bool SdmUltimatePolyphaseUpsampler::importHistory(const std::vector<double>& history)
{
    if (history.size() != history_.size())
        return false;
    history_ = history;
    return true;
}

} // namespace echo
