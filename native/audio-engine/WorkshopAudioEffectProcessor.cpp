#include "WorkshopAudioEffectProcessor.h"

#include <algorithm>
#include <cmath>

namespace echo
{
namespace
{
constexpr int minimumBitDepth = 4;
constexpr int maximumBitDepth = 16;
constexpr int minimumSampleRateHz = 1000;
constexpr int maximumSampleRateHz = 192000;
constexpr double minimumOutputGainDb = -24.0;
constexpr double maximumOutputGainDb = 6.0;
constexpr double minimumChiptuneDrive = 1.0;
constexpr double maximumChiptuneDrive = 8.0;
constexpr double minimumVocalCutBassPreserveHz = 80.0;
constexpr double maximumVocalCutBassPreserveHz = 300.0;
constexpr double pi = 3.14159265358979323846;

double dbToGain(double db)
{
    return std::pow(10.0, db / 20.0);
}

double moveTowards(double current, double target, double step)
{
    if (std::abs(target - current) <= step)
        return target;
    return current + (target > current ? step : -step);
}

double triangleFold(double value)
{
    return (2.0 / pi) * std::asin(std::sin(value * (pi * 0.5)));
}

} // namespace

const char* workshopAudioEffectTypeName(WorkshopAudioEffectType type) noexcept
{
    if (type == WorkshopAudioEffectType::Chiptune) return "chiptune";
    if (type == WorkshopAudioEffectType::VocalCut) return "vocalCut";
    return "bitcrusher";
}

bool WorkshopAudioEffectController::setState(const WorkshopAudioEffectState& state, std::string* error)
{
    if (state.effect != WorkshopAudioEffectType::Bitcrusher
        && state.effect != WorkshopAudioEffectType::Chiptune
        && state.effect != WorkshopAudioEffectType::VocalCut)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_type";
        return false;
    }
    if (state.bitDepth < minimumBitDepth || state.bitDepth > maximumBitDepth)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_bit_depth";
        return false;
    }
    if (state.sampleRateHz < minimumSampleRateHz || state.sampleRateHz > maximumSampleRateHz)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_sample_rate";
        return false;
    }
    if (! std::isfinite(state.mix) || state.mix < 0.0f || state.mix > 1.0f)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_mix";
        return false;
    }
    if (! std::isfinite(state.outputGainDb)
        || state.outputGainDb < minimumOutputGainDb
        || state.outputGainDb > maximumOutputGainDb)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_output_gain";
        return false;
    }
    if (! std::isfinite(state.pulseMix) || state.pulseMix < 0.0f || state.pulseMix > 1.0f
        || ! std::isfinite(state.triangleMix) || state.triangleMix < 0.0f || state.triangleMix > 1.0f
        || ! std::isfinite(state.noiseMix) || state.noiseMix < 0.0f || state.noiseMix > 1.0f
        || state.pulseMix + state.triangleMix + state.noiseMix <= 0.00001f)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_chiptune_layers";
        return false;
    }
    if (! std::isfinite(state.drive)
        || state.drive < minimumChiptuneDrive
        || state.drive > maximumChiptuneDrive)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_chiptune_drive";
        return false;
    }
    if (! std::isfinite(state.vocalCutStrength)
        || state.vocalCutStrength < 0.0f || state.vocalCutStrength > 1.0f)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_vocal_cut_strength";
        return false;
    }
    if (! std::isfinite(state.vocalCutBassPreserveHz)
        || state.vocalCutBassPreserveHz < minimumVocalCutBassPreserveHz
        || state.vocalCutBassPreserveHz > maximumVocalCutBassPreserveHz)
    {
        if (error != nullptr) *error = "workshop_audio_effect_invalid_vocal_cut_bass_preserve";
        return false;
    }

    const std::lock_guard<std::mutex> lock(writerMutex_);
    sequence_.fetch_add(1);
    bitDepth_.store(state.bitDepth, std::memory_order_seq_cst);
    sampleRateHz_.store(state.sampleRateHz, std::memory_order_seq_cst);
    mix_.store(state.mix, std::memory_order_seq_cst);
    outputGainDb_.store(state.outputGainDb, std::memory_order_seq_cst);
    effect_.store(static_cast<uint8_t>(state.effect), std::memory_order_seq_cst);
    pulseMix_.store(state.pulseMix, std::memory_order_seq_cst);
    triangleMix_.store(state.triangleMix, std::memory_order_seq_cst);
    noiseMix_.store(state.noiseMix, std::memory_order_seq_cst);
    drive_.store(state.drive, std::memory_order_seq_cst);
    vocalCutStrength_.store(state.vocalCutStrength, std::memory_order_seq_cst);
    vocalCutBassPreserveHz_.store(state.vocalCutBassPreserveHz, std::memory_order_seq_cst);
    enabled_.store(state.enabled, std::memory_order_seq_cst);
    revision_.fetch_add(1, std::memory_order_acq_rel);
    sequence_.fetch_add(1);
    if (error != nullptr) error->clear();
    return true;
}

