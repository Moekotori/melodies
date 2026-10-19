#include "PlaybackRateProcessor.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>

namespace
{
constexpr double resamplerPi = 3.14159265358979323846;
constexpr int sincTableResolution = 1024;
constexpr int sincTableMaximum = 32;
constexpr int windowTableResolution = 4096;

struct ResamplerLookupTables
{
    std::array<double, sincTableMaximum * sincTableResolution + 1> sinc {};
    std::array<double, windowTableResolution + 1> hannHalfWindow {};

    ResamplerLookupTables()
    {
        for (std::size_t index = 0; index < sinc.size(); ++index)
        {
            const double value = static_cast<double>(index) / sincTableResolution;
            sinc[index] = value < 1.0e-12
                ? 1.0
                : static_cast<double>(std::sin(resamplerPi * value) / (resamplerPi * value));
        }
        for (std::size_t index = 0; index < hannHalfWindow.size(); ++index)
        {
            const double value = static_cast<double>(index) / windowTableResolution;
            hannHalfWindow[index] = static_cast<double>(
                0.5 * (1.0 + std::cos(resamplerPi * value)));
        }
    }
};

const ResamplerLookupTables& resamplerLookupTables()
{
    static const ResamplerLookupTables tables;
    return tables;
}

template <std::size_t Size>
double interpolateLookup(const std::array<double, Size>& table, double scaledIndex) noexcept
{
    const double bounded = std::clamp(scaledIndex, 0.0, static_cast<double>(Size - 1));
    const auto index = static_cast<std::size_t>(bounded);
    const auto next = std::min(index + 1, Size - 1);
    const double fraction = static_cast<double>(bounded - static_cast<double>(index));
    return table[index] + (table[next] - table[index]) * fraction;
}
}

namespace echo
{
double PlaybackRateProcessor::sanitize(double value)
{
    return std::isfinite(value) ? value : 0.0f;
}

PlaybackRateProcessor::PlaybackRateProcessor()
{
    packedConfiguration.store(
        packConfiguration(1.0f, SpeedMode::Nightcore),
        std::memory_order_release);
}

void PlaybackRateProcessor::prepare(double sampleRateIn, int maximumBlockSize, int channelCountIn)
{
    (void) maximumBlockSize;
    sampleRate = sampleRateIn > 0.0 ? sampleRateIn : 44100.0;
    channelCount = std::max(1, channelCountIn);
    history.assign(
        static_cast<std::size_t>(channelCount),
        std::vector<double>(static_cast<std::size_t>(resamplerTaps), 0.0f));
    // Force the immutable sinc/window tables to initialize on the control
    // thread. The render callback only performs bounded lookups.
    (void) resamplerLookupTables();
    reset();
}

void PlaybackRateProcessor::reset()
{
    sourcePhase = 0.0;
    historyFrames = 0;
    for (auto& channel : history)
        std::fill(channel.begin(), channel.end(), 0.0f);
}

template <typename Sample>
void PlaybackRateProcessor::processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    (void) buffer;
    (void) startSample;
    (void) numSamples;
    // Rate conversion is performed before the DSP rack by
    // processResampledBlock(). This fixed post-rack stage remains a no-op so
    // the rack order and telemetry contract stay stable.
}

uint64_t PlaybackRateProcessor::packConfiguration(float rate, SpeedMode mode) noexcept
{
    uint32_t rateBits = 0;
    static_assert(sizeof(rateBits) == sizeof(rate));
    std::memcpy(&rateBits, &rate, sizeof(rateBits));
    return static_cast<uint64_t>(rateBits)
        | (static_cast<uint64_t>(static_cast<uint32_t>(mode)) << 32u);
}

PlaybackRateProcessor::Configuration PlaybackRateProcessor::unpackConfiguration(uint64_t packed) noexcept
{
    Configuration result;
    const uint32_t rateBits = static_cast<uint32_t>(packed & 0xffffffffu);
    std::memcpy(&result.rate, &rateBits, sizeof(result.rate));
    result.mode = static_cast<SpeedMode>(static_cast<uint32_t>(packed >> 32u));
    return result;
}

bool PlaybackRateProcessor::configure(float rate, SpeedMode mode, std::string* error)
{
    const float clamped = std::clamp<double>(sanitize(rate), playbackRateMin, playbackRateMax);
    (void) error;
    packedConfiguration.store(packConfiguration(clamped, mode), std::memory_order_release);
    return true;
}

bool PlaybackRateProcessor::setRate(float rate, std::string* error)
{
    return configure(rate, getSpeedMode(), error);
}

bool PlaybackRateProcessor::setSpeedMode(SpeedMode mode, std::string* error)
{
    return configure(getRate(), mode, error);
}

float PlaybackRateProcessor::getRate() const
{
    return getConfiguration().rate;
}

SpeedMode PlaybackRateProcessor::getSpeedMode() const
{
    return getConfiguration().mode;
}

PlaybackRateProcessor::Configuration PlaybackRateProcessor::getConfiguration() const
{
    return unpackConfiguration(packedConfiguration.load(std::memory_order_acquire));
}

bool PlaybackRateProcessor::isActive() const
{
    const float rate = getRate();
    return std::abs(rate - 1.0f) > 0.0001f;
}

int PlaybackRateProcessor::sourceFramesRequired(
    int outputFrames,
    const Configuration& configuration) const noexcept
{
    if (outputFrames <= 0)
        return 0;
    const double endPosition = sourcePhase
        + static_cast<double>(outputFrames) * static_cast<double>(configuration.rate);
    return std::max(1, static_cast<int>(std::ceil(endPosition)));
}

