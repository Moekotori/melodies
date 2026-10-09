#include "PcmRingAudioSource.h"
#include "WorkshopTransitionWindow.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>
#include <thread>

namespace
{
constexpr double halfPi = 1.57079632679489661923;

float smoothStep(double value)
{
    const float progress = static_cast<float>(std::clamp(value, 0.0, 1.0));
    return progress * progress * (3.0f - 2.0f * progress);
}

int64_t steadyNowNs()
{
    return std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}
}

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <avrt.h>

namespace pcm_detail {

struct PcmCallbackMmcssRegistration
{
    HANDLE handle = nullptr;

    PcmCallbackMmcssRegistration()
    {
        DWORD taskIndex = 0;
        handle = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);
        if (handle != nullptr)
            AvSetMmThreadPriority(handle, AVRT_PRIORITY_CRITICAL);
    }

    ~PcmCallbackMmcssRegistration()
    {
        if (handle != nullptr)
            AvRevertMmThreadCharacteristics(handle);
    }
};

void configureAudioCallbackThread()
{
    thread_local std::unique_ptr<PcmCallbackMmcssRegistration> registration;
    if (registration == nullptr)
        registration = std::make_unique<PcmCallbackMmcssRegistration>();
}

} // namespace pcm_detail
#else
namespace pcm_detail {
void configureAudioCallbackThread() {}
}
#endif

PcmRingAudioSource::PcmRingAudioSource(
    int channelCount,
    int capacityFrames,
    int startupPrebufferFramesToUse,
    int startupPrebufferTimeoutMsToUse,
    double gainToUse,
    echo::EqProcessor& eqProcessorToUse,
    echo::ChannelBalanceProcessor& channelBalanceProcessorToUse)
    : channels(channelCount),
      gain(static_cast<float>(std::max(0.0, std::min(1.0, gainToUse)))),
      startupPrebufferFrames(std::max(0, startupPrebufferFramesToUse)),
      startupPrebufferTimeoutMs(std::max(0, startupPrebufferTimeoutMsToUse)),
      fifo(capacityFrames),
      buffer(static_cast<size_t>(capacityFrames * channelCount), 0.0f),
      automixFifo(capacityFrames),
      automixBuffer(static_cast<size_t>(capacityFrames * channelCount), 0.0f),
      automixCurrentLowState(static_cast<size_t>(channelCount), 0.0f),
      automixNextLowState(static_cast<size_t>(channelCount), 0.0f),
      automixCurrentPresenceState(static_cast<size_t>(channelCount), 0.0f),
      automixNextPresenceState(static_cast<size_t>(channelCount), 0.0f),
      ownedConvolutionProcessor(std::make_unique<echo::ConvolutionProcessor>()),
      convolutionProcessor(ownedConvolutionProcessor.get()),
      ownedHeadroomProcessor(std::make_unique<echo::DspHeadroomProcessor>()),
      headroomProcessor(ownedHeadroomProcessor.get()),
      ownedReplayGainProcessor(std::make_unique<echo::ReplayGainProcessor>()),
      replayGainProcessor(ownedReplayGainProcessor.get()),
      ownedCompressorProcessor(std::make_unique<echo::CompressorProcessor>()),
      compressorProcessor(ownedCompressorProcessor.get()),
      ownedSpatialDspProcessor(std::make_unique<echo::SpatialDspProcessor>()),
      spatialDspProcessor(ownedSpatialDspProcessor.get()),
      ownedRateProcessor(std::make_unique<echo::PlaybackRateProcessor>()),
      rateProcessor(ownedRateProcessor.get()),
      ownedMeterProcessor(std::make_unique<echo::LevelMeterProcessor>()),
      meterProcessor(ownedMeterProcessor.get()),
      dspChain(eqProcessorToUse, *convolutionProcessor, channelBalanceProcessorToUse, *headroomProcessor,
               *ownedReplayGainProcessor, *ownedCompressorProcessor, *ownedSpatialDspProcessor,
               *ownedRateProcessor, *ownedMeterProcessor)
{
}

echo_audio_host::NativeFifo& PcmRingAudioSource::fifoForDeck(int deckSlot)
{
    return deckSlot == 0 ? fifo : automixFifo;
}

const echo_audio_host::NativeFifo& PcmRingAudioSource::fifoForDeck(int deckSlot) const
{
    return deckSlot == 0 ? fifo : automixFifo;
}

std::vector<float>& PcmRingAudioSource::bufferForDeck(int deckSlot)
{
    return deckSlot == 0 ? buffer : automixBuffer;
}

const std::vector<float>& PcmRingAudioSource::bufferForDeck(int deckSlot) const
{
    return deckSlot == 0 ? buffer : automixBuffer;
}

std::mutex& PcmRingAudioSource::mutexForDeck(int deckSlot) const
{
    return deckSlot == 0 ? fifoMutex : automixMutex;
}

bool PcmRingAudioSource::isDeckInputEnded(int deckSlot) const
{
    if (deckSlot == 0
        && deckLeases[0].load(std::memory_order_acquire) == 0)
        return session_.isInputEnded();
    return deckInputEnded[static_cast<size_t>(deckSlot)].load(std::memory_order_acquire);
}

bool PcmRingAudioSource::deckHasAudio(int deckSlot) const
{
    if (deckSlot == 0
        && deckLeases[0].load(std::memory_order_acquire) == 0)
        return session_.hasAudio();
    return deckAudioAvailable[static_cast<size_t>(deckSlot)].load(std::memory_order_acquire);
}

PcmRingAudioSource::PcmRingAudioSource(
    int channelCount,
    int capacityFrames,
    int startupPrebufferFramesToUse,
    int startupPrebufferTimeoutMsToUse,
    double gainToUse,
    echo::EqProcessor& eqProcessorToUse,
    echo::ConvolutionProcessor& convolutionProcessorToUse,
    echo::ChannelBalanceProcessor& channelBalanceProcessorToUse,
    echo::DspHeadroomProcessor& headroomProcessorToUse,
    echo::ReplayGainProcessor& replayGainProcessorToUse,
    echo::CompressorProcessor& compressorProcessorToUse,
    echo::SpatialDspProcessor& spatialDspProcessorToUse,
    echo::PlaybackRateProcessor& rateProcessorToUse,
    echo::LevelMeterProcessor& meterProcessorToUse,
    echo::DspRackOrder* rackOrderToUse,
    echo::WorkshopAudioEffectController* workshopAudioEffectControllerToUse)
    : channels(channelCount),
      gain(static_cast<float>(std::max(0.0, std::min(1.0, gainToUse)))),
      startupPrebufferFrames(std::max(0, startupPrebufferFramesToUse)),
      startupPrebufferTimeoutMs(std::max(0, startupPrebufferTimeoutMsToUse)),
      fifo(capacityFrames),
      buffer(static_cast<size_t>(capacityFrames * channelCount), 0.0f),
      automixFifo(capacityFrames),
      automixBuffer(static_cast<size_t>(capacityFrames * channelCount), 0.0f),
      automixCurrentLowState(static_cast<size_t>(channelCount), 0.0f),
      automixNextLowState(static_cast<size_t>(channelCount), 0.0f),
      automixCurrentPresenceState(static_cast<size_t>(channelCount), 0.0f),
      automixNextPresenceState(static_cast<size_t>(channelCount), 0.0f),
      convolutionProcessor(&convolutionProcessorToUse),
      headroomProcessor(&headroomProcessorToUse),
      replayGainProcessor(&replayGainProcessorToUse),
      compressorProcessor(&compressorProcessorToUse),
      spatialDspProcessor(&spatialDspProcessorToUse),
      rateProcessor(&rateProcessorToUse),
      meterProcessor(&meterProcessorToUse),
      dspChain(eqProcessorToUse, *convolutionProcessor, channelBalanceProcessorToUse, *headroomProcessor,
               *replayGainProcessor, *compressorProcessor, *spatialDspProcessor,
               *rateProcessor, *meterProcessor, rackOrderToUse, workshopAudioEffectControllerToUse)
{
}

void PcmRingAudioSource::prepareRenderScratchBuffers(int maxFramesPerCallback)
{
    const int safeFrames = std::max(1, maxFramesPerCallback);
    const double playbackRateFrames = std::ceil(
        static_cast<double>(safeFrames) * static_cast<double>(echo::playbackRateMax));
    const int safePlaybackRateFrames = static_cast<int>(std::min(
        playbackRateFrames,
        static_cast<double>(std::numeric_limits<int>::max())));
    playbackRateInputBuffer.setSize(channels, safePlaybackRateFrames);
    dspRenderBuffer.setSize(channels, safeFrames);
}

void PcmRingAudioSource::prepareToPlay(int samplesPerBlockExpected, double sampleRate)
{
    echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
    const int safeFrames = std::max(1, samplesPerBlockExpected);
    prepareRenderScratchBuffers(safeFrames);
    configureDeclickRamp(sampleRate);
    dspChain.prepare(sampleRate, safeFrames, channels);
    renderSampleRate.store(static_cast<int>(std::lround(sampleRate)), std::memory_order_release);
}

void PcmRingAudioSource::prepareForNativeRender(int maxFramesPerCallback, double sampleRate)
{
    echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
    const int safeFrames = std::max(1, maxFramesPerCallback);
    nativeRenderBuffer.setSize(channels, safeFrames);
    prepareRenderScratchBuffers(safeFrames);
    configureDeclickRamp(sampleRate);
    dspChain.prepare(sampleRate, safeFrames, channels);
    renderSampleRate.store(static_cast<int>(std::lround(sampleRate)), std::memory_order_release);
}

void PcmRingAudioSource::releaseResources()
{
    echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
    dspChain.resetForHardReplace();
}

bool PcmRingAudioSource::isDspActive() const
{
    return dspChain.isActive() || ditherProcessor.active();
}

bool PcmRingAudioSource::hasDspClippingRisk() const
{
    return dspChain.hasClippingRisk();
}

bool PcmRingAudioSource::isDspLimiterProtecting() const
{
    return dspChain.isSafetyLimiterProtecting();
}

bool PcmRingAudioSource::isDspLimiterEnabled() const
{
    return echo::DspChain::isSafetyLimiterEnabled();
}

float PcmRingAudioSource::getDspLimiterGainReductionDb() const
{
    return dspChain.safetyLimiterGainReductionDb();
}

float PcmRingAudioSource::getDspLimiterCeilingDb() const
{
    return dspChain.safetyLimiterCeilingDb();
}

void PcmRingAudioSource::setUpstreamPcmProcessingActive(bool active)
{
    dspChain.setUpstreamPcmProcessingActive(active);
}

void PcmRingAudioSource::setRealtimeProgramPcmSink(
    echo::listen_together::RealtimeProgramPcmSink* sink) noexcept
{
    realtimeProgramPcmSink.store(sink, std::memory_order_release);
}

uint32_t PcmRingAudioSource::renderInterleaved(float* output, uint32_t frameCount, uint32_t outputChannels)
{
    return renderInterleaved({ output, frameCount, outputChannels });
}

uint32_t PcmRingAudioSource::renderInterleaved(echo_audio_host::FloatInterleavedRenderTarget target)
{
    auto* output = target.samples;
    const auto frameCount = target.frames;
    const auto outputChannels = target.channels;

    if (output == nullptr || frameCount == 0 || outputChannels == 0)
        return 0;

    auto renderLease = renderEpoch.tryAcquire();
    if (! renderLease)
        return 0;

    std::memset(output, 0, static_cast<size_t>(frameCount) * outputChannels * sizeof(float));

    const int scratchFrames = nativeRenderBuffer.getNumSamples();
    if (scratchFrames <= 0)
        return 0;

    uint32_t renderedFrames = 0;
    uint32_t totalFramesRead = 0;

    while (renderedFrames < frameCount)
    {
        const int framesThisChunk = static_cast<int>(std::min<uint32_t>(
            static_cast<uint32_t>(scratchFrames),
            frameCount - renderedFrames));
        const auto framesRead = renderPlanarUnderLease(nativeRenderBuffer, 0, framesThisChunk);
        const bool transparentPassthrough = lastRenderUsedMqaPassthrough_.load(std::memory_order_acquire);
        copyPlanarToInterleaved(
            nativeRenderBuffer,
            output + static_cast<size_t>(renderedFrames) * outputChannels,
            framesThisChunk,
            static_cast<int>(outputChannels));
        const auto boundedFramesRead = std::min<uint64_t>(
            framesRead,
            static_cast<uint64_t>(framesThisChunk));
        if (boundedFramesRead > 0 && ! transparentPassthrough)
        {
            ditherProcessor.process(
                output + static_cast<size_t>(renderedFrames) * outputChannels,
                static_cast<size_t>(boundedFramesRead) * outputChannels);
        }
        totalFramesRead += static_cast<uint32_t>(boundedFramesRead);
        renderedFrames += static_cast<uint32_t>(framesThisChunk);
    }

    return totalFramesRead;
}

uint64_t PcmRingAudioSource::renderPlanar(echo::FloatAudioBuffer& output, int startSample, int frameCount)
{
    if (frameCount <= 0)
        return 0;

    auto renderLease = renderEpoch.tryAcquire();
    if (! renderLease)
        return 0;

    return renderPlanarUnderLease(output, startSample, frameCount);
}

