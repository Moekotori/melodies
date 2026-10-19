#include "PartitionedConvolutionEngine.h"
#include "ConvolutionLimits.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr double pi = 3.14159265358979323846;
constexpr double silenceThreshold = 1.0e-20;
std::vector<std::vector<double>> doubleTaps(const std::vector<std::vector<float>>& taps)
{
    size_t count = 0;
    for (const auto& channel : taps) {
        count += channel.size();
        if (count > convolutionMaxCoefficients) return {};
    }
    std::vector<std::vector<double>> result;
    for (const auto& channel : taps) result.emplace_back(channel.begin(), channel.end());
    return result;
}
}

PartitionedConvolutionEngine::PartitionedConvolutionEngine(
    const std::vector<std::vector<float>>& impulseTaps, int processingChannels)
    : PartitionedConvolutionEngine(doubleTaps(impulseTaps), processingChannels) {}

PartitionedConvolutionEngine::PartitionedConvolutionEngine(
    const std::vector<std::vector<double>>& impulseTaps,
    int processingChannels, const std::vector<ConvolutionRoute>& routes, int outputChannels)
    : processingChannels_(std::clamp(processingChannels, 1, 32)),
      outputChannels_(outputChannels > 0 ? outputChannels : processingChannels_),
      impulseChannels_(static_cast<int>(impulseTaps.size())),
      tapCount_(impulseTaps.empty() ? 0 : static_cast<int>(impulseTaps.front().size())),
      frontTapCount_(std::min(partitionSize, tapCount_)),
      tailPartitionCount_(tapCount_ > partitionSize
              ? (tapCount_ - partitionSize + partitionSize - 1) / partitionSize
              : 0),
      roots_(fftSize),
      directHistory_(
          static_cast<size_t>(processingChannels_),
          std::vector<double>(static_cast<size_t>(partitionSize), 0.0f)),
      inputPartition_(
          static_cast<size_t>(processingChannels_),
          std::vector<double>(static_cast<size_t>(partitionSize), 0.0f)),
      pendingTail_(
          static_cast<size_t>(outputChannels_),
          std::vector<double>(static_cast<size_t>(partitionSize), 0.0f)),
      overlap_(
          static_cast<size_t>(outputChannels_),
          std::vector<double>(static_cast<size_t>(partitionSize), 0.0f)),
      fftScratch_(fftSize),
      accumulationScratch_(fftSize)
{
    if (outputChannels_ < 1 || outputChannels_ > 32 || static_cast<int64_t>(tapCount_) * outputChannels_ > convolutionMaxCoefficients
        || processingChannels < 1 || processingChannels > 32 || impulseChannels_ <= 0 || impulseChannels_ > 32 || tapCount_ <= 0
        || tapCount_ > convolutionMaxTaps || static_cast<int64_t>(tapCount_) * processingChannels_ > convolutionMaxCoefficients
        || static_cast<int64_t>(tapCount_) * impulseChannels_ > convolutionMaxCoefficients
        || (routes.empty() && impulseChannels_ != 1 && impulseChannels_ != processingChannels_)
        || (!routes.empty() && routes.size() != impulseTaps.size()))
        return;
    for (const auto& channel : impulseTaps)
        if (static_cast<int>(channel.size()) != tapCount_)
            return;

    if (routes.empty()) {
        for (int channel = 0; channel < processingChannels_; ++channel) {
            routes_.push_back({channel, channel});
            routeImpulseChannels_.push_back(impulseChannels_ == 1 ? 0 : channel);
        }
    } else {
        for (size_t index = 0; index < routes.size(); ++index) {
            if (routes[index].input < 0 || routes[index].input >= processingChannels_
                || routes[index].output < 0 || routes[index].output >= outputChannels_) return;
            routes_.push_back(routes[index]);
            routeImpulseChannels_.push_back(static_cast<int>(index));
        }
    }

    for (int index = 0; index < fftSize; ++index)
    {
        const double angle = -2.0 * pi * index / fftSize;
        roots_[static_cast<size_t>(index)] = Complex(std::cos(angle), std::sin(angle));
    }

    frontTaps_.assign(
        static_cast<size_t>(impulseChannels_),
        std::vector<double>(static_cast<size_t>(frontTapCount_), 0.0f));
    for (int channel = 0; channel < impulseChannels_; ++channel)
        std::copy_n(
            impulseTaps[static_cast<size_t>(channel)].begin(),
            frontTapCount_,
            frontTaps_[static_cast<size_t>(channel)].begin());

    impulseSpectra_.assign(
        static_cast<size_t>(impulseChannels_),
        std::vector<Spectrum>(
            static_cast<size_t>(tailPartitionCount_),
            Spectrum(static_cast<size_t>(fftSize), Complex {})));
    for (int channel = 0; channel < impulseChannels_; ++channel)
    {
        for (int partition = 0; partition < tailPartitionCount_; ++partition)
        {
            auto& spectrum = impulseSpectra_[static_cast<size_t>(channel)][static_cast<size_t>(partition)];
            const int sourceOffset = partitionSize + partition * partitionSize;
            const int available = std::max(0, std::min(partitionSize, tapCount_ - sourceOffset));
            for (int sample = 0; sample < available; ++sample)
                spectrum[static_cast<size_t>(sample)] = impulseTaps[static_cast<size_t>(channel)][static_cast<size_t>(sourceOffset + sample)];
            fft(spectrum, false);
        }
    }

    inputSpectra_.assign(
        static_cast<size_t>(processingChannels_),
        std::vector<Spectrum>(
            static_cast<size_t>(std::max(1, tailPartitionCount_)),
            Spectrum(static_cast<size_t>(fftSize), Complex {})));
    prepared_ = true;
}