double PlaybackRateProcessor::historySample(int channel, int sourceIndex) const noexcept
{
    if (channel < 0 || channel >= static_cast<int>(history.size()) || sourceIndex >= 0)
        return 0.0f;
    const int historyIndex = historyFrames + sourceIndex;
    if (historyIndex < 0 || historyIndex >= historyFrames)
        return 0.0f;
    return history[static_cast<std::size_t>(channel)][static_cast<std::size_t>(historyIndex)];
}

template <typename Sample>
void PlaybackRateProcessor::retainHistory(
    const echo::PlanarAudioBuffer<Sample>& source,
    int sourceStart,
    int sourceFrames) noexcept
{
    if (sourceFrames <= 0 || history.empty())
        return;
    const int sourceChannels = source.getNumChannels();
    const int retained = std::min(resamplerTaps, historyFrames + sourceFrames);
    for (int channel = 0; channel < channelCount; ++channel)
    {
        auto& destination = history[static_cast<std::size_t>(channel)];
        const int sourceChannel = std::min(channel, std::max(0, sourceChannels - 1));
        for (int index = 0; index < retained; ++index)
        {
            const int combinedIndex = historyFrames + sourceFrames - retained + index;
            destination[static_cast<std::size_t>(index)] = combinedIndex < historyFrames
                ? destination[static_cast<std::size_t>(combinedIndex)]
                : source.getSample(sourceChannel, sourceStart + combinedIndex - historyFrames);
        }
    }
    historyFrames = retained;
}

template <typename Sample>
int PlaybackRateProcessor::processResampledBlock(
    const echo::PlanarAudioBuffer<Sample>& source,
    int sourceFrames,
    echo::PlanarAudioBuffer<Sample>& output,
    int outputStart,
    int outputFrames,
    const Configuration& configuration) noexcept
{
    if (sourceFrames <= 0 || outputFrames <= 0 || configuration.rate <= 0.0)
        return 0;

    const int sourceChannels = source.getNumChannels();
    const int outputChannels = output.getNumChannels();
    const int channelsToRender = std::min(sourceChannels, outputChannels);
    const double rate = static_cast<double>(configuration.rate);
    const double cutoff = std::min(1.0, 1.0 / rate) * 0.94;
    const auto& lookup = resamplerLookupTables();
    int renderedFrames = 0;

    for (int frame = 0; frame < outputFrames; ++frame)
    {
        const double position = sourcePhase + static_cast<double>(frame) * rate;
        const int base = static_cast<int>(std::floor(position));
        if (base >= sourceFrames)
            break;
        const double fraction = position - static_cast<double>(base);
        std::array<double, resamplerTaps> weights {};
        double weightSum = 0.0;
        for (int tap = 0; tap < resamplerTaps; ++tap)
        {
            const double distance = fraction + static_cast<double>(tap);
            const double sinc = cutoff * interpolateLookup(
                lookup.sinc,
                cutoff * distance * sincTableResolution);
            const double window = interpolateLookup(
                lookup.hannHalfWindow,
                distance * windowTableResolution / static_cast<double>(resamplerTaps));
            weights[static_cast<std::size_t>(tap)] = sinc * window;
            weightSum += static_cast<double>(weights[static_cast<std::size_t>(tap)]);
        }

        for (int channel = 0; channel < channelsToRender; ++channel)
        {
            double weighted = 0.0;
            for (int tap = 0; tap < resamplerTaps; ++tap)
            {
                const int sourceIndex = base - tap;
                const double sample = sourceIndex >= 0
                    ? source.getSample(channel, sourceIndex)
                    : historySample(channel, sourceIndex);
                weighted += static_cast<double>(sample)
                    * weights[static_cast<std::size_t>(tap)];
            }
            output.setSample(
                channel,
                outputStart + frame,
                std::abs(weightSum) > 1.0e-12
                    ? static_cast<double>(weighted / weightSum)
                    : 0.0);
        }
        ++renderedFrames;
    }

    retainHistory(source, 0, sourceFrames);
    sourcePhase += static_cast<double>(renderedFrames) * rate - static_cast<double>(sourceFrames);
    sourcePhase = std::clamp(sourcePhase, -1.0, rate);
    return renderedFrames;
}

template <typename Sample>
void PlaybackRateProcessor::primeSourceHistory(
    const echo::PlanarAudioBuffer<Sample>& source,
    int sourceStart,
    int sourceFrames) noexcept
{
    if (sourceStart < 0
        || sourceFrames <= 0
        || sourceStart + sourceFrames > source.getNumSamples())
        return;
    retainHistory(source, sourceStart, sourceFrames);
    sourcePhase = 0.0;
}

template void PlaybackRateProcessor::primeSourceHistory<float>(const echo::PlanarAudioBuffer<float>&, int, int) noexcept;
template void PlaybackRateProcessor::primeSourceHistory<double>(const echo::PlanarAudioBuffer<double>&, int, int) noexcept;
template int PlaybackRateProcessor::processResampledBlock<float>(const echo::PlanarAudioBuffer<float>&, int, echo::PlanarAudioBuffer<float>&, int, int, const Configuration&) noexcept;
template int PlaybackRateProcessor::processResampledBlock<double>(const echo::PlanarAudioBuffer<double>&, int, echo::PlanarAudioBuffer<double>&, int, int, const Configuration&) noexcept;
template void PlaybackRateProcessor::retainHistory<float>(const echo::PlanarAudioBuffer<float>&, int, int) noexcept;
template void PlaybackRateProcessor::retainHistory<double>(const echo::PlanarAudioBuffer<double>&, int, int) noexcept;
template void PlaybackRateProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int);
template void PlaybackRateProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int);
} // namespace echo