uint64_t PcmRingAudioSource::renderPlanarUnderLease(
    echo::FloatAudioBuffer& output,
    int startSample,
    int frameCount)
{
    output.clear(startSample, frameCount);

    // Capture session identity before the pause gate. If a control operation
    // closes admission and advances the generation after this callback began,
    // the callback must not enter the newly published FIFO epoch.
    const uint64_t renderGeneration = session_.generation();
    auto* dualTrackMix = dualTrackMix_.load(std::memory_order_acquire);
    if (sessionPaused.load(std::memory_order_acquire))
        return 0;

    if (shouldHoldForStartupPrebuffer())
        return 0;

    const bool transparentPassthrough = refreshMqaPassthroughState();
    lastRenderUsedMqaPassthrough_.store(transparentPassthrough, std::memory_order_release);
    const uint64_t absoluteStartFrame = deviceOutputFrames.load(std::memory_order_relaxed);
    finalizeAutomixCancelFade(absoluteStartFrame);
    const int currentSlot = currentDeckSlot.load(std::memory_order_acquire);
    const uint64_t renderDeckLease = currentDeckLease.load(std::memory_order_acquire);
#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    if (readAdmissionHookForTests)
        readAdmissionHookForTests();
#endif
    if (session_.generation() != renderGeneration
        || currentDeckSlot.load(std::memory_order_acquire) != currentSlot
        || currentDeckLease.load(std::memory_order_acquire) != renderDeckLease)
        return 0;
    auto& currentFifo = fifoForDeck(currentSlot);
    echo_audio_host::NativeFifo::ReadLease currentReadLease;
    if (! currentFifo.tryAcquireReadLease(currentReadLease))
        return 0;
    if (session_.generation() != renderGeneration
        || currentDeckSlot.load(std::memory_order_acquire) != currentSlot
        || currentDeckLease.load(std::memory_order_acquire) != renderDeckLease)
    {
        currentFifo.releaseReadLease(currentReadLease);
        return 0;
    }
    const bool gaplessActive = automixPlan.enabled.load(std::memory_order_acquire)
        && automixPlan.gapless.load(std::memory_order_acquire);
    if (! gaplessActive)
        activateAutomixFadeIfReady(absoluteStartFrame, frameCount);
    const auto rateConfiguration = rateProcessor != nullptr
        ? rateProcessor->getConfiguration()
        : echo::PlaybackRateProcessor::Configuration {};
    const float playbackRate = rateConfiguration.rate;
    const bool playbackRateActive = rateConfiguration.mode != echo::SpeedMode::Speed
        && std::abs(playbackRate - 1.0f) > 0.0001f;
    const int sourceFrameTarget = playbackRateActive && rateProcessor != nullptr
        ? rateProcessor->sourceFramesRequired(frameCount, rateConfiguration)
        : frameCount;
    if (playbackRateActive)
    {
        if (sourceFrameTarget > playbackRateInputBuffer.getNumSamples())
        {
            currentFifo.releaseReadLease(currentReadLease);
            return 0;
        }
        playbackRateInputBuffer.clear(0, sourceFrameTarget);
    }

    echo::FloatAudioBuffer& renderBuffer = playbackRateActive ? playbackRateInputBuffer : output;
    const int renderStartSample = playbackRateActive ? 0 : startSample;
    int framesNeeded = sourceFrameTarget;
    int outputOffset = 0;
    uint64_t framesReadTotal = 0;

    while (framesNeeded > 0)
    {
        int start1 = 0;
        int size1 = 0;
        int start2 = 0;
        int size2 = 0;
        currentFifo.prepareToRead(framesNeeded, start1, size1, start2, size2);

        const int framesRead = size1 + size2;
        if (framesRead <= 0)
        {
            if (! isDeckInputEnded(currentSlot) && deckHasAudio(currentSlot))
            {
                underrunCallbacks.fetch_add(1, std::memory_order_relaxed);
                underrunFrames.fetch_add(static_cast<uint64_t>(framesNeeded), std::memory_order_relaxed);
            }
            break;
        }

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
        if (readLeaseHookForTests)
            readLeaseHookForTests();
#endif

        copyToOutput(
            start1, size1, renderBuffer, renderStartSample + outputOffset,
            absoluteStartFrame, outputOffset, playbackRate, transparentPassthrough, currentSlot);
        copyToOutput(
            start2,
            size2,
            renderBuffer,
            renderStartSample + outputOffset + size1,
            absoluteStartFrame,
            outputOffset + size1,
            playbackRate,
            transparentPassthrough,
            currentSlot);
        currentFifo.finishedRead(framesRead);

        framesReadTotal += static_cast<uint64_t>(framesRead);
        framesNeeded -= framesRead;
        outputOffset += framesRead;
    }
    currentFifo.releaseReadLease(currentReadLease);
    if (framesReadTotal > 0) {
        if (dualTrackMix) dualTrackMix->mix(renderBuffer, renderStartSample, static_cast<int>(framesReadTotal));
    }
    if (gaplessActive && !transparentPassthrough && replayGainProcessor != nullptr && framesReadTotal > 0)
        replayGainProcessor->processBlock(renderBuffer, renderStartSample, static_cast<int>(framesReadTotal));
#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
    if (postFifoReadHookForTests)
        postFifoReadHookForTests();
#endif

    const bool mainInputEnded = isDeckInputEnded(currentSlot);
    const int automixFrameBudget = mainInputEnded
        ? sourceFrameTarget
        : static_cast<int>(std::min<uint64_t>(static_cast<uint64_t>(sourceFrameTarget), framesReadTotal));
    const uint64_t automixFramesRead = transparentPassthrough
        ? 0
        : gaplessActive && mainInputEnded
        ? mixGaplessNext(
            renderBuffer,
            renderStartSample,
            sourceFrameTarget,
            absoluteStartFrame,
            framesReadTotal,
            playbackRate)
        : automixFrameBudget > 0
            ? mixAutomixNext(
                renderBuffer,
                renderStartSample,
                automixFrameBudget,
                absoluteStartFrame,
                playbackRate)
            : 0;
    const uint64_t renderedFrames = ! transparentPassthrough && automixPlan.enabled.load(std::memory_order_acquire)
        ? (gaplessActive && mainInputEnded
            ? framesReadTotal + automixFramesRead
            : mainInputEnded ? std::max(framesReadTotal, automixFramesRead) : framesReadTotal)
        : framesReadTotal;

    // Replace only the prepared window before the ordinary DSP chain.
    // Both native decks still consume their source frames normally.
    const auto* providerWindow = workshopWindow.load(std::memory_order_acquire);
    if (providerWindow != nullptr && automixPlan.fadeActivated.load(std::memory_order_acquire)
        && !automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
    {
        const uint64_t start = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
        for (uint64_t frame = 0; frame < renderedFrames; ++frame)
        {
            const uint64_t absolute = absoluteStartFrame + frame;
            if (absolute < start || absolute - start >= providerWindow->frames) continue;
            for (int channel = 0; channel < renderBuffer.getNumChannels(); ++channel)
                renderBuffer.getWritePointer(channel, renderStartSample)[frame] =
                    providerWindow->samples[(absolute - start) * providerWindow->channels + channel];
        }
    }
    if (renderedFrames > 0)
        sourceTrackFrames.fetch_add(renderedFrames, std::memory_order_relaxed);

    uint64_t outputFramesRendered = std::min<uint64_t>(
        renderedFrames,
        static_cast<uint64_t>(frameCount));
    if (playbackRateActive)
    {
        const int sourceFramesAvailable = static_cast<int>(std::min<uint64_t>(
            renderedFrames,
            static_cast<uint64_t>(std::numeric_limits<int>::max())));
        outputFramesRendered = static_cast<uint64_t>(rateProcessor->processResampledBlock(
            renderBuffer,
            sourceFramesAvailable,
            output,
            startSample,
            frameCount,
            rateConfiguration));
    }
    else if (rateProcessor != nullptr && renderedFrames > 0)
    {
        // Keep the resampler's causal history warm while running at unity so
        // the first non-unity callback begins from real preceding PCM instead
        // of a zero-padded block edge.
        rateProcessor->primeSourceHistory(
            renderBuffer,
            renderStartSample,
            static_cast<int>(std::min<uint64_t>(
                renderedFrames,
                static_cast<uint64_t>(frameCount))));
    }

    if (! transparentPassthrough)
    {
        if (auto* sink = realtimeProgramPcmSink.load(std::memory_order_acquire))
        {
            sink->onProgramClock(
                sourceTrackFrames.load(std::memory_order_relaxed) - renderedFrames,
                absoluteStartFrame, renderSampleRate.load(std::memory_order_acquire),
                outputFramesRendered == static_cast<uint64_t>(frameCount), playbackRate);
            sink->onProgramPcm(
                output,
                startSample,
                frameCount,
                renderSampleRate.load(std::memory_order_acquire));
        }
        // The shared programme contains the A/B mix and host speed, but never
        // the host's listening volume. Apply local gain exactly once, before
        // the existing DSP graph, without another PCM buffer or allocation.
        const float localOutputGain = gain.load(std::memory_order_acquire);
        if (localOutputGain != 1.0f)
            for (int channel = 0; channel < output.getNumChannels(); ++channel)
                output.applyGain(channel, startSample, frameCount, localOutputGain);
    }

    const bool hadDspTail = !dspChain.isTailDrained();
    if (! transparentPassthrough && frameCount <= dspRenderBuffer.getNumSamples())
    {
        for (int c = 0; c < output.getNumChannels(); ++c)
        {
            const float* src = output.getReadPointer(c, startSample);
            float* dst = dspRenderBuffer.getWritePointer(c);
            std::copy_n(src, static_cast<std::size_t>(frameCount), dst);
        }
        // The user DSP graph is deliberately downstream of the A/B sum so
        // EQ, convolution, balance, headroom and metering each run once.
        // Smart Transition applies ReplayGain per Deck, so the global
        // ReplayGain stage must not apply it a second time to the sum.
        const bool smartTransitionActive =
            automixPlan.enabled.load(std::memory_order_acquire)
            && ! automixPlan.gapless.load(std::memory_order_acquire);
        const bool promotedDeckGainActive =
            currentDeckLease.load(std::memory_order_acquire) != 0;
        dspChain.processBlock(
            dspRenderBuffer,
            0,
            frameCount,
            ! gaplessActive && ! smartTransitionActive && ! promotedDeckGainActive,
            smartTransitionActive);
        for (int c = 0; c < output.getNumChannels(); ++c)
        {
            float* dst = output.getWritePointer(c, startSample);
            const float* src = dspRenderBuffer.getReadPointer(c);
            std::copy_n(src, static_cast<std::size_t>(frameCount), dst);
        }
    }
    if (hadDspTail || !dspChain.isTailDrained())
        outputFramesRendered = static_cast<uint64_t>(frameCount);
    if (! transparentPassthrough)
        applyDeclickRamp(output, startSample, frameCount);

    if (outputFramesRendered > 0)
        deviceOutputFrames.fetch_add(outputFramesRendered, std::memory_order_relaxed);

    return outputFramesRendered;
}

bool PcmRingAudioSource::push(const float* samples, int frameCount)
{
    return pushForGeneration(samples, frameCount, session_.generation());
}

bool PcmRingAudioSource::pushForGeneration(const float* samples, int frameCount, uint64_t generation)
{
    if (samples == nullptr || frameCount <= 0)
        return frameCount == 0
            && generation == session_.generation()
            && initialProducerActive.load(std::memory_order_acquire);
    if (generation != session_.generation()
        || session_.isStopRequested()
        || ! initialProducerActive.load(std::memory_order_acquire))
        return false;

    uint64_t producerEpoch = 0;
    {
        std::lock_guard<std::mutex> lock(fifoMutex);
        producerEpoch = fifo.currentEpoch();
        if ((producerEpoch & 1u) != 0u
            || generation != session_.generation()
            || session_.isStopRequested()
            || ! initialProducerActive.load(std::memory_order_acquire))
            return false;
    }

    int written = 0;

    while (written < frameCount
        && ! session_.isStopRequested()
        && session_.generation() == generation
        && initialProducerActive.load(std::memory_order_acquire))
    {
        int start1 = 0;
        int size1 = 0;
        int start2 = 0;
        int size2 = 0;
        {
            std::lock_guard<std::mutex> lock(fifoMutex);
            // beginSession()/replaceBufferedAudio() advance the generation
            // before resetting this FIFO. Recheck after waiting for the FIFO
            // lock so a stale decoder block cannot enter the new session.
            if (generation != session_.generation()
                || session_.isStopRequested()
                || ! initialProducerActive.load(std::memory_order_acquire)
                || fifo.currentEpoch() != producerEpoch)
                break;
            fifo.prepareToWrite(frameCount - written, start1, size1, start2, size2);

            const int framesWritable = size1 + size2;
            if (framesWritable > 0)
            {
                session_.markHasAudio();
                copyFromInput(samples + written * channels, start1, size1);
                copyFromInput(samples + (written + size1) * channels, start2, size2);
                fifo.finishedWrite(framesWritable);
                written += framesWritable;
                continue;
            }
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(4));
    }

    return written == frameCount
        && session_.generation() == generation
        && initialProducerActive.load(std::memory_order_acquire)
        && fifo.currentEpoch() == producerEpoch
        && !session_.isStopRequested();
}

int PcmRingAudioSource::replaceBufferedAudio(const float* samples, int frameCount, bool pausedAfterReplace)
{
    // Close the callback admission gate before changing generation or deck
    // identity. A callback already admitted under the old state is drained by
    // the per-FIFO reader epoch below.
    sessionPaused.store(true, std::memory_order_release);
    echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
    dspChain.applySessionTransition(echo::DspSessionTransition::HardReplace);
    {
        std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
        const auto gateGeneration = initialPlayGateGeneration.load(std::memory_order_acquire);
        if (isInitialPlayGateArmed(gateGeneration))
        {
            initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::SessionReplaced, std::memory_order_release);
            initialPlayGateState.store(InitialPlayGateState::Cancelled, std::memory_order_release);
        }
    }
    initialPlayGateGeneration.store(0, std::memory_order_release);
    initialPlayGateOperationId.store(0, std::memory_order_release);
    session_.begin();
    cancelAutomix();
    initialProducerActive.store(true, std::memory_order_release);
    deckLeases[0].store(0, std::memory_order_release);
    deckLeases[1].store(0, std::memory_order_release);
    deckInputEnded[0].store(false, std::memory_order_release);
    deckInputEnded[1].store(false, std::memory_order_release);
    deckAudioAvailable[0].store(false, std::memory_order_release);
    deckAudioAvailable[1].store(false, std::memory_order_release);
    promotedDeckReplayGain.store(1.0f, std::memory_order_release);
    promotedDeckReleaseStartGain.store(1.0f, std::memory_order_release);
    promotedDeckReleaseStartFrame.store(0, std::memory_order_release);
    promotedDeckReleaseEndFrame.store(0, std::memory_order_release);
    int framesWritten = 0;
    {
        std::lock_guard<std::mutex> lock(fifoMutex);
        fifo.resetWhenReadersQuiescent();
        prebufferDeadlineNs.store(
            steadyNowNs() + static_cast<int64_t>(startupPrebufferTimeoutMs) * 1'000'000,
            std::memory_order_release);
        if (samples != nullptr && frameCount > 0) {
            int start1 = 0;
            int size1 = 0;
            int start2 = 0;
            int size2 = 0;
            fifo.prepareToWrite(frameCount, start1, size1, start2, size2);
            framesWritten = size1 + size2;
            copyFromInput(samples, start1, size1);
            copyFromInput(samples + size1 * channels, start2, size2);
            fifo.finishedWrite(framesWritten);
        }
    }
    {
        // Deck B may have been promoted by Automix and still be snapshotted by
        // an in-flight render. Retire it before this replacement is visible as
        // complete; the callback only releases an epoch and never waits.
        std::lock_guard<std::mutex> lock(automixMutex);
        automixFifo.resetWhenReadersQuiescent();
    }
    currentDeckSlot.store(0, std::memory_order_release);
    currentDeckLease.store(0, std::memory_order_release);

    sourceTrackFrames.store(0, std::memory_order_relaxed);
    deviceOutputFrames.store(0, std::memory_order_relaxed);
    underrunCallbacks.store(0, std::memory_order_relaxed);
    underrunFrames.store(0, std::memory_order_relaxed);
    sessionPaused.store(pausedAfterReplace, std::memory_order_release);
    stopFadeRequested.store(false, std::memory_order_release);
    declickFadeGeneration.fetch_add(1, std::memory_order_acq_rel);
    prebuffering.store(framesWritten <= 0 && startupPrebufferFrames > 0, std::memory_order_release);
    if (framesWritten > 0)
        session_.markHasAudio();
    return framesWritten;
}

