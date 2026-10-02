#pragma once

#include "buffer.h"

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

    void resetForHardReplace();
    void continueForGapless() noexcept {}
    void processBlock(FloatAudioBuffer& buffer, int startSample, int numSamples);

    int64_t tailFramesRemaining() const noexcept;
    bool isTailDrained() const noexcept { return tailFramesRemaining() == 0; }
    int tapCount() const noexcept { return tapCount_; }
    bool isPrepared() const noexcept { return prepared_; }

private:
    using Complex = std::complex<float>;
    using Spectrum = std::vector<Complex>;

    void fft(Spectrum& values, bool inverse) const noexcept;
    void completeInputPartition() noexcept;

    int processingChannels_ = 0;
    int impulseChannels_ = 0;
    int tapCount_ = 0;
    int frontTapCount_ = 0;
    int tailPartitionCount_ = 0;
    bool prepared_ = false;

    std::vector<std::vector<float>> frontTaps_;
    std::vector<std::vector<Spectrum>> impulseSpectra_;
    Spectrum roots_;

    std::vector<std::vector<float>> directHistory_;
    std::vector<std::vector<float>> inputPartition_;
    std::vector<std::vector<float>> pendingTail_;
    std::vector<std::vector<float>> overlap_;
    std::vector<std::vector<Spectrum>> inputSpectra_;
    Spectrum fftScratch_;
    Spectrum accumulationScratch_;
    int directWriteIndex_ = 0;
    int partitionWriteOffset_ = 0;
    uint64_t completedPartitions_ = 0;
    std::atomic<int64_t> tailFramesRemaining_ { 0 };
};
} // namespace echo