void PartitionedConvolutionEngine::resetForHardReplace()
{
    for (auto& channel : directHistory_)
        std::fill(channel.begin(), channel.end(), 0.0f);
    for (auto& channel : inputPartition_)
        std::fill(channel.begin(), channel.end(), 0.0f);
    for (auto& channel : pendingTail_)
        std::fill(channel.begin(), channel.end(), 0.0f);
    for (auto& channel : overlap_)
        std::fill(channel.begin(), channel.end(), 0.0f);
    for (auto& channel : inputSpectra_)
        for (auto& spectrum : channel)
            std::fill(spectrum.begin(), spectrum.end(), Complex {});
    directWriteIndex_ = 0;
    partitionWriteOffset_ = 0;
    completedPartitions_ = 0;
    tailFramesRemaining_.store(0, std::memory_order_release);
}

template <typename Sample>
void PartitionedConvolutionEngine::processBlock(
    PlanarAudioBuffer<Sample>& buffer,
    int startSample,
    int numSamples)
{
    if (! prepared_ || numSamples <= 0 || buffer.getNumChannels() != processingChannels_ || buffer.getChannelCapacity() < outputChannels_)
        return;

    const int channels = std::min(buffer.getNumChannels(), processingChannels_);
    int64_t remaining = tailFramesRemaining_.load(std::memory_order_relaxed);
    for (int sample = 0; sample < numSamples; ++sample)
    {
        bool hasInput = false;
        for (int channel = 0; channel < channels; ++channel)
        {
            const double input = buffer.getSample(channel, startSample + sample);
            hasInput = hasInput || std::abs(input) > silenceThreshold;
            directHistory_[static_cast<size_t>(channel)][static_cast<size_t>(directWriteIndex_)] = input;
            inputPartition_[static_cast<size_t>(channel)][static_cast<size_t>(partitionWriteOffset_)] = input;
        }

        for (int channel = 0; channel < outputChannels_; ++channel)
        {
            double output = pendingTail_[static_cast<size_t>(channel)][static_cast<size_t>(partitionWriteOffset_)];
            for (size_t route = 0; route < routes_.size(); ++route) {
                if (routes_[route].output != channel) continue;
                const auto& taps = frontTaps_[static_cast<size_t>(routeImpulseChannels_[route])];
                const auto& history = directHistory_[static_cast<size_t>(routes_[route].input)];
                for (int tap = 0; tap < frontTapCount_; ++tap) {
                    int historyIndex = directWriteIndex_ - tap;
                    if (historyIndex < 0) historyIndex += partitionSize;
                    output += taps[static_cast<size_t>(tap)] * history[static_cast<size_t>(historyIndex)];
                }
            }
            buffer.setSample(channel, startSample + sample, static_cast<double>(output));
        }

        if (hasInput)
            remaining = std::max<int64_t>(remaining, tapCount_ - 1);
        else if (remaining > 0)
            --remaining;

        directWriteIndex_ = (directWriteIndex_ + 1) % partitionSize;
        ++partitionWriteOffset_;
        if (partitionWriteOffset_ == partitionSize)
        {
            completeInputPartition();
            partitionWriteOffset_ = 0;
        }
    }
    tailFramesRemaining_.store(remaining, std::memory_order_release);
    buffer.setActiveChannels(outputChannels_);
}