bool PcmRingAudioSource::pushAutomixNext(const float* samples, int frameCount)
{
    const uint64_t deckLease = pendingAutomixDeckLease.load(std::memory_order_acquire);
    return deckLease != 0 && pushAutomixDeck(deckLease, samples, frameCount);
}

bool PcmRingAudioSource::pushAutomixDeck(uint64_t deckLease, const float* samples, int frameCount)
{
    if (deckLease == 0 || samples == nullptr || frameCount <= 0)
        return frameCount == 0 && deckLease != 0;

    const int deckSlot = static_cast<int>(deckLease & 1u);
    const auto leaseIsActive = [this, deckLease, deckSlot]() {
        const bool retiringPendingDeck = cancelFadePending.load(std::memory_order_acquire)
            && pendingAutomixDeckLease.load(std::memory_order_acquire) == deckLease;
        return !retiringPendingDeck
            && deckLeases[static_cast<size_t>(deckSlot)].load(std::memory_order_acquire) == deckLease
            && (pendingAutomixDeckLease.load(std::memory_order_acquire) == deckLease
                || currentDeckLease.load(std::memory_order_acquire) == deckLease);
    };
    if (session_.isStopRequested() || ! leaseIsActive())
        return false;

    deckAudioAvailable[static_cast<size_t>(deckSlot)].store(true, std::memory_order_release);
    if (pendingAutomixDeckLease.load(std::memory_order_acquire) == deckLease)
        automixNextHasAudio.store(true, std::memory_order_release);

    int written = 0;
    auto& deckFifo = fifoForDeck(deckSlot);
    uint64_t producerEpoch = 0;
    {
        std::lock_guard<std::mutex> lock(mutexForDeck(deckSlot));
        if (! leaseIsActive())
            return false;
        producerEpoch = deckFifo.currentEpoch();
        if ((producerEpoch & 1u) != 0u)
            return false;
    }
    while (written < frameCount && ! session_.isStopRequested() && leaseIsActive())
    {
        int start1 = 0;
        int size1 = 0;
        int start2 = 0;
        int size2 = 0;
        {
            std::lock_guard<std::mutex> lock(mutexForDeck(deckSlot));
            if (! leaseIsActive() || deckFifo.currentEpoch() != producerEpoch)
                break;
            deckFifo.prepareToWrite(frameCount - written, start1, size1, start2, size2);

            const int framesWritable = size1 + size2;
            if (framesWritable > 0)
            {
                copyToAutomixBuffer(samples + written * channels, start1, size1, deckSlot);
                copyToAutomixBuffer(samples + (written + size1) * channels, start2, size2, deckSlot);
                deckFifo.finishedWrite(framesWritable);
                written += framesWritable;
                continue;
            }
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(4));
    }

    return written == frameCount
        && leaseIsActive()
        && deckFifo.currentEpoch() == producerEpoch
        && ! session_.isStopRequested();
}

void PcmRingAudioSource::prepareAutomix(double sampleRate, double fadeStartSeconds, double overlapSeconds, double currentGainDb, double nextGainDb)
{
    const double safeSampleRate = sampleRate > 0.0 ? sampleRate : 44100.0;
    const auto fadeStart = static_cast<uint64_t>(std::max(0.0, fadeStartSeconds) * safeSampleRate);
    const auto overlapFrames = static_cast<uint64_t>(std::max(0.001, overlapSeconds) * safeSampleRate);
    const double releaseSeconds = std::clamp(std::max(0.001, overlapSeconds) * 0.5, 0.05, 4.0);
    const auto gainReleaseFrames = static_cast<uint64_t>(releaseSeconds * safeSampleRate);

    if (prepareAutomixFrames(fadeStart, overlapFrames, currentGainDb, nextGainDb))
    {
        automixPlan.gainReleaseEndFrame.store(
            fadeStart + std::max<uint64_t>(1, overlapFrames) + std::max<uint64_t>(1, gainReleaseFrames),
            std::memory_order_release);
    }
}

bool PcmRingAudioSource::prepareAutomixFrames(
    uint64_t fadeStartFrame,
    uint64_t overlapFrames,
    double currentGainDb,
    double nextGainDb,
    double currentReplayGainDb,
    double nextReplayGainDb,
    bool rhythmicBassSwap,
    bool tailRide,
    bool echoThrow,
    double echoDelaySeconds,
    double echoSend,
    double echoFeedback,
    double vocalExitStartProgress,
    double vocalEntryStartProgress,
    AutomixHandoffSchedule handoffSchedule)
{
    return prepareAutomixDeck(
        fadeStartFrame,
        overlapFrames,
        currentGainDb,
        nextGainDb,
        currentReplayGainDb,
        nextReplayGainDb,
        rhythmicBassSwap,
        tailRide,
        echoThrow,
        echoDelaySeconds,
        echoSend,
        echoFeedback,
        vocalExitStartProgress,
        vocalEntryStartProgress,
        handoffSchedule) != 0;
}

uint64_t PcmRingAudioSource::prepareAutomixDeck(
    uint64_t fadeStartFrame,
    uint64_t overlapFrames,
    double currentGainDb,
    double nextGainDb,
    double currentReplayGainDb,
    double nextReplayGainDb,
    bool rhythmicBassSwap,
    bool tailRide,
    bool echoThrow,
    double echoDelaySeconds,
    double echoSend,
    double echoFeedback,
    double vocalExitStartProgress,
    double vocalEntryStartProgress,
    AutomixHandoffSchedule handoffSchedule)
{
    if (session_.isStopRequested())
        return 0;
    // Fade frames live on the host's monotonic render clock. A stale plan can
    // otherwise activate immediately (for example after a previous autonomous
    // queue transition rebased track progress but not the native render clock).
    if (fadeStartFrame < deviceOutputFrames.load(std::memory_order_acquire))
        return 0;
    // A ramped cancel may still be easing Deck B out and Deck A back to unity.
    // The hard reset below would step both envelopes mid-ramp, so let the render
    // thread finalize the ramp first (bounded: a paused session never renders,
    // and then nothing is audible for the reset to click).
    constexpr int maxCancelRampWaitIterations = 125;
    for (int iteration = 0;
         iteration < maxCancelRampWaitIterations
             && cancelFadePending.load(std::memory_order_acquire)
             && ! session_.isStopRequested();
         ++iteration)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    cancelAutomix();
    const int nextDeckSlot = 1 - currentDeckSlot.load(std::memory_order_acquire);
    {
        std::lock_guard<std::mutex> lock(mutexForDeck(nextDeckSlot));
        fifoForDeck(nextDeckSlot).resetWhenReadersQuiescent();
        auto& nextBuffer = bufferForDeck(nextDeckSlot);
        std::fill(nextBuffer.begin(), nextBuffer.end(), 0.0f);
    }

    const uint64_t sequence = automixDeckLeaseSequence.fetch_add(1, std::memory_order_acq_rel) + 1;
    const uint64_t deckLease = (sequence << 1u) | static_cast<uint64_t>(nextDeckSlot);
    deckLeases[static_cast<size_t>(nextDeckSlot)].store(deckLease, std::memory_order_release);
    deckInputEnded[static_cast<size_t>(nextDeckSlot)].store(false, std::memory_order_release);
    deckAudioAvailable[static_cast<size_t>(nextDeckSlot)].store(false, std::memory_order_release);
    pendingAutomixDeckLease.store(deckLease, std::memory_order_release);

    const auto safeOverlapFrames = overlapFrames;
    const auto releaseFrames = std::max<uint64_t>(1, safeOverlapFrames / 2);
    automixPlan.gapless.store(false, std::memory_order_release);
    gaplessBoundaryFrame.store(UINT64_MAX, std::memory_order_release);
    automixPlan.fadeStartFrame.store(fadeStartFrame, std::memory_order_release);
    automixPlan.fadeEndFrame.store(fadeStartFrame + safeOverlapFrames, std::memory_order_release);
    automixPlan.gainReleaseEndFrame.store(fadeStartFrame + safeOverlapFrames + releaseFrames, std::memory_order_release);
    automixPlan.overlapFrames.store(safeOverlapFrames, std::memory_order_release);
    automixPlan.currentGain.store(dbToGain(currentGainDb), std::memory_order_release);
    automixPlan.nextGain.store(dbToGain(nextGainDb), std::memory_order_release);
    automixPlan.currentReplayGain.store(dbToGain(currentReplayGainDb), std::memory_order_release);
    automixPlan.nextReplayGain.store(dbToGain(nextReplayGainDb), std::memory_order_release);
    automixPlan.rhythmicBassSwap.store(rhythmicBassSwap, std::memory_order_release);
    if (!rhythmicBassSwap || !handoffSchedule.valid()) handoffSchedule = {};
    automixPlan.highSwapStart.store(handoffSchedule.highStart, std::memory_order_release);
    automixPlan.highSwapEnd.store(handoffSchedule.highEnd, std::memory_order_release);
    automixPlan.lowSwapStart.store(handoffSchedule.lowStart, std::memory_order_release);
    automixPlan.lowSwapEnd.store(handoffSchedule.lowEnd, std::memory_order_release);
    automixPlan.holdStart.store(handoffSchedule.holdStart, std::memory_order_release);
    automixPlan.holdEnd.store(handoffSchedule.holdEnd, std::memory_order_release);
    automixPlan.tailRide.store(tailRide, std::memory_order_release);
    const float safeVocalExitStart = static_cast<float>(std::clamp(vocalExitStartProgress, 0.30, 0.82));
    const float safeVocalEntryStart = static_cast<float>(std::clamp(
        vocalEntryStartProgress,
        static_cast<double>(safeVocalExitStart + 0.04f),
        0.92));
    automixPlan.vocalExitStartProgress.store(safeVocalExitStart, std::memory_order_release);
    automixPlan.vocalEntryStartProgress.store(safeVocalEntryStart, std::memory_order_release);
    automixPlan.fadeActivated.store(false, std::memory_order_release);
    automixPlan.nextDeckFaulted.store(false, std::memory_order_release);
    automixNextInputEnded.store(false, std::memory_order_release);
    automixNextHasAudio.store(false, std::memory_order_release);
    if (echoThrow)
        armAutomixEchoThrow(
            fadeStartFrame + safeOverlapFrames,
            echoDelaySeconds,
            echoSend,
            echoFeedback);
    automixPlan.enabled.store(true, std::memory_order_release);
    return deckLease;
}

void PcmRingAudioSource::installDualTrackMix(std::shared_ptr<DualTrackMixBuffer> mix)
{
    std::shared_ptr<DualTrackMixBuffer> retired;
    {
        echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
        retired = std::move(dualTrackMixOwner_);
        dualTrackMixOwner_ = std::move(mix);
        dualTrackMix_.store(dualTrackMixOwner_.get(), std::memory_order_release);
    }
    // Release the fixed ring on the control thread, after all old render leases leave.
}

void PcmRingAudioSource::installWorkshopTransitionWindow(std::shared_ptr<const WorkshopTransitionWindow> window)
{
    std::shared_ptr<const WorkshopTransitionWindow> retired;
    {
        echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
        retired = std::move(workshopWindowOwner);
        workshopWindowOwner = std::move(window);
        workshopDeadlineMissed.store(false, std::memory_order_release);
        workshopWindow.store(workshopWindowOwner.get(), std::memory_order_release);
    }
    // Release the potentially large vector on this control thread.
}

bool PcmRingAudioSource::commitAutomixDeck(uint64_t deckLease)
{
    if (deckLease == 0
        || pendingAutomixDeckLease.load(std::memory_order_acquire) != deckLease
        || ! automixPlan.enabled.load(std::memory_order_acquire)
        || automixPlan.gapless.load(std::memory_order_acquire)
        || ! automixPlan.fadeActivated.load(std::memory_order_acquire)
        || deviceOutputFrames.load(std::memory_order_acquire)
            < automixPlan.fadeEndFrame.load(std::memory_order_acquire))
        return false;

    const int nextDeckSlot = static_cast<int>(deckLease & 1u);
    if (deckLeases[static_cast<size_t>(nextDeckSlot)].load(std::memory_order_acquire) != deckLease)
        return false;

    const int oldDeckSlot = currentDeckSlot.load(std::memory_order_acquire);
    const uint64_t oldDeckLease = currentDeckLease.load(std::memory_order_acquire);
    const uint64_t commitFrame = deviceOutputFrames.load(std::memory_order_acquire);
    const float releaseStartGain = nextAutomixEnvelope(commitFrame);
    const uint64_t releaseEndFrame = std::max(
        commitFrame,
        automixPlan.gainReleaseEndFrame.load(std::memory_order_acquire));

    promotedDeckReplayGain.store(
        automixPlan.nextReplayGain.load(std::memory_order_acquire),
        std::memory_order_release);
    promotedDeckReleaseStartGain.store(releaseStartGain, std::memory_order_release);
    promotedDeckReleaseStartFrame.store(commitFrame, std::memory_order_release);
    promotedDeckReleaseEndFrame.store(releaseEndFrame, std::memory_order_release);
    currentDeckSlot.store(nextDeckSlot, std::memory_order_release);
    currentDeckLease.store(deckLease, std::memory_order_release);
    pendingAutomixDeckLease.store(0, std::memory_order_release);
    dspChain.applySessionTransition(echo::DspSessionTransition::AutomixPromotion);
    initialProducerActive.store(false, std::memory_order_release);
    if (oldDeckLease != 0)
        deckLeases[static_cast<size_t>(oldDeckSlot)].store(0, std::memory_order_release);

    automixPlan.enabled.store(false, std::memory_order_release);
    automixPlan.fadeActivated.store(false, std::memory_order_release);
    automixPlan.nextDeckFaulted.store(false, std::memory_order_release);
    automixPlan.rhythmicBassSwap.store(false, std::memory_order_release);
    automixPlan.tailRide.store(false, std::memory_order_release);
    automixNextInputEnded.store(false, std::memory_order_release);
    automixNextHasAudio.store(false, std::memory_order_release);
    return true;
}

void PcmRingAudioSource::prepareGapless()
{
    dspChain.applySessionTransition(echo::DspSessionTransition::GaplessContinuation);
    cancelAutomix();
    gaplessReplayGainTimeline.reset();
    if (replayGainProcessor != nullptr) replayGainProcessor->selectSequenceIndex(0);
    if (currentDeckSlot.load(std::memory_order_acquire) != 0)
        return;
    {
        std::lock_guard<std::mutex> lock(automixMutex);
        automixFifo.resetWhenReadersQuiescent();
        std::fill(automixBuffer.begin(), automixBuffer.end(), 0.0f);
    }

    const uint64_t sequence = automixDeckLeaseSequence.fetch_add(1, std::memory_order_acq_rel) + 1;
    const uint64_t deckLease = (sequence << 1u) | 1u;
    deckLeases[1].store(deckLease, std::memory_order_release);
    deckInputEnded[1].store(false, std::memory_order_release);
    deckAudioAvailable[1].store(false, std::memory_order_release);
    pendingAutomixDeckLease.store(deckLease, std::memory_order_release);

    gaplessBoundaryFrame.store(UINT64_MAX, std::memory_order_release);
    automixPlan.fadeActivated.store(false, std::memory_order_release);
    automixPlan.gapless.store(true, std::memory_order_release);
    automixPlan.rhythmicBassSwap.store(false, std::memory_order_release);
    automixPlan.tailRide.store(false, std::memory_order_release);
    automixPlan.enabled.store(true, std::memory_order_release);
    automixNextInputEnded.store(false, std::memory_order_release);
    automixNextHasAudio.store(false, std::memory_order_release);
}