WorkshopAudioEffectState WorkshopAudioEffectController::getState() const noexcept
{
    const std::lock_guard<std::mutex> lock(writerMutex_);
    return loadState();
}

bool WorkshopAudioEffectController::tryGetState(WorkshopAudioEffectState& state) const noexcept
{
    // Never spin or lock on the audio thread. A concurrent preset publication
    // simply leaves the previous complete state in use for one more callback.
    const auto before = sequence_.load();
    if ((before & 1u) != 0u) return false;
    const auto candidate = loadState();
    if (sequence_.load() != before) return false;
    state = candidate;
    return true;
}

WorkshopAudioEffectState WorkshopAudioEffectController::loadState() const noexcept
{
    WorkshopAudioEffectState state;
    state.enabled = enabled_.load(std::memory_order_seq_cst);
    state.bitDepth = bitDepth_.load(std::memory_order_seq_cst);
    state.sampleRateHz = sampleRateHz_.load(std::memory_order_seq_cst);
    state.mix = mix_.load(std::memory_order_seq_cst);
    state.outputGainDb = outputGainDb_.load(std::memory_order_seq_cst);
    state.effect = static_cast<WorkshopAudioEffectType>(effect_.load(std::memory_order_seq_cst));
    state.pulseMix = pulseMix_.load(std::memory_order_seq_cst);
    state.triangleMix = triangleMix_.load(std::memory_order_seq_cst);
    state.noiseMix = noiseMix_.load(std::memory_order_seq_cst);
    state.drive = drive_.load(std::memory_order_seq_cst);
    state.vocalCutStrength = vocalCutStrength_.load(std::memory_order_seq_cst);
    state.vocalCutBassPreserveHz = vocalCutBassPreserveHz_.load(std::memory_order_seq_cst);
    return state;
}

bool WorkshopAudioEffectController::isEnabled() const noexcept
{
    return enabled_.load(std::memory_order_acquire)
        && mix_.load(std::memory_order_acquire) > 0.00001f;
}

uint64_t WorkshopAudioEffectController::revision() const noexcept
{
    return revision_.load(std::memory_order_acquire);
}

WorkshopAudioEffectProcessor::WorkshopAudioEffectProcessor(WorkshopAudioEffectController& controller)
    : controller_(controller)
{
}

void WorkshopAudioEffectProcessor::prepare(double sampleRate, int, int channelCount)
{
    sourceSampleRate_ = sampleRate > 0.0 ? sampleRate : 44100.0;
    heldSamples_.assign(static_cast<size_t>(std::max<double>(1, channelCount)), 0.0f);
    smoothingSamples_ = std::max<double>(1, static_cast<int>(sourceSampleRate_ * 0.008));
    reset();
}

