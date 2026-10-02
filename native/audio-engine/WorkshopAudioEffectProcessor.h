#pragma once

#include "buffer.h"
#include "WorkshopToneRamp.h"

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
    void processBlock(FloatAudioBuffer& buffer, int startSample, int numSamples);
    bool isActive() const noexcept;
    bool hasClippingRisk() const noexcept;

private:
    struct VocalCutFilterState
    {
        float integrator1 = 0.0f;
        float integrator2 = 0.0f;
    };

    WorkshopAudioEffectController& controller_;
    WorkshopAudioEffectState lastState_;
    std::vector<float> heldSamples_;
    double sourceSampleRate_ = 44100.0;
    double holdPhase_ = 1.0;
    float wetMix_ = 0.0f;
    float chiptuneMix_ = 0.0f;
    float vocalCutMix_ = 0.0f;
    float vocalCutStrengthSmoothed_ = 1.0f;
    float vocalCutStrengthTarget_ = 1.0f;
    float vocalCutFilterA1_ = 1.0f;
    float vocalCutFilterA2_ = 0.0f;
    float vocalCutFilterA3_ = 0.0f;
    float vocalCutFilterTargetA1_ = 1.0f;
    float vocalCutFilterTargetA2_ = 0.0f;
    float vocalCutFilterTargetA3_ = 0.0f;
    float vocalCutBassPreserveHzTarget_ = 160.0f;
    VocalCutFilterState vocalCutFilterStage1_;
    VocalCutFilterState vocalCutFilterStage2_;
    float chipHeldSample_ = 0.0f;
    float chipBassSample_ = 0.0f;
    float chipNoiseEnvelope_ = 0.0f;
    float chipInputEnvelope_ = 0.0f;
    float outputGainSmoothed_ = 1.0f;
    WorkshopToneRamp toneRamp_;
    uint32_t chipNoiseLfsr_ = 0x1ffffu;
    int smoothingSamples_ = 1;
    int vocalCutStrengthRampRemaining_ = 0;
    int vocalCutFilterRampRemaining_ = 0;
};
} // namespace echo