void PcmRingAudioSource::markAutomixNextEnded()
{
    const uint64_t deckLease = pendingAutomixDeckLease.load(std::memory_order_acquire);
    if (deckLease != 0)
        markAutomixDeckEnded(deckLease);
}

void PcmRingAudioSource::markAutomixDeckEnded(uint64_t deckLease)
{
    if (deckLease == 0)
        return;
    const int deckSlot = static_cast<int>(deckLease & 1u);
    if (deckLeases[static_cast<size_t>(deckSlot)].load(std::memory_order_acquire) != deckLease)
        return;
    deckInputEnded[static_cast<size_t>(deckSlot)].store(true, std::memory_order_release);
    if (pendingAutomixDeckLease.load(std::memory_order_acquire) == deckLease)
        automixNextInputEnded.store(true, std::memory_order_release);
}

void PcmRingAudioSource::failAutomixNext(uint64_t fadeFrames)
{
    failAutomixDeck(
        pendingAutomixDeckLease.load(std::memory_order_acquire),
        fadeFrames);
}

void PcmRingAudioSource::failAutomixDeck(uint64_t deckLease, uint64_t fadeFrames)
{
    if (deckLease == 0
        || pendingAutomixDeckLease.load(std::memory_order_acquire) != deckLease)
        return;
    if (! automixPlan.enabled.load(std::memory_order_acquire)
        || automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
        return;
    stopAutomixEchoThrow();
    const uint64_t startFrame = deviceOutputFrames.load(std::memory_order_acquire);
    automixPlan.faultCurrentStartGain.store(
        currentAutomixEnvelope(startFrame),
        std::memory_order_release);
    automixPlan.faultNextStartGain.store(
        nextAutomixEnvelope(startFrame),
        std::memory_order_release);
    automixPlan.faultFadeStartFrame.store(startFrame, std::memory_order_release);
    automixPlan.faultFadeEndFrame.store(
        startFrame + std::max<uint64_t>(1, fadeFrames),
        std::memory_order_release);
    automixPlan.nextDeckFaulted.store(true, std::memory_order_release);
}

void PcmRingAudioSource::cancelAutomix()
{
    stopAutomixEchoThrow();
    cancelFadePending.store(false, std::memory_order_release);
    const bool gaplessWasActive = automixPlan.gapless.load(std::memory_order_acquire);
    const uint64_t pendingDeckLease = pendingAutomixDeckLease.exchange(0, std::memory_order_acq_rel);
    automixPlan.enabled.store(false, std::memory_order_release);
    automixPlan.gapless.store(false, std::memory_order_release);
    automixPlan.fadeActivated.store(false, std::memory_order_release);
    automixPlan.nextDeckFaulted.store(false, std::memory_order_release);
    automixPlan.currentReplayGain.store(1.0f, std::memory_order_release);
    automixPlan.nextReplayGain.store(1.0f, std::memory_order_release);
    automixPlan.rhythmicBassSwap.store(false, std::memory_order_release);
    automixPlan.tailRide.store(false, std::memory_order_release);
    automixNextInputEnded.store(false, std::memory_order_release);
    automixNextHasAudio.store(false, std::memory_order_release);
    gaplessBoundaryFrame.store(UINT64_MAX, std::memory_order_release);
    int cancelledDeckSlot = 1;
    if (pendingDeckLease != 0)
    {
        cancelledDeckSlot = static_cast<int>(pendingDeckLease & 1u);
        uint64_t expectedLease = pendingDeckLease;
        deckLeases[static_cast<size_t>(cancelledDeckSlot)].compare_exchange_strong(
            expectedLease,
            0,
            std::memory_order_acq_rel,
            std::memory_order_acquire);
        deckInputEnded[static_cast<size_t>(cancelledDeckSlot)].store(false, std::memory_order_release);
        deckAudioAvailable[static_cast<size_t>(cancelledDeckSlot)].store(false, std::memory_order_release);
    }
    else if (! gaplessWasActive)
    {
        return;
    }
    {
        std::lock_guard<std::mutex> lock(mutexForDeck(cancelledDeckSlot));
        fifoForDeck(cancelledDeckSlot).resetWhenReadersQuiescent();
    }
}

void PcmRingAudioSource::cancelAutomixWithFade()
{
    stopAutomixEchoThrow();
    // Only a Smart Transition whose fade is already audible needs a ramp; a
    // plan that never produced Deck B output can tear down instantly without
    // a click, exactly like before.
    if (! automixPlan.enabled.load(std::memory_order_acquire)
        || automixPlan.gapless.load(std::memory_order_acquire)
        || ! automixPlan.fadeActivated.load(std::memory_order_acquire))
    {
        cancelAutomix();
        return;
    }
    const uint64_t pendingLease = pendingAutomixDeckLease.load(std::memory_order_acquire);
    if (pendingLease == 0)
    {
        cancelAutomix();
        return;
    }
    if (cancelFadePending.load(std::memory_order_acquire))
        return;
    if (! automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
    {
        // Reuse the deck-fault ramp: Deck B eases out from its current
        // envelope while Deck A eases back to unity instead of jumping.
        const uint64_t startFrame = deviceOutputFrames.load(std::memory_order_acquire);
        const uint64_t fadeFrames = std::max<uint64_t>(
            1,
            static_cast<uint64_t>(automixCancelFadeFrames.load(std::memory_order_acquire)));
        automixPlan.faultCurrentStartGain.store(
            currentAutomixEnvelope(startFrame),
            std::memory_order_release);
        automixPlan.faultNextStartGain.store(
            nextAutomixEnvelope(startFrame),
            std::memory_order_release);
        automixPlan.faultFadeStartFrame.store(startFrame, std::memory_order_release);
        automixPlan.faultFadeEndFrame.store(startFrame + fadeFrames, std::memory_order_release);
        automixPlan.nextDeckFaulted.store(true, std::memory_order_release);
    }
    // cancelFadePending retires producer admission without invalidating the
    // render lease. The callback must keep consuming the already-buffered Deck
    // B tail until its envelope reaches zero; finalizeAutomixCancelFade()
    // retires the shared deck lease after that last audible frame.
    automixNextInputEnded.store(true, std::memory_order_release);
    cancelFadeEndFrame.store(
        automixPlan.faultFadeEndFrame.load(std::memory_order_acquire),
        std::memory_order_release);
    cancelFadeLease.store(pendingLease, std::memory_order_release);
    cancelFadePending.store(true, std::memory_order_release);
}

void PcmRingAudioSource::finalizeAutomixCancelFade(uint64_t absoluteStartFrame)
{
    if (! cancelFadePending.load(std::memory_order_acquire))
        return;
    if (absoluteStartFrame < cancelFadeEndFrame.load(std::memory_order_acquire))
        return;
    // Runs on the render thread once the cancel ramp has fully played out.
    // Both envelopes have reached their resting values (Deck A at 1.0, Deck B
    // at 0.0), so tearing the plan down here cannot produce a step.
    // Only tear down the plan we actually cancelled. If a hard cancel or a
    // fresh prepareAutomixDeck() raced in between, the pending lease no longer
    // matches and the new plan must stay untouched.
    const uint64_t pendingLease = cancelFadeLease.load(std::memory_order_acquire);
    uint64_t expectedPendingLease = pendingLease;
    if (pendingLease == 0
        || ! pendingAutomixDeckLease.compare_exchange_strong(
            expectedPendingLease,
            0,
            std::memory_order_acq_rel,
            std::memory_order_acquire))
        return;
    automixPlan.enabled.store(false, std::memory_order_release);
    automixPlan.fadeActivated.store(false, std::memory_order_release);
    automixPlan.nextDeckFaulted.store(false, std::memory_order_release);
    automixPlan.currentReplayGain.store(1.0f, std::memory_order_release);
    automixPlan.nextReplayGain.store(1.0f, std::memory_order_release);
    automixPlan.rhythmicBassSwap.store(false, std::memory_order_release);
    automixPlan.tailRide.store(false, std::memory_order_release);
    automixNextInputEnded.store(false, std::memory_order_release);
    automixNextHasAudio.store(false, std::memory_order_release);
    const int deckSlot = static_cast<int>(pendingLease & 1u);
    uint64_t expectedLease = pendingLease;
    deckLeases[static_cast<size_t>(deckSlot)].compare_exchange_strong(
        expectedLease,
        0,
        std::memory_order_acq_rel,
        std::memory_order_acquire);
    deckInputEnded[static_cast<size_t>(deckSlot)].store(false, std::memory_order_release);
    deckAudioAvailable[static_cast<size_t>(deckSlot)].store(false, std::memory_order_release);
    cancelFadePending.store(false, std::memory_order_release);
    // The deck FIFO reset is intentionally deferred: prepareAutomixDeck()
    // resets the slot before reuse, and taking a deck mutex here could block
    // the audio callback behind a producer memcpy.
}

void PcmRingAudioSource::beginSession(bool startPaused)
{
    sessionPaused.store(true, std::memory_order_release);
    echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
    dspChain.applySessionTransition(echo::DspSessionTransition::HardReplace);
    std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
    const auto previousGateGeneration = initialPlayGateGeneration.load(std::memory_order_acquire);
    if (isInitialPlayGateArmed(previousGateGeneration))
    {
        initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::SessionReplaced, std::memory_order_release);
        initialPlayGateState.store(InitialPlayGateState::Cancelled, std::memory_order_release);
    }
    else
    {
        initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::None, std::memory_order_release);
        initialPlayGateState.store(InitialPlayGateState::Idle, std::memory_order_release);
    }
    initialPlayGateGeneration.store(0, std::memory_order_release);
    initialPlayGateOperationId.store(0, std::memory_order_release);
    ditherProcessor.reset();
    session_.begin();
    cancelAutomix();
    initialProducerActive.store(true, std::memory_order_release);
    deckLeases[0].store(0, std::memory_order_release);
    deckLeases[1].store(0, std::memory_order_release);
    deckInputEnded[0].store(false, std::memory_order_release);
    deckInputEnded[1].store(false, std::memory_order_release);
    deckAudioAvailable[0].store(false, std::memory_order_release);
    deckAudioAvailable[1].store(false, std::memory_order_release);
    promotedDeckReplayGain.store(1.0f, std::memory_order_release);
    promotedDeckReleaseStartGain.store(1.0f, std::memory_order_release);
    promotedDeckReleaseStartFrame.store(0, std::memory_order_release);
    promotedDeckReleaseEndFrame.store(0, std::memory_order_release);
    {
        std::lock_guard<std::mutex> lock(fifoMutex);
        fifo.resetWhenReadersQuiescent();
        prebufferDeadlineNs.store(
            steadyNowNs() + static_cast<int64_t>(startupPrebufferTimeoutMs) * 1'000'000,
            std::memory_order_release);
    }
    {
        std::lock_guard<std::mutex> lock(automixMutex);
        automixFifo.resetWhenReadersQuiescent();
    }
    currentDeckSlot.store(0, std::memory_order_release);
    currentDeckLease.store(0, std::memory_order_release);

    sourceTrackFrames.store(0, std::memory_order_relaxed);
    deviceOutputFrames.store(0, std::memory_order_relaxed);
    underrunCallbacks.store(0, std::memory_order_relaxed);
    underrunFrames.store(0, std::memory_order_relaxed);
    sessionPaused.store(startPaused, std::memory_order_release);
    stopFadeRequested.store(false, std::memory_order_release);
    declickFadeGeneration.fetch_add(1, std::memory_order_acq_rel);
    prebuffering.store(startupPrebufferFrames > 0, std::memory_order_release);
}

void PcmRingAudioSource::reconfigureCapacity(int capacityFrames)
{
    const int nextCapacity = std::max(1, capacityFrames);
    if (fifo.getTotalSize() == nextCapacity && automixFifo.getTotalSize() == nextCapacity
        && static_cast<int>(buffer.size()) == nextCapacity * channels
        && static_cast<int>(automixBuffer.size()) == nextCapacity * channels)
        return;

    echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
    std::lock_guard<std::mutex> lock(fifoMutex);
    std::lock_guard<std::mutex> automixLock(automixMutex);
    fifo.reconfigureCapacityWhenReadersQuiescent(nextCapacity);
    automixFifo.reconfigureCapacityWhenReadersQuiescent(nextCapacity);
    buffer.assign(static_cast<size_t>(nextCapacity) * static_cast<size_t>(channels), 0.0f);
    automixBuffer.assign(static_cast<size_t>(nextCapacity) * static_cast<size_t>(channels), 0.0f);
}

int PcmRingAudioSource::getCapacityFrames() const
{
    return fifo.getTotalSize();
}

void PcmRingAudioSource::continueSessionAfterDrain()
{
    sessionPaused.store(true, std::memory_order_release);
    echo_audio_host::RenderEpoch::QuiescenceGuard renderQuiescence(renderEpoch);
    dspChain.applySessionTransition(echo::DspSessionTransition::HardReplace);
    {
        std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
        const auto gateGeneration = initialPlayGateGeneration.load(std::memory_order_acquire);
        if (isInitialPlayGateArmed(gateGeneration))
        {
            initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::SessionReplaced, std::memory_order_release);
            initialPlayGateState.store(InitialPlayGateState::Cancelled, std::memory_order_release);
        }
    }
    ditherProcessor.reset();
    session_.continueAfterDrain();
    cancelAutomix();
    initialProducerActive.store(true, std::memory_order_release);
    deckLeases[0].store(0, std::memory_order_release);
    deckLeases[1].store(0, std::memory_order_release);
    deckInputEnded[0].store(false, std::memory_order_release);
    deckInputEnded[1].store(false, std::memory_order_release);
    deckAudioAvailable[0].store(false, std::memory_order_release);
    deckAudioAvailable[1].store(false, std::memory_order_release);
    promotedDeckReplayGain.store(1.0f, std::memory_order_release);
    promotedDeckReleaseStartGain.store(1.0f, std::memory_order_release);
    promotedDeckReleaseStartFrame.store(0, std::memory_order_release);
    promotedDeckReleaseEndFrame.store(0, std::memory_order_release);
    {
        std::lock_guard<std::mutex> lock(fifoMutex);
        fifo.resetWhenReadersQuiescent();
        prebufferDeadlineNs.store(
            steadyNowNs() + static_cast<int64_t>(startupPrebufferTimeoutMs) * 1'000'000,
            std::memory_order_release);
    }
    {
        std::lock_guard<std::mutex> lock(automixMutex);
        automixFifo.resetWhenReadersQuiescent();
    }
    currentDeckSlot.store(0, std::memory_order_release);
    currentDeckLease.store(0, std::memory_order_release);
    sourceTrackFrames.store(0, std::memory_order_relaxed);
    deviceOutputFrames.store(0, std::memory_order_relaxed);
    underrunCallbacks.store(0, std::memory_order_relaxed);
    underrunFrames.store(0, std::memory_order_relaxed);
    sessionPaused.store(false, std::memory_order_release);
    stopFadeRequested.store(false, std::memory_order_release);
    prebuffering.store(startupPrebufferFrames > 0, std::memory_order_release);
}

