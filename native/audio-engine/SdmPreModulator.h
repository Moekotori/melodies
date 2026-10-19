#pragma once

#include <array>
#include <cstddef>
#include <vector>

namespace echo {

constexpr int sdmPreModulatorMaxStages = 2;
constexpr int sdmPreModulatorHalfbandTapCount = 63;
constexpr int sdmPreModulatorHistoryLength = 31;
constexpr int sdmPreModulatorEvenTapCount = sdmPreModulatorHistoryLength + 1;
constexpr int sdmPreModulatorMaxFactor = 4;
constexpr int sdmBitsPerTransportSample = 16;
constexpr double sdmPreModulatorKaiserBeta = 10.0;
constexpr int sdmUltimatePreModulatorFactor = 16;
constexpr int sdmUltimatePreModulatorTapCount = 255;
constexpr int sdmUltimatePreModulatorPhaseTapCount = 16;
constexpr double sdmUltimatePreModulatorKaiserBeta = 15.0;

enum class SdmPreModulatorProfile : int {
    Standard = 0,
    Heavy = 1,
    UltimateReference = 2,
    UltimateTransient = 3,
};

int resolveSdmPreModulatorStageCount(int transportSampleRate, bool heavyPreMod = false);
int resolveSdmPreModulatorUpsampleFactor(int transportSampleRate, bool heavyPreMod = false);
int resolveSdmPreModulatorDelayBits(int stageCount);

class SdmHalfbandUpsampler final
{
public:
    void configure(int channels, int stages);
    void reset();
    void resetChannel(int channel);

    int channels() const noexcept { return channels_; }
    int stages() const noexcept { return stages_; }
    int factor() const noexcept { return factor_; }
    int delayBits() const noexcept { return delayBits_; }
    const std::array<double, sdmPreModulatorHalfbandTapCount>& taps() const noexcept { return taps_; }

    void processSample(int channel, double input, double* output);
    void exportHistory(std::vector<double>& history) const;
    bool importHistory(const std::vector<double>& history);

private:
    void processStage(
        int channel,
        int stage,
        double input,
        double& even,
        double& odd);

    int channels_ = 1;
    int stages_ = 0;
    int factor_ = 1;
    int delayBits_ = 0;
    std::array<double, sdmPreModulatorHalfbandTapCount> taps_ {};
    std::array<double, sdmPreModulatorEvenTapCount> evenTaps_ {};
    std::vector<double> history_;
};

class SdmUltimatePolyphaseUpsampler final
{
public:
    void configure(int channels, SdmPreModulatorProfile profile);
    void reset();
    void resetChannel(int channel);

    bool active() const noexcept
    {
        return profile_ == SdmPreModulatorProfile::UltimateReference
            || profile_ == SdmPreModulatorProfile::UltimateTransient;
    }
    int channels() const noexcept { return channels_; }
    int factor() const noexcept { return active() ? sdmUltimatePreModulatorFactor : 1; }
    int delayBits() const noexcept { return delayBits_; }
    SdmPreModulatorProfile profile() const noexcept { return profile_; }
    int tailTransportFrames() const noexcept
    {
        return active() ? (sdmUltimatePreModulatorTapCount - 1) / sdmUltimatePreModulatorFactor : 0;
    }
    const std::vector<double>& taps() const noexcept { return taps_; }

    void processSample(int channel, double input, double* output);
    void exportHistory(std::vector<double>& history) const;
    bool importHistory(const std::vector<double>& history);

private:
    int channels_ = 1;
    int delayBits_ = 0;
    SdmPreModulatorProfile profile_ = SdmPreModulatorProfile::Standard;
    std::vector<double> taps_;
    std::array<std::array<double, sdmUltimatePreModulatorPhaseTapCount>, sdmUltimatePreModulatorFactor> phases_ {};
    std::vector<double> history_;
};

} // namespace echo