int64_t PartitionedConvolutionEngine::tailFramesRemaining() const noexcept
{
    return std::max<int64_t>(0, tailFramesRemaining_.load(std::memory_order_acquire));
}

void PartitionedConvolutionEngine::fft(Spectrum& values, bool inverse) const noexcept
{
    for (int index = 1, reversed = 0; index < fftSize; ++index)
    {
        int bit = fftSize >> 1;
        for (; (reversed & bit) != 0; bit >>= 1)
            reversed ^= bit;
        reversed ^= bit;
        if (index < reversed)
            std::swap(values[static_cast<size_t>(index)], values[static_cast<size_t>(reversed)]);
    }

    for (int length = 2; length <= fftSize; length <<= 1)
    {
        const int half = length >> 1;
        const int rootStep = fftSize / length;
        for (int offset = 0; offset < fftSize; offset += length)
        {
            for (int index = 0; index < half; ++index)
            {
                Complex root = roots_[static_cast<size_t>(index * rootStep)];
                if (inverse)
                    root = std::conj(root);
                const Complex even = values[static_cast<size_t>(offset + index)];
                const Complex odd = values[static_cast<size_t>(offset + index + half)] * root;
                values[static_cast<size_t>(offset + index)] = even + odd;
                values[static_cast<size_t>(offset + index + half)] = even - odd;
            }
        }
    }

    if (inverse)
    {
        const double scale = 1.0 / fftSize;
        for (auto& value : values)
            value *= scale;
    }
}

void PartitionedConvolutionEngine::completeInputPartition() noexcept
{
    if (tailPartitionCount_ <= 0)
    {
        for (auto& channel : pendingTail_)
            std::fill(channel.begin(), channel.end(), 0.0f);
        ++completedPartitions_;
        return;
    }

    const int historySlot = static_cast<int>(completedPartitions_ % static_cast<uint64_t>(tailPartitionCount_));
    for (int channel = 0; channel < processingChannels_; ++channel)
    {
        std::fill(fftScratch_.begin(), fftScratch_.end(), Complex {});
        for (int sample = 0; sample < partitionSize; ++sample)
            fftScratch_[static_cast<size_t>(sample)] = inputPartition_[static_cast<size_t>(channel)][static_cast<size_t>(sample)];
        fft(fftScratch_, false);
        auto& destination = inputSpectra_[static_cast<size_t>(channel)][static_cast<size_t>(historySlot)];
        std::copy(fftScratch_.begin(), fftScratch_.end(), destination.begin());
    }
    for (int channel = 0; channel < outputChannels_; ++channel)
    {
        std::fill(accumulationScratch_.begin(), accumulationScratch_.end(), Complex {});
        const int availablePartitions = static_cast<int>(std::min<uint64_t>(completedPartitions_ + 1, tailPartitionCount_));
        for (size_t route = 0; route < routes_.size(); ++route) {
            if (routes_[route].output != channel) continue;
            for (int partition = 0; partition < availablePartitions; ++partition) {
                const int inputSlot = static_cast<int>((completedPartitions_ + tailPartitionCount_ - partition) % tailPartitionCount_);
                const auto& inputSpectrum = inputSpectra_[static_cast<size_t>(routes_[route].input)][static_cast<size_t>(inputSlot)];
                const auto& impulseSpectrum = impulseSpectra_[static_cast<size_t>(routeImpulseChannels_[route])][static_cast<size_t>(partition)];
                for (int bin = 0; bin < fftSize; ++bin)
                    accumulationScratch_[static_cast<size_t>(bin)] += inputSpectrum[static_cast<size_t>(bin)] * impulseSpectrum[static_cast<size_t>(bin)];
            }
        }
        fft(accumulationScratch_, true);
        for (int sample = 0; sample < partitionSize; ++sample)
        {
            pendingTail_[static_cast<size_t>(channel)][static_cast<size_t>(sample)] =
                accumulationScratch_[static_cast<size_t>(sample)].real()
                + overlap_[static_cast<size_t>(channel)][static_cast<size_t>(sample)];
            overlap_[static_cast<size_t>(channel)][static_cast<size_t>(sample)] =
                accumulationScratch_[static_cast<size_t>(sample + partitionSize)].real();
        }
    }
    ++completedPartitions_;
}

template void PartitionedConvolutionEngine::processBlock<float>(PlanarAudioBuffer<float>&, int, int);
template void PartitionedConvolutionEngine::processBlock<double>(PlanarAudioBuffer<double>&, int, int);
} // namespace echo