void PcmRingAudioSource::markInputEnded()
{
    session_.markInputEnded();
}

void PcmRingAudioSource::requestStop()
{
    std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
    session_.requestStop();
    initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::StopRequested, std::memory_order_release);
    initialPlayGateState.store(InitialPlayGateState::Cancelled, std::memory_order_release);
    sessionPaused.store(false, std::memory_order_release);
    stopFadeRequested.store(true, std::memory_order_release);
}

bool PcmRingAudioSource::requestStopForGeneration(uint64_t generation)
{
    std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
    if (!session_.requestStop(generation))
        return false;
    initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::StopRequested, std::memory_order_release);
    initialPlayGateState.store(InitialPlayGateState::Cancelled, std::memory_order_release);
    sessionPaused.store(false, std::memory_order_release);
    stopFadeRequested.store(true, std::memory_order_release);
    return true;
}

void PcmRingAudioSource::setPaused(bool paused)
{
    if (paused)
    {
        const auto generation = initialPlayGateGeneration.load(std::memory_order_acquire);
        if (isInitialPlayGateArmed(generation))
            cancelInitialPlayGate(generation, InitialPlayGateReleaseReason::ExplicitPause);
    }
    sessionPaused.store(paused, std::memory_order_release);
}

void PcmRingAudioSource::armInitialPlayGate(uint64_t operationId)
{
    std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
    const uint64_t generation = session_.generation();
    if (generation == 0 || session_.isStopRequested())
        return;
    if (initialPlayGateGeneration.load(std::memory_order_acquire) == generation)
    {
        const auto state = initialPlayGateState.load(std::memory_order_acquire);
        if (state == InitialPlayGateState::Armed
            || state == InitialPlayGateState::WaitingForPcm
            || state == InitialPlayGateState::SoftTimeout
            || state == InitialPlayGateState::Released
            || state == InitialPlayGateState::Failed)
            return;
    }
    initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::None, std::memory_order_release);
    initialPlayGateArmedAtNs.store(0, std::memory_order_release);
    initialPlayGateState.store(InitialPlayGateState::Armed, std::memory_order_release);
    initialPlayGateGeneration.store(generation, std::memory_order_release);
    initialPlayGateOperationId.store(operationId, std::memory_order_release);
    sessionPaused.store(true, std::memory_order_release);
}

bool PcmRingAudioSource::isInitialPlayGateArmed(uint64_t generation) const noexcept
{
    if (generation == 0
        || initialPlayGateGeneration.load(std::memory_order_acquire) != generation)
        return false;
    const auto state = initialPlayGateState.load(std::memory_order_acquire);
    return state == InitialPlayGateState::Armed
        || state == InitialPlayGateState::WaitingForPcm
        || state == InitialPlayGateState::SoftTimeout;
}

bool PcmRingAudioSource::beginInitialPlayGateWait(uint64_t generation, uint64_t operationId)
{
    std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
    if (initialPlayGateGeneration.load(std::memory_order_acquire) != generation)
        return false;
    if (operationId != 0
        && initialPlayGateOperationId.load(std::memory_order_acquire) != operationId)
        return false;
    if (initialPlayGateState.load(std::memory_order_acquire) != InitialPlayGateState::Armed)
        return false;
    if (session_.generation() != generation || session_.isStopRequested())
        return false;

    const int64_t armedAt = steadyNowNs();
    initialPlayGateArmedAtNs.store(armedAt, std::memory_order_release);
    initialPlayGateState.store(InitialPlayGateState::WaitingForPcm, std::memory_order_release);
    prebufferDeadlineNs.store(
        armedAt + static_cast<int64_t>(std::max(0, startupPrebufferTimeoutMs)) * 1'000'000,
        std::memory_order_release);
    return true;
}

PcmRingAudioSource::InitialPlayGateSnapshot PcmRingAudioSource::pollInitialPlayGate(
    uint64_t generation,
    uint64_t operationId)
{
    std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
    InitialPlayGateSnapshot snapshot = getInitialPlayGateSnapshot();
    if (snapshot.generation != generation
        || (operationId != 0 && snapshot.operationId != operationId)
        || session_.generation() != generation
        || session_.isStopRequested())
        return snapshot;

    if (snapshot.state != InitialPlayGateState::WaitingForPcm
        && snapshot.state != InitialPlayGateState::SoftTimeout)
        return snapshot;

    const bool inputEnded = hasInputEnded();
    if (snapshot.readyFrames >= snapshot.requiredFrames
        || (inputEnded && snapshot.readyFrames > 0))
    {
        initialPlayGateReleaseReason.store(
            snapshot.readyFrames >= snapshot.requiredFrames
                ? InitialPlayGateReleaseReason::RequiredFramesReady
                : InitialPlayGateReleaseReason::ShortInputEnded,
            std::memory_order_release);
        initialPlayGateState.store(InitialPlayGateState::Released, std::memory_order_release);
        prebuffering.store(false, std::memory_order_release);
        sessionPaused.store(false, std::memory_order_release);
        return getInitialPlayGateSnapshot();
    }

    if (inputEnded)
    {
        initialPlayGateReleaseReason.store(InitialPlayGateReleaseReason::EmptyInputEnded, std::memory_order_release);
        initialPlayGateState.store(InitialPlayGateState::Failed, std::memory_order_release);
        return getInitialPlayGateSnapshot();
    }

    const int64_t softTimeoutAt = snapshot.armedAtNs
        + static_cast<int64_t>(std::max(0, startupPrebufferTimeoutMs)) * 1'000'000;
    if (steadyNowNs() >= softTimeoutAt)
    {
        initialPlayGateState.store(InitialPlayGateState::SoftTimeout, std::memory_order_release);
        return getInitialPlayGateSnapshot();
    }

    return snapshot;
}

void PcmRingAudioSource::cancelInitialPlayGate(
    uint64_t generation,
    InitialPlayGateReleaseReason reason,
    uint64_t operationId)
{
    std::lock_guard<std::mutex> gateLock(initialPlayGateMutex);
    if (generation == 0
        || initialPlayGateGeneration.load(std::memory_order_acquire) != generation)
        return;
    if (operationId != 0
        && initialPlayGateOperationId.load(std::memory_order_acquire) != operationId)
        return;
    initialPlayGateReleaseReason.store(reason, std::memory_order_release);
    initialPlayGateState.store(InitialPlayGateState::Cancelled, std::memory_order_release);
}

PcmRingAudioSource::InitialPlayGateSnapshot PcmRingAudioSource::getInitialPlayGateSnapshot() const
{
    const int currentSlot = currentDeckSlot.load(std::memory_order_acquire);
    InitialPlayGateSnapshot snapshot;
    snapshot.state = initialPlayGateState.load(std::memory_order_acquire);
    snapshot.releaseReason = initialPlayGateReleaseReason.load(std::memory_order_acquire);
    snapshot.generation = initialPlayGateGeneration.load(std::memory_order_acquire);
    snapshot.operationId = initialPlayGateOperationId.load(std::memory_order_acquire);
    snapshot.requiredFrames = requiredStartupSourceFrames();
    snapshot.readyFrames = fifoForDeck(currentSlot).getNumReady();
    snapshot.armedAtNs = initialPlayGateArmedAtNs.load(std::memory_order_acquire);
    return snapshot;
}

void PcmRingAudioSource::setGain(float nextGain)
{
    if (! std::isfinite(nextGain))
        return;

    gain.store(std::max(0.0f, std::min(1.0f, nextGain)), std::memory_order_release);
    if (isMqaPassthroughActive())
        refreshMqaPassthroughState();
}

bool PcmRingAudioSource::setMqaPassthroughActive(bool active)
{
    mqaPassthroughState_.store(
        active ? MqaPassthroughState::Active : MqaPassthroughState::Inactive,
        std::memory_order_release);
    return active && refreshMqaPassthroughState();
}

bool PcmRingAudioSource::isMqaPassthroughActive() const noexcept
{
    return mqaPassthroughState_.load(std::memory_order_acquire) == MqaPassthroughState::Active;
}

PcmRingAudioSource::MqaPassthroughState PcmRingAudioSource::mqaPassthroughState() const noexcept
{
    return mqaPassthroughState_.load(std::memory_order_acquire);
}

bool PcmRingAudioSource::refreshMqaPassthroughState()
{
    if (! isMqaPassthroughActive())
        return false;

    MqaPassthroughState interruption = MqaPassthroughState::Active;
    const float playbackRate = rateProcessor != nullptr ? rateProcessor->getRate() : 1.0f;
    if (std::abs(playbackRate - 1.0f) > 0.0001f)
        interruption = MqaPassthroughState::PlaybackRateActive;
    else if (automixPlan.enabled.load(std::memory_order_acquire)
        || automixEchoRinging.load(std::memory_order_acquire))
        interruption = MqaPassthroughState::TransitionActive;
    else if (std::abs(gain.load(std::memory_order_acquire) - 1.0f) > 0.000001f)
        interruption = MqaPassthroughState::VolumeNotUnity;
    else if (dspChain.isActive() || ditherProcessor.active())
        interruption = MqaPassthroughState::DspActive;
    else if (stopFadeRequested.load(std::memory_order_acquire))
        interruption = MqaPassthroughState::StopRequested;

    if (interruption != MqaPassthroughState::Active)
    {
        mqaPassthroughState_.store(interruption, std::memory_order_release);
        return false;
    }
    return true;
}

uint64_t PcmRingAudioSource::getGaplessBoundaryFrame() const
{
    return gaplessBoundaryFrame.load(std::memory_order_acquire);
}

uint64_t PcmRingAudioSource::getAutomixFadeStartFrame() const
{
    return automixPlan.fadeStartFrame.load(std::memory_order_acquire);
}

uint64_t PcmRingAudioSource::getAutomixFadeEndFrame() const
{
    return automixPlan.fadeEndFrame.load(std::memory_order_acquire);
}

bool PcmRingAudioSource::isAutomixActive() const
{
    return automixPlan.enabled.load(std::memory_order_acquire)
        && ! automixPlan.gapless.load(std::memory_order_acquire)
        && automixPlan.fadeActivated.load(std::memory_order_acquire)
        && ! automixPlan.nextDeckFaulted.load(std::memory_order_acquire);
}

void PcmRingAudioSource::configureDither(echo::PcmDitherMode mode, int bitDepth)
{
    ditherProcessor.configure(mode, bitDepth, channels);
}

void PcmRingAudioSource::resetDither()
{
    ditherProcessor.reset();
}

bool PcmRingAudioSource::isDrained() const
{
    const int currentSlot = currentDeckSlot.load(std::memory_order_acquire);
    {
        std::lock_guard<std::mutex> lock(mutexForDeck(currentSlot));
        if (! isDeckInputEnded(currentSlot) || fifoForDeck(currentSlot).getNumReady() != 0)
            return false;
    }

    if (! automixPlan.enabled.load(std::memory_order_acquire))
        return dspChain.isTailDrained();

    if (automixPlan.gapless.load(std::memory_order_acquire))
    {
        std::lock_guard<std::mutex> automixLock(automixMutex);
        return automixNextInputEnded.load(std::memory_order_acquire)
            && automixFifo.getNumReady() == 0
            && dspChain.isTailDrained();
    }

    const uint64_t nextLease = pendingAutomixDeckLease.load(std::memory_order_acquire);
    if (nextLease == 0)
        return dspChain.isTailDrained();
    const int nextSlot = static_cast<int>(nextLease & 1u);
    std::lock_guard<std::mutex> nextLock(mutexForDeck(nextSlot));
    return deckInputEnded[static_cast<size_t>(nextSlot)].load(std::memory_order_acquire)
        && fifoForDeck(nextSlot).getNumReady() == 0
        && dspChain.isTailDrained();
}

bool PcmRingAudioSource::hasInputEnded() const
{
    return isDeckInputEnded(currentDeckSlot.load(std::memory_order_acquire));
}

int PcmRingAudioSource::getReadyFrames() const
{
    const int currentSlot = currentDeckSlot.load(std::memory_order_acquire);
    std::lock_guard<std::mutex> lock(mutexForDeck(currentSlot));
    int ready = fifoForDeck(currentSlot).getNumReady();
    if (automixPlan.enabled.load(std::memory_order_acquire))
    {
        const int nextSlot = automixPlan.gapless.load(std::memory_order_acquire)
            ? 1
            : static_cast<int>(pendingAutomixDeckLease.load(std::memory_order_acquire) & 1u);
        if (nextSlot != currentSlot)
        {
            std::lock_guard<std::mutex> nextLock(mutexForDeck(nextSlot));
            ready += fifoForDeck(nextSlot).getNumReady();
        }
    }
    return ready;
}

bool PcmRingAudioSource::isReadyToResume() const
{
    const int currentSlot = currentDeckSlot.load(std::memory_order_acquire);
    std::lock_guard<std::mutex> lock(mutexForDeck(currentSlot));
    const int requiredFrames = std::max(1, requiredStartupSourceFrames());
    return fifoForDeck(currentSlot).getNumReady() >= requiredFrames
        || isDeckInputEnded(currentSlot);
}

uint64_t PcmRingAudioSource::getFramesPlayed() const
{
    return getSourceTrackFrames();
}

uint64_t PcmRingAudioSource::getSourceTrackFrames() const
{
    return sourceTrackFrames.load(std::memory_order_relaxed);
}

uint64_t PcmRingAudioSource::getDeviceOutputFrames() const
{
    return deviceOutputFrames.load(std::memory_order_relaxed);
}

uint64_t PcmRingAudioSource::getUnderrunCallbacks() const
{
    return underrunCallbacks.load(std::memory_order_relaxed);
}

uint64_t PcmRingAudioSource::getUnderrunFrames() const
{
    return underrunFrames.load(std::memory_order_relaxed);
}

echo::ReplayGainProcessor* PcmRingAudioSource::getReplayGainProcessor()
{
    return replayGainProcessor;
}

echo::PlaybackRateProcessor* PcmRingAudioSource::getRateProcessor()
{
    return rateProcessor;
}

echo::LevelMeterProcessor* PcmRingAudioSource::getMeterProcessor()
{
    return meterProcessor;
}

