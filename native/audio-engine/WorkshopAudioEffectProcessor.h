#pragma once

#include "buffer.h"
#include "WorkshopToneRamp.h"
#include "VocalCutCenterFilter.h"

#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

namespace echo
{
enum class WorkshopAudioEffectType : uint8_t
{
    Bitcrusher = 0,
    Chiptune = 1,
    VocalCut = 2,
};

const char* workshopAudioEffectTypeName(WorkshopAudioEffectType type) noexcept;

struct WorkshopAudioEffectState
{
    bool operator==(const WorkshopAudioEffectState&) const = default;
    bool enabled = false;
    int bitDepth = 8;
    int sampleRateHz = 11025;
    float mix = 1.0f;
    float outputGainDb = 0.0f;
    WorkshopAudioEffectType effect = WorkshopAudioEffectType::Bitcrusher;
    float pulseMix = 0.8f;
    float triangleMix = 0.45f;
    float noiseMix = 0.2f;
    float drive = 2.8f;
    float vocalCutStrength = 1.0f;
    float vocalCutBassPreserveHz = 160.0f;
};

class WorkshopAudioEffectController final
{
public:
    bool setState(const WorkshopAudioEffectState& state, std::string* error = nullptr);
    WorkshopAudioEffectState getState() const noexcept;
    bool tryGetState(WorkshopAudioEffectState& state) const noexcept;
    bool isEnabled() const noexcept;
    uint64_t revision() const noexcept;

private:
    WorkshopAudioEffectState loadState() const noexcept;
    mutable std::mutex writerMutex_;
    std::atomic<uint64_t> sequence_ { 0 };
    std::atomic<bool> enabled_ { false };
    std::atomic<int> bitDepth_ { 8 };
    std::atomic<int> sampleRateHz_ { 11025 };
    std::atomic<float> mix_ { 1.0f };
    std::atomic<float> outputGainDb_ { 0.0f };
    std::atomic<uint8_t> effect_ { static_cast<uint8_t>(WorkshopAudioEffectType::Bitcrusher) };
    std::atomic<float> pulseMix_ { 0.8f };
    std::atomic<float> triangleMix_ { 0.45f };
    std::atomic<float> noiseMix_ { 0.2f };
    std::atomic<float> drive_ { 2.8f };
    std::atomic<float> vocalCutStrength_ { 1.0f };
    std::atomic<float> vocalCutBassPreserveHz_ { 160.0f };
    std::atomic<uint64_t> revision_ { 0 };
};

// Realtime runtime for the bounded Workshop audio effect contract. Each
// playback path owns its own runtime state while sharing one atomic controller.
class WorkshopAudioEffectProcessor final
{
public:
    explicit WorkshopAudioEffectProcessor(WorkshopAudioEffectController& controller);

    void prepare(double sampleRate, int maximumBlockSize, int channelCount);
    void reset();
    template <typename Sample>
    void processBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples);
    bool isActive() const noexcept;
    bool hasClippingRisk() const noexcept;

private:
    WorkshopAudioEffectController& controller_;
    friend struct DspPreparedRuntime;
    WorkshopAudioEffectState lastState_;
    std::vector<double> heldSamples_;
    double sourceSampleRate_ = 44100.0;
    double holdPhase_ = 1.0;
    double wetMix_ = 0.0;
    double chiptuneMix_ = 0.0;
    double vocalCutMix_ = 0.0;
    double vocalCutStrengthSmoothed_ = 1.0;
    double vocalCutStrengthTarget_ = 1.0;
    VocalCutCenterFilter vocalCutCenterFilter_;
    double chipHeldSample_ = 0.0;
    double chipBassSample_ = 0.0;
    double chipNoiseEnvelope_ = 0.0;
    double chipInputEnvelope_ = 0.0;
    double outputGainSmoothed_ = 1.0;
    WorkshopToneRamp toneRamp_;
    uint32_t chipNoiseLfsr_ = 0x1ffffu;
    int smoothingSamples_ = 1;
    int vocalCutStrengthRampRemaining_ = 0;
};
} // namespace echo
