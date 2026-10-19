#pragma once

#include "buffer.h"
#include "ConvolutionRouting.h"

#include <atomic>
#include <complex>
#include <cstdint>
#include <vector>

namespace echo
{
class PartitionedConvolutionEngine final
{
public:
    static constexpr int partitionSize = 128;
    static constexpr int fftSize = partitionSize * 2;

    PartitionedConvolutionEngine(
        const std::vector<std::vector<float>>& impulseTaps,
        int processingChannels);
    PartitionedConvolutionEngine(
        const std::vector<std::vector<double>>& impulseTaps,
        int processingChannels,
        const std::vector<ConvolutionRoute>& routes = {}, int outputChannels = 0);

    void resetForHardReplace();
    void continueForGapless() noexcept {}
    template <typename Sample>
    void processBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);

    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }
    int outputChannels() const noexcept { return outputChannels_; }
    int inputChannels() const noexcept { return processingChannels_; }
    int tapCount() const noexcept { return tapCount_; }
    bool isPrepared() const noexcept { return prepared_; }

private:
    using Complex = std::complex<double>;
    using Spectrum = std::vector<Complex>;

    void fft(Spectrum& values, bool inverse) const noexcept;
    void completeInputPartition() noexcept;

    int processingChannels_ = 0;
    int outputChannels_ = 0;
    int impulseChannels_ = 0;
    int tapCount_ = 0;
    int frontTapCount_ = 0;
    int tailPartitionCount_ = 0;
    bool prepared_ = false;

    std::vector<ConvolutionRoute> routes_;
    std::vector<int> routeImpulseChannels_;
    std::vector<std::vector<double>> frontTaps_;
    std::vector<std::vector<Spectrum>> impulseSpectra_;
    Spectrum roots_;

    std::vector<std::vector<double>> directHistory_;
    std::vector<std::vector<double>> inputPartition_;
    std::vector<std::vector<double>> pendingTail_;
    std::vector<std::vector<double>> overlap_;
    std::vector<std::vector<Spectrum>> inputSpectra_;
    Spectrum fftScratch_;
    Spectrum accumulationScratch_;
    int directWriteIndex_ = 0;
    int partitionWriteOffset_ = 0;
    uint64_t completedPartitions_ = 0;
    std::atomic<int64_t> tailFramesRemaining_ { 0 };
};
} // namespace echo