void PcmRingAudioSource::copyFromInput(const float* source, int startFrame, int frameCount, int deckSlot)
{
    if (frameCount <= 0)
        return;

    std::memcpy(
        bufferForDeck(deckSlot).data() + static_cast<size_t>(startFrame * channels),
        source,
        static_cast<size_t>(frameCount * channels) * sizeof(float));
}

float PcmRingAudioSource::dbToGain(double db)
{
    if (! std::isfinite(db))
        return 1.0f;

    return static_cast<float>(std::pow(10.0, std::max(-24.0, std::min(12.0, db)) / 20.0));
}

void PcmRingAudioSource::activateAutomixFadeIfReady(uint64_t absoluteStartFrame, int frameCount)
{
    if (frameCount <= 0 || ! automixPlan.enabled.load(std::memory_order_acquire) || automixPlan.fadeActivated.load(std::memory_order_acquire))
        return;

    const uint64_t plannedFadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const bool echoThrowArmed = automixEchoRinging.load(std::memory_order_acquire);
    const uint64_t activationStartFrame = echoThrowArmed
        ? std::min(
            plannedFadeStartFrame,
            automixEchoCaptureStartFrame.load(std::memory_order_acquire))
        : plannedFadeStartFrame;
    const bool reachedPlannedWindow =
        absoluteStartFrame + static_cast<uint64_t>(frameCount) > plannedFadeStartFrame;
    const bool reachedActivationWindow =
        absoluteStartFrame + static_cast<uint64_t>(frameCount) > activationStartFrame;
    if (! reachedActivationWindow)
    {
        // A plan scheduled past the actually decodable audio (metadata longer
        // than the file) would otherwise never activate: once Deck A drains,
        // framesPlayed freezes below fadeStartFrame and playback hangs silent.
        // When Deck A's remaining PCM can no longer reach the planned fade
        // start, pull the fade forward instead of waiting for a watchdog.
        const int currentSlot = currentDeckSlot.load(std::memory_order_acquire);
        if (! isDeckInputEnded(currentSlot))
            return;
        const uint64_t remainingFrames = static_cast<uint64_t>(
            std::max(0, fifoForDeck(currentSlot).getNumReady()));
        if (absoluteStartFrame + remainingFrames > plannedFadeStartFrame)
            return;
    }

    const uint64_t nextLease = pendingAutomixDeckLease.load(std::memory_order_acquire);
    if (workshopWindow.load(std::memory_order_acquire) != nullptr
        && (!reachedPlannedWindow || absoluteStartFrame > plannedFadeStartFrame || nextLease == 0
            || fifoForDeck(static_cast<int>(nextLease & 1u)).getNumReady() <= 0))
    {
        workshopDeadlineMissed.store(true, std::memory_order_release);
        return;
    }
    if (nextLease == 0
        || fifoForDeck(static_cast<int>(nextLease & 1u)).getNumReady() <= 0)
        return;

    const uint64_t effectiveFadeStartFrame = reachedPlannedWindow
        ? std::max(absoluteStartFrame, plannedFadeStartFrame)
        : reachedActivationWindow && echoThrowArmed
            ? plannedFadeStartFrame
            : absoluteStartFrame;
    const uint64_t overlapFrames = automixPlan.overlapFrames.load(std::memory_order_acquire);
    const uint64_t releaseFrames = std::max<uint64_t>(
        1,
        automixPlan.gainReleaseEndFrame.load(std::memory_order_acquire)
            - automixPlan.fadeEndFrame.load(std::memory_order_acquire));
    automixPlan.fadeStartFrame.store(effectiveFadeStartFrame, std::memory_order_release);
    automixPlan.fadeEndFrame.store(effectiveFadeStartFrame + overlapFrames, std::memory_order_release);
    automixPlan.gainReleaseEndFrame.store(effectiveFadeStartFrame + overlapFrames + releaseFrames, std::memory_order_release);
    std::fill(automixCurrentLowState.begin(), automixCurrentLowState.end(), 0.0f);
    std::fill(automixNextLowState.begin(), automixNextLowState.end(), 0.0f);
    std::fill(automixCurrentPresenceState.begin(), automixCurrentPresenceState.end(), 0.0f);
    std::fill(automixNextPresenceState.begin(), automixNextPresenceState.end(), 0.0f);
    if (echoThrowArmed)
        retimeAutomixEchoThrow(effectiveFadeStartFrame + overlapFrames);
    automixPlan.fadeActivated.store(true, std::memory_order_release);
}

void PcmRingAudioSource::configureDeclickRamp(double sampleRate)
{
    const double safeSampleRate = sampleRate > 0.0 ? sampleRate : 44100.0;
    constexpr double bassSwapCutoffHz = 250.0;
    constexpr double presenceCutoffHz = 4'000.0;
    automixLowPassCoefficient.store(
        static_cast<float>(1.0 - std::exp((-2.0 * 3.14159265358979323846 * bassSwapCutoffHz) / safeSampleRate)),
        std::memory_order_release);
    automixPresenceLowPassCoefficient.store(
        static_cast<float>(1.0 - std::exp((-2.0 * 3.14159265358979323846 * presenceCutoffHz) / safeSampleRate)),
        std::memory_order_release);
    declickRampFrames = std::max(1, static_cast<int>(std::ceil(safeSampleRate * 0.006)));
    // ~20ms, matching the failAutomixDeck recovery ramp the daemon requests.
    automixCancelFadeFrames.store(
        std::max(1, static_cast<int>(std::ceil(safeSampleRate * 0.02))),
        std::memory_order_release);

    // Echo Throw owns a fixed one-second delay line. Allocation and clearing
    // happen while the render epoch is quiescent, never in the audio callback.
    automixEchoCapacityFrames = std::max<size_t>(
        2,
        static_cast<size_t>(std::ceil(safeSampleRate)) + 1);
    automixEchoDelayBuffer.assign(
        automixEchoCapacityFrames * static_cast<size_t>(channels),
        0.0f);
    automixEchoGenerationTags.assign(automixEchoCapacityFrames, 0);
    automixEchoFrameTags.assign(automixEchoCapacityFrames, UINT64_MAX);
    stopAutomixEchoThrow();
}

void PcmRingAudioSource::armAutomixEchoThrow(
    uint64_t fadeEndFrame,
    double delaySeconds,
    double sendGain,
    double feedbackGain)
{
    if (automixEchoCapacityFrames < 2 || automixEchoDelayBuffer.empty())
        return;

    const int sampleRate = std::max(1, renderSampleRate.load(std::memory_order_acquire));
    const double safeDelaySeconds = std::isfinite(delaySeconds)
        ? std::clamp(delaySeconds, 0.08, 1.0)
        : 0.25;
    const uint64_t delayFrames = std::clamp<uint64_t>(
        static_cast<uint64_t>(std::llround(safeDelaySeconds * static_cast<double>(sampleRate))),
        1,
        static_cast<uint64_t>(automixEchoCapacityFrames - 1));
    automixEchoRinging.store(false, std::memory_order_release);
    automixEchoGeneration.fetch_add(1, std::memory_order_acq_rel);
    automixEchoDelayFrames.store(delayFrames, std::memory_order_release);
    automixEchoSendGain.store(
        static_cast<float>(std::clamp(std::isfinite(sendGain) ? sendGain : 0.6, 0.0, 0.8)),
        std::memory_order_release);
    automixEchoFeedbackGain.store(
        static_cast<float>(std::clamp(std::isfinite(feedbackGain) ? feedbackGain : 0.45, 0.0, 0.65)),
        std::memory_order_release);
    retimeAutomixEchoThrow(fadeEndFrame);
    automixEchoRinging.store(true, std::memory_order_release);
}

void PcmRingAudioSource::retimeAutomixEchoThrow(uint64_t fadeEndFrame)
{
    const uint64_t sampleRate = static_cast<uint64_t>(
        std::max(1, renderSampleRate.load(std::memory_order_acquire)));
    const uint64_t openFrames = std::max<uint64_t>(1, (sampleRate * 15) / 100);
    const uint64_t delayFrames = automixEchoDelayFrames.load(std::memory_order_acquire);
    const float feedback = automixEchoFeedbackGain.load(std::memory_order_acquire);
    automixEchoCaptureStartFrame.store(
        fadeEndFrame > openFrames ? fadeEndFrame - openFrames : 0,
        std::memory_order_release);
    automixEchoCaptureEndFrame.store(fadeEndFrame, std::memory_order_release);
    const uint64_t audibleRepeats = feedback > 0.0f && feedback < 1.0f
        ? std::clamp<uint64_t>(
            static_cast<uint64_t>(std::ceil(std::log(0.02) / std::log(feedback))),
            3,
            10)
        : 3;
    const uint64_t repeatTailFrames = delayFrames > (UINT64_MAX - fadeEndFrame) / audibleRepeats
        ? UINT64_MAX - fadeEndFrame
        : delayFrames * audibleRepeats;
    automixEchoRingEndFrame.store(fadeEndFrame + repeatTailFrames, std::memory_order_release);
}

void PcmRingAudioSource::stopAutomixEchoThrow()
{
    automixEchoRinging.store(false, std::memory_order_release);
    automixEchoGeneration.fetch_add(1, std::memory_order_acq_rel);
}

float PcmRingAudioSource::processAutomixEchoSample(
    float sample,
    int channel,
    uint64_t absoluteFrame)
{
    if (! automixEchoRinging.load(std::memory_order_acquire)
        || channel < 0
        || channel >= channels
        || automixEchoCapacityFrames < 2)
        return 0.0f;

    const uint64_t ringEndFrame = automixEchoRingEndFrame.load(std::memory_order_acquire);
    if (absoluteFrame >= ringEndFrame)
    {
        automixEchoRinging.store(false, std::memory_order_release);
        return 0.0f;
    }

    const uint64_t generation = automixEchoGeneration.load(std::memory_order_acquire);
    const uint64_t delayFrames = automixEchoDelayFrames.load(std::memory_order_acquire);
    float delayed = 0.0f;
    if (absoluteFrame >= delayFrames)
    {
        const uint64_t readFrame = absoluteFrame - delayFrames;
        const size_t readIndex = static_cast<size_t>(readFrame % automixEchoCapacityFrames);
        if (automixEchoGenerationTags[readIndex] == generation
            && automixEchoFrameTags[readIndex] == readFrame)
        {
            delayed = automixEchoDelayBuffer[
                static_cast<size_t>(channel) * automixEchoCapacityFrames + readIndex];
        }
    }

    float sendGain = 0.0f;
    const uint64_t captureStart = automixEchoCaptureStartFrame.load(std::memory_order_acquire);
    const uint64_t captureEnd = automixEchoCaptureEndFrame.load(std::memory_order_acquire);
    if (automixPlan.fadeActivated.load(std::memory_order_acquire)
        && absoluteFrame >= captureStart
        && absoluteFrame < captureEnd)
    {
        const double progress = static_cast<double>(absoluteFrame - captureStart)
            / static_cast<double>(std::max<uint64_t>(1, captureEnd - captureStart));
        sendGain = automixEchoSendGain.load(std::memory_order_acquire) * smoothStep(progress);
    }

    const size_t writeIndex = static_cast<size_t>(absoluteFrame % automixEchoCapacityFrames);
    automixEchoDelayBuffer[
        static_cast<size_t>(channel) * automixEchoCapacityFrames + writeIndex]
        = sample * sendGain
        + delayed * automixEchoFeedbackGain.load(std::memory_order_acquire);
    automixEchoGenerationTags[writeIndex] = generation;
    automixEchoFrameTags[writeIndex] = absoluteFrame;
    return delayed;
}

void PcmRingAudioSource::applyDeclickRamp(echo::FloatAudioBuffer& output, int startSample, int frameCount)
{
    const auto generation = declickFadeGeneration.load(std::memory_order_acquire);
    if (generation != appliedDeclickFadeGeneration)
    {
        appliedDeclickFadeGeneration = generation;
        declickGain = 0.0f;
    }

    const float targetGain = stopFadeRequested.load(std::memory_order_acquire) ? 0.0f : 1.0f;
    if (declickGain == targetGain && targetGain == 1.0f)
        return;

    const float step = 1.0f / static_cast<float>(std::max(1, declickRampFrames));
    const int outputChannels = output.getNumChannels();

    for (int frame = 0; frame < frameCount; ++frame)
    {
        for (int ch = 0; ch < outputChannels; ++ch)
            output.getWritePointer(ch, startSample)[frame] *= declickGain;

        if (declickGain < targetGain)
            declickGain = std::min(targetGain, declickGain + step);
        else if (declickGain > targetGain)
            declickGain = std::max(targetGain, declickGain - step);
    }
}

float PcmRingAudioSource::promotedDeckGainEnvelope(uint64_t absoluteFrame) const
{
    if (currentDeckLease.load(std::memory_order_acquire) == 0)
        return 1.0f;
    const uint64_t startFrame = promotedDeckReleaseStartFrame.load(std::memory_order_acquire);
    const uint64_t endFrame = promotedDeckReleaseEndFrame.load(std::memory_order_acquire);
    const float startGain = promotedDeckReleaseStartGain.load(std::memory_order_acquire);
    if (absoluteFrame <= startFrame)
        return startGain;
    if (absoluteFrame >= endFrame || endFrame <= startFrame)
        return 1.0f;
    const double progress = static_cast<double>(absoluteFrame - startFrame)
        / static_cast<double>(endFrame - startFrame);
    return startGain + (1.0f - startGain) * smoothStep(progress);
}