void WorkshopAudioEffectProcessor::reset()
{
    std::fill(heldSamples_.begin(), heldSamples_.end(), 0.0f);
    holdPhase_ = 1.0;
    wetMix_ = 0.0f;
    chiptuneMix_ = 0.0f;
    vocalCutMix_ = 0.0f;
    vocalCutStrengthSmoothed_ = 1.0f;
    vocalCutStrengthTarget_ = 1.0f;
    vocalCutStrengthRampRemaining_ = 0;
    vocalCutCenterFilter_.prepare(sourceSampleRate_, 160.0f);
    chipHeldSample_ = 0.0f;
    chipBassSample_ = 0.0f;
    chipNoiseEnvelope_ = 0.0f;
    chipInputEnvelope_ = 0.0f;
    outputGainSmoothed_ = 1.0f;
    chipNoiseLfsr_ = 0x7fffu;
}

template <typename Sample>
void WorkshopAudioEffectProcessor::processBlock(PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0 || heldSamples_.empty())
        return;

    controller_.tryGetState(lastState_);
    const auto& state = lastState_;
    const double targetWetMix = state.enabled ? state.mix : 0.0;
    if (targetWetMix <= 0.0 && wetMix_ <= 0.0)
    {
        // The filter does not receive input while fully dry. Clear its history
        // so a later activation cannot revive stale center-band energy.
        vocalCutCenterFilter_.reset();
        chipHeldSample_ = 0.0;
        chipBassSample_ = 0.0;
        chipNoiseEnvelope_ = 0.0;
        chipInputEnvelope_ = 0.0;
        chipNoiseLfsr_ = 0x7fffu;
        holdPhase_ = 1.0;
        return;
    }

    const int channels = std::min<double>(buffer.getNumChannels(), static_cast<int>(heldSamples_.size()));
    const double outputGain = dbToGain(state.outputGainDb);
    const double captureIncrement = static_cast<double>(std::min<double>(1.0, static_cast<double>(state.sampleRateHz) / sourceSampleRate_));
    const double mixStep = 1.0 / static_cast<double>(smoothingSamples_);
    double targetChiptuneMix = state.effect == WorkshopAudioEffectType::Chiptune ? 1.0 : 0.0;
    double targetVocalCutMix = state.effect == WorkshopAudioEffectType::VocalCut ? 1.0 : 0.0;
    if (targetWetMix <= 0.0 && wetMix_ > 0.0)
    {
        // Keep the selected effect stable until its wet signal has fully faded.
        // Otherwise restoring a disabled module can briefly crossfade through a
        // different effect while the previous one is still audible.
        targetChiptuneMix = chiptuneMix_;
        targetVocalCutMix = vocalCutMix_;
    }
    else if (targetWetMix > 0.0 && wetMix_ <= 0.0)
    {
        // Effect selection is inaudible while fully dry, so switch it before
        // ramping wet mix. This prevents bitcrusher bleed on first activation.
        chiptuneMix_ = targetChiptuneMix;
        vocalCutMix_ = targetVocalCutMix;
        outputGainSmoothed_ = outputGain;
    }
    const double captureRate = std::min<double>(static_cast<double>(sourceSampleRate_), static_cast<double>(state.sampleRateHz));
    const double bassCutoff = std::clamp<double>(captureRate * 0.035, 80.0, 260.0);
    const double bassCoefficient = 1.0 - std::exp(-2.0 * pi * bassCutoff / std::max<double>(1.0, captureRate));
    const double noiseRelease = std::exp(-1.0 / std::max<double>(1.0, captureRate * 0.06));
    const double envelopeRelease = std::exp(-1.0 / std::max<double>(1.0, captureRate * 0.01));
    // Normalize before interpolation so near-zero layer totals cannot turn a
    // tiny slider step into an immediate change of the entire audible voice.
    const double targetLayerWeight = state.pulseMix + state.triangleMix + state.noiseMix;
    toneRamp_.setTarget({ static_cast<double>(state.bitDepth), captureIncrement,
        bassCoefficient, noiseRelease, envelopeRelease, state.pulseMix / targetLayerWeight,
        state.triangleMix / targetLayerWeight, state.noiseMix / targetLayerWeight,
        state.drive }, smoothingSamples_, wetMix_ <= 0.0);
    if (std::abs(state.vocalCutStrength - vocalCutStrengthTarget_) > 0.000001)
    {
        vocalCutStrengthTarget_ = state.vocalCutStrength;
        vocalCutStrengthRampRemaining_ = smoothingSamples_;
    }
    vocalCutCenterFilter_.setCutoff(state.vocalCutBassPreserveHz, smoothingSamples_, wetMix_ <= 0.0);
    if (targetVocalCutMix >= 1.0 && vocalCutMix_ >= 1.0)
    {
        // Dormant stylizers need no PCM history. Start a later effect selection
        // with fresh envelope/noise state instead of stale unrelated audio.
        chipHeldSample_ = chipBassSample_ = chipNoiseEnvelope_ = chipInputEnvelope_ = 0.0;
        chipNoiseLfsr_ = 0x7fffu;
    }

    for (int sample = 0; sample < numSamples; ++sample)
    {
        wetMix_ = moveTowards(wetMix_, targetWetMix, mixStep);
        outputGainSmoothed_ = moveTowards(outputGainSmoothed_, outputGain, mixStep);
        chiptuneMix_ = moveTowards(chiptuneMix_, targetChiptuneMix, mixStep);
        vocalCutMix_ = moveTowards(vocalCutMix_, targetVocalCutMix, mixStep);
        if (vocalCutStrengthRampRemaining_ > 0)
        {
            const double remaining = static_cast<double>(vocalCutStrengthRampRemaining_);
            vocalCutStrengthSmoothed_ += (vocalCutStrengthTarget_ - vocalCutStrengthSmoothed_) / remaining;
            --vocalCutStrengthRampRemaining_;
        }
        double vocalCutLeft = 0.0;
        double vocalCutRight = 0.0;
        if (channels >= 2 && (targetVocalCutMix > 0.0 || vocalCutMix_ > 0.0))
        {
            const double dryLeft = buffer.getReadPointer(0, startSample)[sample];
            const double dryRight = buffer.getReadPointer(1, startSample)[sample];
            const double mid = 0.5 * (dryLeft + dryRight);
            const double side = 0.5 * (dryLeft - dryRight);
            const double preservedCenter = vocalCutCenterFilter_.process(mid);
            const double processedMid = mid
                + (preservedCenter - mid) * vocalCutStrengthSmoothed_;
            vocalCutLeft = processedMid + side;
            vocalCutRight = processedMid - side;
        }
        if (targetVocalCutMix >= 1.0 && vocalCutMix_ >= 1.0)
        {
            // Pure vocal cut skips bitcrusher/chiptune capture and transcendental
            // waveshaping. Crossfades still run the shared path below.
            for (int channel = 0; channel < channels; ++channel)
            {
                auto* samples = buffer.getWritePointer(channel, startSample);
                const double dry = samples[sample];
                const double wet = (channels < 2 ? dry
                    : channel == 0 ? vocalCutLeft : channel == 1 ? vocalCutRight : dry) * outputGainSmoothed_;
                samples[sample] = dry + (wet - dry) * wetMix_;
            }
            holdPhase_ = 1.0;
            continue;
        }
        toneRamp_.advance();
        const bool capture = holdPhase_ >= 1.0;
        if (capture)
        {
            holdPhase_ -= std::floor(holdPhase_);
            double mono = 0.0;
            for (int channel = 0; channel < channels; ++channel)
            {
                const auto* samples = buffer.getReadPointer(channel, startSample);
                const double dry = samples[sample];
                mono += dry;
                heldSamples_[static_cast<size_t>(channel)] = toneRamp_.quantize(dry);
            }
            mono /= static_cast<double>(std::max<double>(1, channels));
            // Quantize relative to the source envelope: quiet passages must not
            // be expanded into loud square waves or a fixed-level noise floor.
            chipInputEnvelope_ = std::max<double>(std::abs(mono), chipInputEnvelope_ * toneRamp_[WorkshopToneRamp::EnvelopeRelease]);
            const double normalizedMono = chipInputEnvelope_ > 0.000001 ? mono / chipInputEnvelope_ : 0.0;
            const double quantizedMono = toneRamp_.quantize(normalizedMono);
            chipBassSample_ += toneRamp_[WorkshopToneRamp::BassCoefficient] * (quantizedMono - chipBassSample_);
            const double transient = std::abs(quantizedMono - chipBassSample_);
            chipNoiseEnvelope_ = std::max<double>(transient, chipNoiseEnvelope_ * toneRamp_[WorkshopToneRamp::NoiseRelease]);

            const uint32_t feedback = (chipNoiseLfsr_ ^ (chipNoiseLfsr_ >> 1u)) & 1u;
            chipNoiseLfsr_ = (chipNoiseLfsr_ >> 1u) | (feedback << 14u);
            const double noise = (chipNoiseLfsr_ & 1u) != 0u ? 1.0 : -1.0;
            const double noiseGate = std::clamp<double>((chipNoiseEnvelope_ - 0.025) * 5.0, 0.0, 1.0);
            const double driven = quantizedMono * toneRamp_[WorkshopToneRamp::Drive];
            const double pulse = driven / (1.0 + std::abs(driven));
            const double triangle = triangleFold(std::clamp<double>(chipBassSample_ * toneRamp_[WorkshopToneRamp::Drive], -1.0, 1.0));
            const double layerWeight = std::max<double>(0.00001, toneRamp_[WorkshopToneRamp::Pulse]
                + toneRamp_[WorkshopToneRamp::Triangle] + toneRamp_[WorkshopToneRamp::Noise]);
            const double chip = (pulse * toneRamp_[WorkshopToneRamp::Pulse]
                + triangle * toneRamp_[WorkshopToneRamp::Triangle]
                + noise * noiseGate * toneRamp_[WorkshopToneRamp::Noise] * 0.15) / layerWeight;
            chipHeldSample_ = toneRamp_.quantize(chip) * chipInputEnvelope_;
        }

        for (int channel = 0; channel < channels; ++channel)
        {
            auto* samples = buffer.getWritePointer(channel, startSample);
            const double dry = samples[sample];
            const double bitcrusher = heldSamples_[static_cast<size_t>(channel)];
            const double stylized = bitcrusher + (chipHeldSample_ - bitcrusher) * chiptuneMix_;
            const double vocalCut = channels >= 2
                ? (channel == 0 ? vocalCutLeft : (channel == 1 ? vocalCutRight : dry))
                : dry;
            const double selectedEffect = stylized + (vocalCut - stylized) * vocalCutMix_;
            const double wet = selectedEffect * outputGainSmoothed_;
            samples[sample] = dry + (wet - dry) * wetMix_;
        }
        holdPhase_ += toneRamp_[WorkshopToneRamp::CaptureIncrement];
    }
    // DspChain may skip us as soon as isActive() becomes false. Cleanup must
    // happen on the final fade block, not depend on a later inactive callback.
    if (targetWetMix <= 0.0 && wetMix_ <= 0.0)
        reset();
}

bool WorkshopAudioEffectProcessor::isActive() const noexcept
{
    return controller_.isEnabled() || wetMix_ > 0.0f;
}

bool WorkshopAudioEffectProcessor::hasClippingRisk() const noexcept
{
    WorkshopAudioEffectState state;
    if (!controller_.tryGetState(state)) return true;
    return state.enabled && state.mix > 0.00001f
        && (state.outputGainDb > 0.0001f
            || (state.effect == WorkshopAudioEffectType::VocalCut && state.vocalCutStrength > 0.0001f));
}
template void WorkshopAudioEffectProcessor::processBlock<float>(PlanarAudioBuffer<float>&, int, int);
template void WorkshopAudioEffectProcessor::processBlock<double>(PlanarAudioBuffer<double>&, int, int);
} // namespace echo