float PcmRingAudioSource::currentAutomixEnvelope(uint64_t absoluteFrame) const
{
    if (automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
    {
        const uint64_t start = automixPlan.faultFadeStartFrame.load(std::memory_order_acquire);
        const uint64_t end = automixPlan.faultFadeEndFrame.load(std::memory_order_acquire);
        const float startGain = automixPlan.faultCurrentStartGain.load(std::memory_order_acquire);
        if (absoluteFrame >= end)
            return 1.0f;
        if (absoluteFrame >= start)
        {
            const float progress = static_cast<float>(absoluteFrame - start)
                / static_cast<float>(std::max<uint64_t>(1, end - start));
            return startGain + (1.0f - startGain) * progress;
        }
    }
    const bool enabled = automixPlan.enabled.load(std::memory_order_acquire);
    const bool fadeActivated = automixPlan.fadeActivated.load(std::memory_order_acquire);
    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    const float currentGain = automixPlan.currentGain.load(std::memory_order_acquire);
    if (! enabled)
        return 1.0f;

    if (! fadeActivated || absoluteFrame < fadeStartFrame)
        return 1.0f;

    if (absoluteFrame >= fadeEndFrame)
        return 0.0f;

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double envelopeProgress = automixPlan.tailRide.load(std::memory_order_acquire)
        ? std::pow(progress, 1.65)
        : automixHeldProgress(progress,
            automixPlan.holdStart.load(std::memory_order_acquire),
            automixPlan.holdEnd.load(std::memory_order_acquire));
    const float smoothProgress = smoothStep(envelopeProgress);
    const float gainMatch = 1.0f + ((currentGain - 1.0f) * smoothProgress);
    return gainMatch * static_cast<float>(std::cos(envelopeProgress * halfPi));
}

float PcmRingAudioSource::nextAutomixEnvelope(uint64_t absoluteFrame) const
{
    if (automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
    {
        const uint64_t start = automixPlan.faultFadeStartFrame.load(std::memory_order_acquire);
        const uint64_t end = automixPlan.faultFadeEndFrame.load(std::memory_order_acquire);
        const float startGain = automixPlan.faultNextStartGain.load(std::memory_order_acquire);
        if (absoluteFrame >= end)
            return 0.0f;
        if (absoluteFrame >= start)
        {
            const float progress = static_cast<float>(absoluteFrame - start)
                / static_cast<float>(std::max<uint64_t>(1, end - start));
            return startGain * (1.0f - progress);
        }
    }
    const bool enabled = automixPlan.enabled.load(std::memory_order_acquire);
    const bool fadeActivated = automixPlan.fadeActivated.load(std::memory_order_acquire);
    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    const uint64_t gainReleaseEndFrame = automixPlan.gainReleaseEndFrame.load(std::memory_order_acquire);
    const float nextGain = automixPlan.nextGain.load(std::memory_order_acquire);
    if (! enabled || ! fadeActivated || absoluteFrame < fadeStartFrame)
        return 0.0f;

    if (absoluteFrame >= gainReleaseEndFrame)
        return 1.0f;

    if (absoluteFrame >= fadeEndFrame)
    {
        const double releaseProgress = static_cast<double>(absoluteFrame - fadeEndFrame)
            / static_cast<double>(std::max<uint64_t>(1, gainReleaseEndFrame - fadeEndFrame));
        const float smoothRelease = smoothStep(releaseProgress);
        return nextGain + ((1.0f - nextGain) * smoothRelease);
    }

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double envelopeProgress = automixPlan.tailRide.load(std::memory_order_acquire)
        ? std::pow(progress, 0.72)
        : automixHeldProgress(progress,
            automixPlan.holdStart.load(std::memory_order_acquire),
            automixPlan.holdEnd.load(std::memory_order_acquire));
    return nextGain * static_cast<float>(std::sin(envelopeProgress * halfPi));
}

float PcmRingAudioSource::currentAutomixLowEnvelope(uint64_t absoluteFrame) const
{
    const float regularEnvelope = currentAutomixEnvelope(absoluteFrame);
    const bool shapedHandoff = automixPlan.rhythmicBassSwap.load(std::memory_order_acquire)
        || automixPlan.tailRide.load(std::memory_order_acquire);
    if (! shapedHandoff || automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
        return regularEnvelope;

    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    if (absoluteFrame < fadeStartFrame || absoluteFrame >= fadeEndFrame)
        return regularEnvelope;

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double swapStart = automixPlan.lowSwapStart.load(std::memory_order_acquire);
    const double swapEnd = automixPlan.lowSwapEnd.load(std::memory_order_acquire);
    const double bassProgress = std::clamp((progress - swapStart) / (swapEnd - swapStart), 0.0, 1.0);
    const float currentGain = automixPlan.currentGain.load(std::memory_order_acquire);
    const float gainMatch = 1.0f + ((currentGain - 1.0f) * smoothStep(progress));
    return gainMatch * static_cast<float>(std::cos(bassProgress * halfPi));
}

float PcmRingAudioSource::nextAutomixLowEnvelope(uint64_t absoluteFrame) const
{
    const float regularEnvelope = nextAutomixEnvelope(absoluteFrame);
    const bool shapedHandoff = automixPlan.rhythmicBassSwap.load(std::memory_order_acquire)
        || automixPlan.tailRide.load(std::memory_order_acquire);
    if (! shapedHandoff || automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
        return regularEnvelope;

    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    if (absoluteFrame < fadeStartFrame || absoluteFrame >= fadeEndFrame)
        return regularEnvelope;

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double swapStart = automixPlan.lowSwapStart.load(std::memory_order_acquire);
    const double swapEnd = automixPlan.lowSwapEnd.load(std::memory_order_acquire);
    const double bassProgress = std::clamp((progress - swapStart) / (swapEnd - swapStart), 0.0, 1.0);
    const float nextGain = automixPlan.nextGain.load(std::memory_order_acquire);
    return nextGain * static_cast<float>(std::sin(bassProgress * halfPi));
}

float PcmRingAudioSource::currentAutomixHighEnvelope(uint64_t absoluteFrame) const
{
    const float regularEnvelope = currentAutomixEnvelope(absoluteFrame);
    if (! automixPlan.rhythmicBassSwap.load(std::memory_order_acquire)
        || automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
        return regularEnvelope;

    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    if (absoluteFrame < fadeStartFrame || absoluteFrame >= fadeEndFrame)
        return regularEnvelope;

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double swapStart = automixPlan.highSwapStart.load(std::memory_order_acquire);
    const double swapEnd = automixPlan.highSwapEnd.load(std::memory_order_acquire);
    const double highHandoffProgress = std::clamp((progress - swapStart) / (swapEnd - swapStart), 0.0, 1.0);
    const float currentGain = automixPlan.currentGain.load(std::memory_order_acquire);
    const float gainMatch = 1.0f + ((currentGain - 1.0f) * smoothStep(progress));
    return gainMatch * static_cast<float>(std::cos(highHandoffProgress * halfPi));
}

float PcmRingAudioSource::nextAutomixHighEnvelope(uint64_t absoluteFrame) const
{
    const float regularEnvelope = nextAutomixEnvelope(absoluteFrame);
    if (! automixPlan.rhythmicBassSwap.load(std::memory_order_acquire)
        || automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
        return regularEnvelope;

    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    if (absoluteFrame < fadeStartFrame || absoluteFrame >= fadeEndFrame)
        return regularEnvelope;

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double swapStart = automixPlan.highSwapStart.load(std::memory_order_acquire);
    const double swapEnd = automixPlan.highSwapEnd.load(std::memory_order_acquire);
    const double highHandoffProgress = std::clamp((progress - swapStart) / (swapEnd - swapStart), 0.0, 1.0);
    const float nextGain = automixPlan.nextGain.load(std::memory_order_acquire);
    return nextGain * static_cast<float>(std::sin(highHandoffProgress * halfPi));
}

float PcmRingAudioSource::currentAutomixCenterEnvelope(uint64_t absoluteFrame) const
{
    const float regularEnvelope = currentAutomixEnvelope(absoluteFrame);
    if (! automixPlan.rhythmicBassSwap.load(std::memory_order_acquire)
        || automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
        return regularEnvelope;

    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    if (absoluteFrame < fadeStartFrame || absoluteFrame >= fadeEndFrame)
        return regularEnvelope;

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double exitStart = automixPlan.vocalExitStartProgress.load(std::memory_order_acquire);
    const double exitEnd = std::min(0.94, exitStart + 0.27);
    const double exitProgress = std::clamp((progress - exitStart) / (exitEnd - exitStart), 0.0, 1.0);
    const float currentGain = automixPlan.currentGain.load(std::memory_order_acquire);
    const float gainMatch = 1.0f + ((currentGain - 1.0f) * smoothStep(progress));
    return gainMatch * static_cast<float>(std::cos(exitProgress * halfPi));
}

float PcmRingAudioSource::nextAutomixCenterEnvelope(uint64_t absoluteFrame) const
{
    const float regularEnvelope = nextAutomixEnvelope(absoluteFrame);
    if (! automixPlan.rhythmicBassSwap.load(std::memory_order_acquire)
        || automixPlan.nextDeckFaulted.load(std::memory_order_acquire))
        return regularEnvelope;

    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const uint64_t fadeEndFrame = automixPlan.fadeEndFrame.load(std::memory_order_acquire);
    if (absoluteFrame < fadeStartFrame || absoluteFrame >= fadeEndFrame)
        return regularEnvelope;

    const double progress = static_cast<double>(absoluteFrame - fadeStartFrame)
        / static_cast<double>(std::max<uint64_t>(1, fadeEndFrame - fadeStartFrame));
    const double entryStart = automixPlan.vocalEntryStartProgress.load(std::memory_order_acquire);
    const double entryEnd = std::min(0.96, entryStart + 0.16);
    const double entryProgress = std::clamp((progress - entryStart) / (entryEnd - entryStart), 0.0, 1.0);
    const float nextGain = automixPlan.nextGain.load(std::memory_order_acquire);
    return nextGain * static_cast<float>(std::sin(entryProgress * halfPi));
}

void PcmRingAudioSource::copyToOutput(
    int startFrame,
    int frameCount,
    echo::FloatAudioBuffer& output,
    int outputStart,
    uint64_t absoluteOutputStartFrame,
    int sourceFrameOffset,
    float sourceFramesPerOutputFrame,
    bool transparentPassthrough,
    int deckSlot)
{
    if (frameCount <= 0)
        return;

    const float* source = bufferForDeck(deckSlot).data() + static_cast<size_t>(startFrame * channels);
    const bool smartTransitionActive =
        automixPlan.enabled.load(std::memory_order_acquire)
        && ! automixPlan.gapless.load(std::memory_order_acquire);
    const bool rhythmicBassSwap = smartTransitionActive
        && automixPlan.rhythmicBassSwap.load(std::memory_order_acquire);
    const bool shapedHandoff = rhythmicBassSwap
        || (smartTransitionActive && automixPlan.tailRide.load(std::memory_order_acquire));
    const float lowPassCoefficient = automixLowPassCoefficient.load(std::memory_order_acquire);
    const float presenceLowPassCoefficient = automixPresenceLowPassCoefficient.load(std::memory_order_acquire);
    const float deckReplayGain = smartTransitionActive
        ? automixPlan.currentReplayGain.load(std::memory_order_acquire)
        : currentDeckLease.load(std::memory_order_acquire) != 0
            ? promotedDeckReplayGain.load(std::memory_order_acquire)
            : 1.0f;
    const int outputChannels = output.getNumChannels();

    if (transparentPassthrough)
    {
        for (int channel = 0; channel < outputChannels; ++channel)
        {
            float* destination = output.getWritePointer(channel, outputStart);
            const int sourceChannel = std::min(channel, channels - 1);
            for (int frame = 0; frame < frameCount; ++frame)
                destination[frame] = source[frame * channels + sourceChannel];
        }
        return;
    }

    // The rhythmic profile uses a bounded, model-free pseudo-stem split. Low and
    // high bands stand in for bass and drum transients; the centre of the vocal
    // presence band is handed over separately from the stereo accompaniment.
    if (rhythmicBassSwap && channels == 2 && outputChannels == 2)
    {
        float* leftDestination = output.getWritePointer(0, outputStart);
        float* rightDestination = output.getWritePointer(1, outputStart);
        float& leftLowState = automixCurrentLowState[0];
        float& rightLowState = automixCurrentLowState[1];
        float& leftPresenceState = automixCurrentPresenceState[0];
        float& rightPresenceState = automixCurrentPresenceState[1];
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float left = source[frame * channels];
            const float right = source[frame * channels + 1];
            leftLowState += lowPassCoefficient * (left - leftLowState);
            rightLowState += lowPassCoefficient * (right - rightLowState);
            leftPresenceState += presenceLowPassCoefficient * (left - leftPresenceState);
            rightPresenceState += presenceLowPassCoefficient * (right - rightPresenceState);

            const float leftMiddle = leftPresenceState - leftLowState;
            const float rightMiddle = rightPresenceState - rightLowState;
            const float centerMiddle = (leftMiddle + rightMiddle) * 0.5f;
            const float leftSideMiddle = leftMiddle - centerMiddle;
            const float rightSideMiddle = rightMiddle - centerMiddle;
            const float leftHigh = left - leftPresenceState;
            const float rightHigh = right - rightPresenceState;
            const uint64_t absoluteFrame = absoluteOutputStartFrame + static_cast<uint64_t>(std::floor(
                static_cast<double>(sourceFrameOffset + frame)
                / std::max(0.0001, static_cast<double>(sourceFramesPerOutputFrame))));
            const float deckEnvelope = currentAutomixEnvelope(absoluteFrame)
                * promotedDeckGainEnvelope(absoluteFrame);
            const float lowEnvelope = currentAutomixLowEnvelope(absoluteFrame)
                * promotedDeckGainEnvelope(absoluteFrame);
            const float highEnvelope = currentAutomixHighEnvelope(absoluteFrame)
                * promotedDeckGainEnvelope(absoluteFrame);
            const float centerEnvelope = currentAutomixCenterEnvelope(absoluteFrame)
                * promotedDeckGainEnvelope(absoluteFrame);
            leftDestination[frame] = (leftLowState * lowEnvelope
                + leftSideMiddle * deckEnvelope
                + centerMiddle * centerEnvelope
                + leftHigh * highEnvelope) * deckReplayGain;
            rightDestination[frame] = (rightLowState * lowEnvelope
                + rightSideMiddle * deckEnvelope
                + centerMiddle * centerEnvelope
                + rightHigh * highEnvelope) * deckReplayGain;
            leftDestination[frame] += processAutomixEchoSample(
                left * deckReplayGain, 0, absoluteFrame);
            rightDestination[frame] += processAutomixEchoSample(
                right * deckReplayGain, 1, absoluteFrame);
        }
        return;
    }

    for (int channel = 0; channel < outputChannels; ++channel)
    {
        float* destination = output.getWritePointer(channel, outputStart);
        const int sourceChannel = std::min(channel, channels - 1);

        float& lowState = automixCurrentLowState[static_cast<size_t>(sourceChannel)];
        float& presenceState = automixCurrentPresenceState[static_cast<size_t>(sourceChannel)];
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float sample = source[frame * channels + sourceChannel];
            const uint64_t absoluteFrame = absoluteOutputStartFrame + static_cast<uint64_t>(std::floor(
                static_cast<double>(sourceFrameOffset + frame)
                / std::max(0.0001, static_cast<double>(sourceFramesPerOutputFrame))));
            const float envelope = currentAutomixEnvelope(absoluteFrame)
                * promotedDeckGainEnvelope(absoluteFrame);
            if (workshopWindow.load(std::memory_order_acquire) != nullptr
                && absoluteFrame < automixPlan.fadeStartFrame.load(std::memory_order_acquire))
            {
                destination[frame] = sample;
                continue;
            }
            if (shapedHandoff)
            {
                lowState += lowPassCoefficient * (sample - lowState);
                presenceState += presenceLowPassCoefficient * (sample - presenceState);
                const float middle = presenceState - lowState;
                const float high = sample - presenceState;
                const float middleEnvelope = rhythmicBassSwap
                    ? currentAutomixCenterEnvelope(absoluteFrame)
                        * promotedDeckGainEnvelope(absoluteFrame)
                    : envelope;
                destination[frame] = (lowState * currentAutomixLowEnvelope(absoluteFrame)
                    + middle * middleEnvelope
                    + high * currentAutomixHighEnvelope(absoluteFrame))
                    * deckReplayGain;
            }
            else
            {
                destination[frame] = sample * deckReplayGain * envelope;
            }
            destination[frame] += processAutomixEchoSample(
                sample * deckReplayGain,
                sourceChannel,
                absoluteFrame);
        }
    }
}

void PcmRingAudioSource::copyToAutomixBuffer(const float* source, int startFrame, int frameCount, int deckSlot)
{
    if (frameCount <= 0)
        return;

    std::memcpy(
        bufferForDeck(deckSlot).data() + static_cast<size_t>(startFrame * channels),
        source,
        static_cast<size_t>(frameCount * channels) * sizeof(float));
}

void PcmRingAudioSource::addAutomixToOutput(
    int startFrame,
    int frameCount,
    echo::FloatAudioBuffer& output,
    int outputStart,
    uint64_t absoluteOutputStartFrame,
    int sourceFrameOffset,
    float sourceFramesPerOutputFrame,
    int deckSlot)
{
    if (frameCount <= 0)
        return;

    const float* source = bufferForDeck(deckSlot).data() + static_cast<size_t>(startFrame * channels);
    const float deckReplayGain = automixPlan.nextReplayGain.load(std::memory_order_acquire);
    const bool rhythmicBassSwap = automixPlan.rhythmicBassSwap.load(std::memory_order_acquire);
    const bool shapedHandoff = rhythmicBassSwap
        || automixPlan.tailRide.load(std::memory_order_acquire);
    const float lowPassCoefficient = automixLowPassCoefficient.load(std::memory_order_acquire);
    const float presenceLowPassCoefficient = automixPresenceLowPassCoefficient.load(std::memory_order_acquire);
    const int outputChannels = output.getNumChannels();

    // Mirror the outgoing pseudo-stem split so each musical role changes hands
    // at its own moment while the user's DSP still runs once on the final sum.
    if (rhythmicBassSwap && channels == 2 && outputChannels == 2)
    {
        float* leftDestination = output.getWritePointer(0, outputStart);
        float* rightDestination = output.getWritePointer(1, outputStart);
        float& leftLowState = automixNextLowState[0];
        float& rightLowState = automixNextLowState[1];
        float& leftPresenceState = automixNextPresenceState[0];
        float& rightPresenceState = automixNextPresenceState[1];
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float left = source[frame * channels];
            const float right = source[frame * channels + 1];
            leftLowState += lowPassCoefficient * (left - leftLowState);
            rightLowState += lowPassCoefficient * (right - rightLowState);
            leftPresenceState += presenceLowPassCoefficient * (left - leftPresenceState);
            rightPresenceState += presenceLowPassCoefficient * (right - rightPresenceState);

            const float leftMiddle = leftPresenceState - leftLowState;
            const float rightMiddle = rightPresenceState - rightLowState;
            const float centerMiddle = (leftMiddle + rightMiddle) * 0.5f;
            const float leftSideMiddle = leftMiddle - centerMiddle;
            const float rightSideMiddle = rightMiddle - centerMiddle;
            const float leftHigh = left - leftPresenceState;
            const float rightHigh = right - rightPresenceState;
            const uint64_t absoluteFrame = absoluteOutputStartFrame + static_cast<uint64_t>(std::floor(
                static_cast<double>(sourceFrameOffset + frame)
                / std::max(0.0001, static_cast<double>(sourceFramesPerOutputFrame))));
            const float deckEnvelope = nextAutomixEnvelope(absoluteFrame);
            const float lowEnvelope = nextAutomixLowEnvelope(absoluteFrame);
            const float highEnvelope = nextAutomixHighEnvelope(absoluteFrame);
            const float centerEnvelope = nextAutomixCenterEnvelope(absoluteFrame);
            leftDestination[frame] += (leftLowState * lowEnvelope
                + leftSideMiddle * deckEnvelope
                + centerMiddle * centerEnvelope
                + leftHigh * highEnvelope) * deckReplayGain;
            rightDestination[frame] += (rightLowState * lowEnvelope
                + rightSideMiddle * deckEnvelope
                + centerMiddle * centerEnvelope
                + rightHigh * highEnvelope) * deckReplayGain;
        }
        return;
    }

    for (int channel = 0; channel < outputChannels; ++channel)
    {
        float* destination = output.getWritePointer(channel, outputStart);
        const int sourceChannel = std::min(channel, channels - 1);

        float& lowState = automixNextLowState[static_cast<size_t>(sourceChannel)];
        float& presenceState = automixNextPresenceState[static_cast<size_t>(sourceChannel)];
        for (int frame = 0; frame < frameCount; ++frame)
        {
            const float sample = source[frame * channels + sourceChannel];
            const uint64_t absoluteFrame = absoluteOutputStartFrame + static_cast<uint64_t>(std::floor(
                static_cast<double>(sourceFrameOffset + frame)
                / std::max(0.0001, static_cast<double>(sourceFramesPerOutputFrame))));
            const float envelope = nextAutomixEnvelope(absoluteFrame);
            if (shapedHandoff)
            {
                lowState += lowPassCoefficient * (sample - lowState);
                presenceState += presenceLowPassCoefficient * (sample - presenceState);
                const float middle = presenceState - lowState;
                const float high = sample - presenceState;
                const float middleEnvelope = rhythmicBassSwap
                    ? nextAutomixCenterEnvelope(absoluteFrame)
                    : envelope;
                destination[frame] += (lowState * nextAutomixLowEnvelope(absoluteFrame)
                    + middle * middleEnvelope
                    + high * nextAutomixHighEnvelope(absoluteFrame))
                    * deckReplayGain;
            }
            else
            {
                if (workshopWindow.load(std::memory_order_acquire) != nullptr
                    && absoluteFrame >= automixPlan.fadeEndFrame.load(std::memory_order_acquire))
                    destination[frame] = sample;
                else
                    destination[frame] += sample * deckReplayGain * envelope;
            }
        }
    }
}

void PcmRingAudioSource::copyGaplessToOutput(
    int startFrame,
    int frameCount,
    echo::FloatAudioBuffer& output,
    int outputStart)
{
    if (frameCount <= 0)
        return;

    const float* source = automixBuffer.data() + static_cast<size_t>(startFrame * channels);
    const int outputChannels = output.getNumChannels();
    for (int channel = 0; channel < outputChannels; ++channel)
    {
        float* destination = output.getWritePointer(channel, outputStart);
        const int sourceChannel = std::min(channel, channels - 1);
        for (int frame = 0; frame < frameCount; ++frame)
            destination[frame] = source[frame * channels + sourceChannel];
    }
}

uint64_t PcmRingAudioSource::mixGaplessNext(
    echo::FloatAudioBuffer& output,
    int startSample,
    int frameCount,
    uint64_t absoluteStartFrame,
    uint64_t currentFramesRead,
    float sourceFramesPerOutputFrame)
{
    if (! automixPlan.enabled.load(std::memory_order_acquire)
        || ! automixPlan.gapless.load(std::memory_order_acquire)
        || frameCount <= 0)
        return 0;

    const int startOffset = static_cast<int>(std::min<uint64_t>(currentFramesRead, static_cast<uint64_t>(frameCount)));
    int framesNeeded = frameCount - startOffset;
    int outputOffset = startOffset;
    uint64_t framesReadTotal = 0;
    const uint64_t gaplessLease = pendingAutomixDeckLease.load(std::memory_order_acquire);
    if (gaplessLease == 0 || static_cast<int>(gaplessLease & 1u) != 1)
        return 0;
    echo_audio_host::NativeFifo::ReadLease readLease;
    if (! automixFifo.tryAcquireReadLease(readLease))
        return 0;
    if (!automixPlan.enabled.load(std::memory_order_acquire)
        || !automixPlan.gapless.load(std::memory_order_acquire)
        || pendingAutomixDeckLease.load(std::memory_order_acquire) != gaplessLease
        || deckLeases[1].load(std::memory_order_acquire) != gaplessLease)
    {
        automixFifo.releaseReadLease(readLease);
        return 0;
    }

    while (framesNeeded > 0)
    {
        int start1 = 0;
        int size1 = 0;
        int start2 = 0;
        int size2 = 0;
        automixFifo.prepareToRead(framesNeeded, start1, size1, start2, size2);
        const int framesRead = size1 + size2;
        if (framesRead <= 0)
        {
            if (! automixNextInputEnded.load(std::memory_order_acquire)
                && automixNextHasAudio.load(std::memory_order_acquire))
            {
                underrunCallbacks.fetch_add(1, std::memory_order_relaxed);
                underrunFrames.fetch_add(static_cast<uint64_t>(framesNeeded), std::memory_order_relaxed);
            }
            break;
        }

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
        if (readLeaseHookForTests)
            readLeaseHookForTests();
#endif

        const uint64_t boundary = absoluteStartFrame + static_cast<uint64_t>(std::floor(
            static_cast<double>(currentFramesRead)
            / std::max(0.0001, static_cast<double>(sourceFramesPerOutputFrame))));
        uint64_t unsetBoundary = UINT64_MAX;
        gaplessBoundaryFrame.compare_exchange_strong(
            unsetBoundary,
            boundary,
            std::memory_order_acq_rel,
            std::memory_order_acquire);
        copyGaplessToOutput(start1, size1, output, startSample + outputOffset);
        copyGaplessToOutput(start2, size2, output, startSample + outputOffset + size1);
        if (replayGainProcessor != nullptr)
            gaplessReplayGainTimeline.process(*replayGainProcessor, output, startSample + outputOffset, framesRead);
        automixFifo.finishedRead(framesRead);

        framesReadTotal += static_cast<uint64_t>(framesRead);
        framesNeeded -= framesRead;
        outputOffset += framesRead;
    }

    automixFifo.releaseReadLease(readLease);

    return framesReadTotal;
}

uint64_t PcmRingAudioSource::mixAutomixNext(
    echo::FloatAudioBuffer& output,
    int startSample,
    int frameCount,
    uint64_t absoluteStartFrame,
    float sourceFramesPerOutputFrame)
{
    if (! automixPlan.enabled.load(std::memory_order_acquire) || frameCount <= 0)
        return 0;

    const uint64_t nextLease = pendingAutomixDeckLease.load(std::memory_order_acquire);
    if (nextLease == 0)
        return 0;
    const int nextDeckSlot = static_cast<int>(nextLease & 1u);
    auto& nextFifo = fifoForDeck(nextDeckSlot);

    const uint64_t fadeStartFrame = automixPlan.fadeStartFrame.load(std::memory_order_acquire);
    const double safeRate = std::max(0.0001, static_cast<double>(sourceFramesPerOutputFrame));
    const uint64_t outputFrameSpan = static_cast<uint64_t>(std::ceil(
        static_cast<double>(frameCount) / safeRate));
    if (absoluteStartFrame + outputFrameSpan <= fadeStartFrame)
        return 0;

    const int startOffset = absoluteStartFrame >= fadeStartFrame
        ? 0
        : std::min(frameCount, static_cast<int>(std::ceil(
            static_cast<double>(fadeStartFrame - absoluteStartFrame) * safeRate)));
    int framesNeeded = frameCount - startOffset;
    int outputOffset = startOffset;
    uint64_t framesReadTotal = 0;
    echo_audio_host::NativeFifo::ReadLease readLease;
    if (! nextFifo.tryAcquireReadLease(readLease))
        return 0;
    if (!automixPlan.enabled.load(std::memory_order_acquire)
        || pendingAutomixDeckLease.load(std::memory_order_acquire) != nextLease
        || deckLeases[static_cast<size_t>(nextDeckSlot)].load(std::memory_order_acquire) != nextLease)
    {
        nextFifo.releaseReadLease(readLease);
        return 0;
    }

    while (framesNeeded > 0)
    {
        int start1 = 0;
        int size1 = 0;
        int start2 = 0;
        int size2 = 0;
        nextFifo.prepareToRead(framesNeeded, start1, size1, start2, size2);

        const int framesRead = size1 + size2;
        if (framesRead <= 0)
        {
            if (! automixNextInputEnded.load(std::memory_order_acquire) && automixNextHasAudio.load(std::memory_order_acquire))
            {
                underrunCallbacks.fetch_add(1, std::memory_order_relaxed);
                underrunFrames.fetch_add(static_cast<uint64_t>(framesNeeded), std::memory_order_relaxed);
            }
            break;
        }

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
        if (readLeaseHookForTests)
            readLeaseHookForTests();
#endif

        addAutomixToOutput(
            start1,
            size1,
            output,
            startSample + outputOffset,
            absoluteStartFrame,
            outputOffset,
            sourceFramesPerOutputFrame,
            nextDeckSlot);
        addAutomixToOutput(
            start2,
            size2,
            output,
            startSample + outputOffset + size1,
            absoluteStartFrame,
            outputOffset + size1,
            sourceFramesPerOutputFrame,
            nextDeckSlot);
        nextFifo.finishedRead(framesRead);

        framesReadTotal += static_cast<uint64_t>(framesRead);
        framesNeeded -= framesRead;
        outputOffset += framesRead;
    }

    nextFifo.releaseReadLease(readLease);

    return framesReadTotal > 0 ? framesReadTotal + static_cast<uint64_t>(startOffset) : 0;
}

int PcmRingAudioSource::requiredStartupSourceFrames() const noexcept
{
    const auto configuration = rateProcessor != nullptr
        ? rateProcessor->getConfiguration()
        : echo::PlaybackRateProcessor::Configuration {};
    const double rate = configuration.mode == echo::SpeedMode::Speed
        ? 1.0
        : static_cast<double>(configuration.rate);
    return std::max(0, static_cast<int>(std::ceil(
        static_cast<double>(startupPrebufferFrames) * rate)));
}

void PcmRingAudioSource::copyPlanarToInterleaved(const echo::FloatAudioBuffer& source, float* output, int frameCount, int outputChannels) const
{
    if (output == nullptr || frameCount <= 0 || outputChannels <= 0)
        return;

    const int sourceChannels = source.getNumChannels();
    if (sourceChannels <= 0)
        return;

    for (int frame = 0; frame < frameCount; ++frame)
    {
        for (int channel = 0; channel < outputChannels; ++channel)
        {
            const int sourceChannel = std::min(channel, sourceChannels - 1);
            output[static_cast<size_t>(frame) * outputChannels + channel] =
                source.getReadPointer(sourceChannel)[frame];
        }
    }
}

bool PcmRingAudioSource::shouldHoldForStartupPrebuffer()
{
    if (! prebuffering.load(std::memory_order_acquire))
        return false;

    const int currentSlot = currentDeckSlot.load(std::memory_order_acquire);
    const int readyFrames = fifoForDeck(currentSlot).getNumReady();
    const int64_t deadlineNs = prebufferDeadlineNs.load(std::memory_order_acquire);
    const bool enoughPcm = readyFrames >= requiredStartupSourceFrames();
    const bool timedOut = startupPrebufferTimeoutMs <= 0 || steadyNowNs() >= deadlineNs;
    const bool ended = isDeckInputEnded(currentSlot);

    if (enoughPcm || timedOut || ended)
    {
        prebuffering.store(false, std::memory_order_release);
        return false;
    }

    return true;
}
