#include "ListenTogetherRealtimeAudio.h"

#include "ListenTogetherOpusCodec.h"
#include "NativePrimitives.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

extern "C" {
#include <libavutil/channel_layout.h>
#include <libavutil/error.h>
#include <libavutil/mathematics.h>
#include <libavutil/samplefmt.h>
#include <libswresample/swresample.h>
}

namespace echo::listen_together {
namespace {

constexpr int kMaximumSourceSampleRate = 384'000;
constexpr int kPcmRingMilliseconds = 500;
constexpr int kWorkerReadFrames = 4'096;
constexpr int kMaximumConsecutiveConcealmentPackets = 25;

std::string ffmpegError(const char* operation, int code)
{
    std::array<char, AV_ERROR_MAX_STRING_SIZE> buffer{};
    av_strerror(code, buffer.data(), buffer.size());
    return std::string(operation) + ": " + buffer.data();
}

void requireFfmpeg(int result, const char* operation)
{
    if (result < 0)
        throw std::runtime_error(ffmpegError(operation, result));
}

class StereoResampler final
{
public:
    explicit StereoResampler(int sourceSampleRate)
        : sourceSampleRate_(sourceSampleRate)
    {
        if (sourceSampleRate_ < 8'000 || sourceSampleRate_ > kMaximumSourceSampleRate)
            throw std::invalid_argument("listen-together source sample rate is unsupported");

        AVChannelLayout stereo{};
        av_channel_layout_default(&stereo, kCanonicalChannels);
        const int result = swr_alloc_set_opts2(
            &context_,
            &stereo,
            AV_SAMPLE_FMT_FLT,
            static_cast<int>(kCanonicalSampleRate),
            &stereo,
            AV_SAMPLE_FMT_FLT,
            sourceSampleRate_,
            0,
            nullptr);
        av_channel_layout_uninit(&stereo);
        requireFfmpeg(result, "configure listen-together resampler");
        if (context_ == nullptr)
            throw std::bad_alloc();
        requireFfmpeg(swr_init(context_), "initialize listen-together resampler");
    }

    ~StereoResampler() { swr_free(&context_); }

    StereoResampler(const StereoResampler&) = delete;
    StereoResampler& operator=(const StereoResampler&) = delete;

    std::vector<float> convert(const float* interleavedStereo, int inputFrames)
    {
        if (interleavedStereo == nullptr || inputFrames <= 0)
            return {};

        const auto outputCapacity = static_cast<int>(av_rescale_rnd(
            swr_get_delay(context_, sourceSampleRate_) + inputFrames,
            static_cast<int>(kCanonicalSampleRate),
            sourceSampleRate_,
            AV_ROUND_UP));
        std::vector<float> output(static_cast<std::size_t>(std::max(1, outputCapacity)) * kCanonicalChannels);
        const std::uint8_t* input[] = {
            reinterpret_cast<const std::uint8_t*>(interleavedStereo),
        };
        std::uint8_t* destination[] = {
            reinterpret_cast<std::uint8_t*>(output.data()),
        };
        const int converted = swr_convert(context_, destination, outputCapacity, input, inputFrames);
        requireFfmpeg(converted, "resample listen-together PCM");
        output.resize(static_cast<std::size_t>(converted) * kCanonicalChannels);
        return output;
    }

private:
    int sourceSampleRate_ = 0;
    SwrContext* context_ = nullptr;
};

} // namespace

struct ListenTogetherHostAudioStream::Impl {
    struct ActiveConfig {
        bool active = false;
        HostAudioStreamConfig stream;
    };

    explicit Impl(PacketSink sink, MediaClock clock)
        : mediaClock(std::move(clock)), packetSink(std::move(sink)),
          pcmFifo((kMaximumSourceSampleRate * kPcmRingMilliseconds) / 1'000),
          pcmRing(static_cast<std::size_t>(pcmFifo.getTotalSize()) * kCanonicalChannels),
          worker([this](std::stop_token stopToken) { run(stopToken); })
    {
        if (!packetSink)
            throw std::invalid_argument("listen-together host packet sink is required");
    }

    ~Impl()
    {
        enabled.store(false, std::memory_order_release);
        while (activeCallbacks.load(std::memory_order_acquire) != 0)
            std::this_thread::yield();
        worker.request_stop();
        wake.notify_all();
    }

    bool reconfigure(const ActiveConfig& next, std::string& error)
    {
        enabled.store(false, std::memory_order_release);
        while (activeCallbacks.load(std::memory_order_acquire) != 0)
            std::this_thread::yield();

        std::unique_lock<std::mutex> lock(controlMutex);
        workerFailed = false;
        workerError.clear();
        pendingConfig = next;
        activeSourceSampleRate.store(next.active ? next.stream.sourceSampleRate : 0, std::memory_order_release);
        const auto requested = requestedGeneration.fetch_add(1, std::memory_order_acq_rel) + 1;
        wake.notify_all();
        const bool applied = controlApplied.wait_for(lock, std::chrono::seconds(2), [this, requested] {
            return appliedGeneration.load(std::memory_order_acquire) >= requested || workerFailed;
        });
        if (!applied || workerFailed)
        {
            error = workerError.empty() ? "listen-together host worker reconfiguration timed out" : workerError;
            return false;
        }
        mediaHistory.reset();
        anchorStreamFrame = 0;
        mediaAnchor.store(-1.0, std::memory_order_release);
        clockRate.store(next.stream.playbackRate, std::memory_order_release);
        clockSubmittedFrames = 0;
        enabled.store(next.active, std::memory_order_release);
        error.clear();
        return true;
    }

    void run(std::stop_token stopToken) noexcept
    {
        std::uint64_t localGeneration = 0;
        ActiveConfig config;
        std::unique_ptr<StereoResampler> resampler;
        std::unique_ptr<OpusEncoder> encoder;
        std::vector<float> input(static_cast<std::size_t>(kWorkerReadFrames) * kCanonicalChannels);
        std::vector<float> canonical;
        std::size_t canonicalOffset = 0;
        std::uint32_t sequence = 0;

        while (!stopToken.stop_requested())
        {
            const auto requested = requestedGeneration.load(std::memory_order_acquire);
            if (requested != localGeneration)
            {
                try
                {
                    std::lock_guard<std::mutex> lock(controlMutex);
                    config = pendingConfig;
                    pcmFifo.resetWhenReadersQuiescent();
                    canonical.clear();
                    canonicalOffset = 0;
                    sequence = 0;
                    resampler.reset();
                    encoder.reset();
                    if (config.active)
                    {
                        resampler = std::make_unique<StereoResampler>(config.stream.sourceSampleRate);
                        encoder = std::make_unique<OpusEncoder>(OpusCodecConfig{
                            config.stream.bitrate, config.stream.expectedPacketLossPercent,
                        });
                    }
                    localGeneration = requested;
                    appliedGeneration.store(localGeneration, std::memory_order_release);
                    controlApplied.notify_all();
                }
                catch (const std::exception& exception)
                {
                    std::lock_guard<std::mutex> lock(controlMutex);
                    workerFailed = true;
                    workerError = exception.what();
                    appliedGeneration.store(requested, std::memory_order_release);
                    controlApplied.notify_all();
                    config.active = false;
                }
                continue;
            }

            if (!config.active || !resampler || !encoder)
            {
                std::unique_lock<std::mutex> lock(controlMutex);
                wake.wait_for(lock, std::chrono::milliseconds(100), [this, &stopToken, localGeneration] {
                    return stopToken.stop_requested()
                        || requestedGeneration.load(std::memory_order_acquire) != localGeneration;
                });
                continue;
            }

            echo_audio_host::NativeFifo::ReadLease readLease;
            if (!pcmFifo.tryAcquireReadLease(readLease))
                continue;
            const auto block = pcmFifo.prepareToRead(kWorkerReadFrames);
            const int frames = block.size1 + block.size2;
            if (frames <= 0)
            {
                pcmFifo.releaseReadLease(readLease);
                std::unique_lock<std::mutex> lock(controlMutex);
                wake.wait_for(lock, std::chrono::milliseconds(10), [this, &stopToken, localGeneration] {
                    return stopToken.stop_requested()
                        || requestedGeneration.load(std::memory_order_acquire) != localGeneration
                        || pcmFifo.getNumReady() > 0;
                });
                continue;
            }

            const auto copyBlock = [this, &input](int sourceStart, int sourceFrames, int destinationStart) {
                std::copy_n(
                    pcmRing.data() + static_cast<std::size_t>(sourceStart) * kCanonicalChannels,
                    static_cast<std::size_t>(sourceFrames) * kCanonicalChannels,
                    input.data() + static_cast<std::size_t>(destinationStart) * kCanonicalChannels);
            };
            copyBlock(block.start1, block.size1, 0);
            copyBlock(block.start2, block.size2, block.size1);
            pcmFifo.releaseReadLease(readLease, frames);

            try
            {
                auto converted = resampler->convert(input.data(), frames);
                canonical.insert(canonical.end(), converted.begin(), converted.end());
                const std::size_t samplesPerPacket = kFramesPerPacket * kCanonicalChannels;
                while (canonical.size() - canonicalOffset >= samplesPerPacket)
                {
                    const float* pcm = canonical.data() + canonicalOffset;
                    const auto packetSequence = sequence++;
                    // Consume exactly once even if an encoder or output sink
                    // fails. Retrying this block repeats stale audio and
                    // lets unconsumed canonical PCM grow without a bound.
                    canonicalOffset += samplesPerPacket;
                    try {
                        AudioPacket packet;
                        packet.streamEpoch = config.stream.streamEpoch;
                        packet.sequence = packetSequence;
                        packet.timestampFrames = static_cast<std::uint64_t>(packetSequence) * kFramesPerPacket;
                        packet.flags = packetSequence == 0 ? AudioPacketFlag::StreamStart : AudioPacketFlag::None;
                        packet.opusPayload = encoder->encode(pcm, static_cast<int>(kFramesPerPacket));
                        if (packetSink(encodeAudioPacket(packet)))
                            encodedPackets.fetch_add(1, std::memory_order_relaxed);
                        else
                            packetSinkFailures.fetch_add(1, std::memory_order_relaxed);
                    } catch (...) {
                        packetSinkFailures.fetch_add(1, std::memory_order_relaxed);
                    }
                }
                if (canonicalOffset > 0 && canonicalOffset * 2 >= canonical.size())
                {
                    canonical.erase(canonical.begin(), canonical.begin() + static_cast<std::ptrdiff_t>(canonicalOffset));
                    canonicalOffset = 0;
                }
            }
            catch (...)
            {
                packetSinkFailures.fetch_add(1, std::memory_order_relaxed);
            }
        }
    }

    ListeningMediaClock mediaHistory;
    double anchorStreamFrame = 0;
    MediaClock mediaClock;
    std::atomic<double> mediaAnchor { -1.0 }, clockRate { 1.0 };
    std::uint64_t clockSubmittedFrames = 0; // single device callback owner
    PacketSink packetSink;
    echo_audio_host::NativeFifo pcmFifo;
    std::vector<float> pcmRing;
    std::atomic<bool> enabled { false };
    std::atomic<std::uint32_t> activeCallbacks { 0 };
    std::atomic<int> activeSourceSampleRate { 0 };
    std::atomic<std::uint64_t> requestedGeneration { 0 };
    std::atomic<std::uint64_t> appliedGeneration { 0 };
    std::mutex controlMutex;
    std::condition_variable wake;
    std::condition_variable controlApplied;
    ActiveConfig pendingConfig;
    bool workerFailed = false;
    std::string workerError;
    std::atomic<std::uint64_t> encodedPackets { 0 };
    std::atomic<std::uint64_t> droppedInputFrames { 0 };
    std::atomic<std::uint64_t> packetSinkFailures { 0 };
    std::jthread worker;
};

ListenTogetherHostAudioStream::ListenTogetherHostAudioStream(PacketSink packetSink, MediaClock clock)
    : impl_(std::make_unique<Impl>(std::move(packetSink), std::move(clock)))
{
}

ListenTogetherHostAudioStream::~ListenTogetherHostAudioStream() = default;

bool ListenTogetherHostAudioStream::start(const HostAudioStreamConfig& config, std::string& error)
{
    if (config.bitrate != 256'000) {
        error = "listen-together requires fixed 256 kbps audio";
        return false;
    }
    if (config.streamEpoch == 0)
    {
        error = "listen-together stream epoch must be non-zero";
        return false;
    }
    if (config.sourceSampleRate < 8'000 || config.sourceSampleRate > kMaximumSourceSampleRate)
    {
        error = "listen-together source sample rate is unsupported";
        return false;
    }
    return impl_->reconfigure({true, config}, error);
}

void ListenTogetherHostAudioStream::stop() noexcept
{
    std::string ignored;
    impl_->reconfigure({}, ignored);
}

HostAudioStreamStats ListenTogetherHostAudioStream::stats() const noexcept
{
    return {
        impl_->enabled.load(std::memory_order_acquire),
        impl_->encodedPackets.load(std::memory_order_relaxed),
        impl_->droppedInputFrames.load(std::memory_order_relaxed),
        impl_->packetSinkFailures.load(std::memory_order_relaxed),
        impl_->mediaHistory.snapshot(),
        impl_->mediaAnchor.load(std::memory_order_acquire),
        impl_->clockRate.load(std::memory_order_acquire),
    };
}

// Media anchors follow the actual programme tap; rate changes never restart audio.
void ListenTogetherHostAudioStream::onProgramClock(
    std::uint64_t source, std::uint64_t output, int rate, bool complete, double mediaRate) noexcept
{
    if (!impl_->enabled.load(std::memory_order_acquire)) return;
    impl_->activeCallbacks.fetch_add(1, std::memory_order_acq_rel);
    if (impl_->enabled.load(std::memory_order_acquire) && impl_->mediaClock && rate > 0) {
        const double position = impl_->mediaClock(source, output, rate);
        const double streamFrame = static_cast<double>(impl_->clockSubmittedFrames) * kCanonicalSampleRate / rate;
        const double anchor = impl_->mediaAnchor.load(std::memory_order_acquire);
        const double previousRate = impl_->clockRate.load(std::memory_order_acquire);
        const bool valid = complete && position >= 0 && std::isfinite(position)
            && std::isfinite(mediaRate) && mediaRate >= 0.1 && mediaRate <= 8.0;
        if (!valid) {
            if (anchor != -2.0) impl_->mediaHistory.append({ streamFrame, -1.0, 1.0 });
            impl_->mediaAnchor.store(-2.0, std::memory_order_release);
        } else if (anchor < 0 || std::abs(mediaRate - previousRate) > 0.0001
            || std::abs(position - anchor - (streamFrame - impl_->anchorStreamFrame) / kCanonicalSampleRate * previousRate) > 0.050) {
            impl_->mediaHistory.append({ streamFrame, position, mediaRate });
            impl_->anchorStreamFrame = streamFrame;
            impl_->clockRate.store(mediaRate, std::memory_order_release);
            impl_->mediaAnchor.store(position, std::memory_order_release);
        }
    }
    impl_->activeCallbacks.fetch_sub(1, std::memory_order_acq_rel);
}

void ListenTogetherHostAudioStream::onProgramPcm(
    const echo::FloatAudioBuffer& audio,
    int startFrame,
    int frameCount,
    int sampleRate) noexcept
{
    if (!impl_->enabled.load(std::memory_order_acquire) || frameCount <= 0 || startFrame < 0)
        return;

    impl_->activeCallbacks.fetch_add(1, std::memory_order_acq_rel);
    if (!impl_->enabled.load(std::memory_order_acquire))
    {
        impl_->activeCallbacks.fetch_sub(1, std::memory_order_acq_rel);
        return;
    }

    if (sampleRate != impl_->activeSourceSampleRate.load(std::memory_order_acquire) || audio.getNumChannels() <= 0
        || startFrame + frameCount > audio.getNumSamples())
    {
        impl_->droppedInputFrames.fetch_add(static_cast<std::uint64_t>(frameCount), std::memory_order_relaxed);
        impl_->activeCallbacks.fetch_sub(1, std::memory_order_acq_rel);
        return;
    }

    const auto block = impl_->pcmFifo.prepareToWrite(frameCount);
    const int writable = block.size1 + block.size2;
    const auto writeBlock = [this, &audio, startFrame](int destinationStart, int frames, int sourceOffset) {
        const int channels = audio.getNumChannels();
        float* destination = impl_->pcmRing.data()
            + static_cast<std::size_t>(destinationStart) * kCanonicalChannels;
        const float* left = audio.getReadPointer(0, startFrame + sourceOffset);
        const float* right = audio.getReadPointer(std::min(1, channels - 1), startFrame + sourceOffset);
        for (int frame = 0; frame < frames; ++frame)
        {
            destination[static_cast<std::size_t>(frame) * 2] = left[frame];
            destination[static_cast<std::size_t>(frame) * 2 + 1] = right[frame];
        }
    };
    writeBlock(block.start1, block.size1, 0);
    writeBlock(block.start2, block.size2, block.size1);
    impl_->clockSubmittedFrames += static_cast<std::uint64_t>(writable);
    if (writable != frameCount) impl_->mediaAnchor.store(-2.0, std::memory_order_release);
    impl_->pcmFifo.finishedWrite(writable);
    if (writable < frameCount)
        impl_->droppedInputFrames.fetch_add(static_cast<std::uint64_t>(frameCount - writable), std::memory_order_relaxed);
    impl_->activeCallbacks.fetch_sub(1, std::memory_order_acq_rel);
    impl_->wake.notify_one();
}

struct ListenTogetherGuestAudioStream::Impl {
    explicit Impl(PcmSink sink)
        : pcmSink(std::move(sink)),
          worker([this](std::stop_token stopToken) { run(stopToken); })
    {
        if (!pcmSink)
            throw std::invalid_argument("listen-together guest PCM sink is required");
    }

    ~Impl()
    {
        running.store(false, std::memory_order_release);
        worker.request_stop();
        wake.notify_all();
        if (worker.joinable()) worker.join();
    }

    void run(std::stop_token stopToken) noexcept
    {
        std::unique_ptr<OpusDecoder> decoder;
        std::uint64_t localGeneration = 0;
        std::vector<float> silence(kFramesPerPacket * kCanonicalChannels, 0.0f);
        auto nextDeadline = std::chrono::steady_clock::now();
        int consecutiveMissing = 0;
        bool playoutStarted = false;

        while (!stopToken.stop_requested())
        {
            const auto requested = requestedGeneration.load(std::memory_order_acquire);
            if (requested != localGeneration)
            {
                try
                {
                    decoder = running.load(std::memory_order_acquire) ? std::make_unique<OpusDecoder>() : nullptr;
                }
                catch (...)
                {
                    running.store(false, std::memory_order_release);
                    decoder.reset();
                }
                {
                    std::lock_guard<std::mutex> lock(jitterMutex);
                    jitter.reset();
                }
                playoutClock.reset();
                consecutiveMissing = 0;
                playoutStarted = false;
                nextDeadline = std::chrono::steady_clock::now();
                localGeneration = requested;
                appliedGeneration.store(localGeneration, std::memory_order_release);
                controlApplied.notify_all();
                continue;
            }

            if (!running.load(std::memory_order_acquire) || !decoder)
            {
                std::unique_lock<std::mutex> lock(waitMutex);
                wake.wait_for(lock, std::chrono::milliseconds(100));
                continue;
            }

            JitterBuffer::PopResult next;
            {
                std::lock_guard<std::mutex> lock(jitterMutex);
                next = jitter.pop(playoutStarted);
            }
            if (next.kind == JitterBuffer::PopKind::Waiting)
            {
                std::unique_lock<std::mutex> lock(waitMutex);
                wake.wait_for(lock, std::chrono::milliseconds(20));
                continue;
            }

            nextDeadline = std::max(nextDeadline + std::chrono::milliseconds(kFrameDurationMs),
                std::chrono::steady_clock::now());
            const float* samples = silence.data();
            int frames = static_cast<int>(kFramesPerPacket);
            std::vector<float> decoded;
            if (next.kind == JitterBuffer::PopKind::Packet && next.packet)
            {
                try
                {
                    decoded = decoder->decode(
                        next.packet->opusPayload.data(),
                        static_cast<int>(next.packet->opusPayload.size()));
                    if (decoded.size() == kFramesPerPacket * kCanonicalChannels)
                    {
                        samples = decoded.data();
                        consecutiveMissing = 0;
                        playoutStarted = true;
                        decodedPackets.fetch_add(1, std::memory_order_relaxed);
                    }
                    else
                    {
                        ++consecutiveMissing;
                        concealedPackets.fetch_add(1, std::memory_order_relaxed);
                    }
                }
                catch (...)
                {
                    ++consecutiveMissing;
                    concealedPackets.fetch_add(1, std::memory_order_relaxed);
                }
            }
            else
            {
                ++consecutiveMissing;
                playoutStarted = true;
                concealedPackets.fetch_add(1, std::memory_order_relaxed);
            }

            if (!pcmSink(samples, frames))
                sinkFailures.fetch_add(1, std::memory_order_relaxed);
            else
                playoutClock.append(next.packet ? next.packet->timestampFrames : 0,
                    static_cast<std::uint64_t>(frames), next.packet.has_value() && consecutiveMissing == 0);

            if (consecutiveMissing >= kMaximumConsecutiveConcealmentPackets)
            {
                consecutiveMissing = 0;
                playoutStarted = false;
                {
                    std::lock_guard<std::mutex> lock(jitterMutex);
                    jitter.rebuffer();
                }
                // The normal Waiting branch retries within 20 ms. An unbounded
                // wait here can miss a packet/stop notification sent just before
                // waiting, leaving an otherwise live stream permanently asleep.
                nextDeadline = std::chrono::steady_clock::now();
            }
            else
            {
                std::this_thread::sleep_until(nextDeadline);
            }
        }
    }

    ListeningPlayoutClock playoutClock;
    PcmSink pcmSink;
    std::atomic<bool> running { false };
    std::atomic<std::uint64_t> expectedEpoch { 0 };
    std::atomic<std::uint64_t> requestedGeneration { 0 };
    std::atomic<std::uint64_t> appliedGeneration { 0 };
    std::mutex jitterMutex;
    // Public TLS relay paths can deliver several 20ms frames in one burst.
    // Keep 400ms of runway; cap encoded audio at 1s (at most ~64KiB).
    JitterBuffer jitter { 20, 50 };
    std::mutex waitMutex;
    std::condition_variable wake;
    std::mutex controlMutex;
    std::condition_variable controlApplied;
    std::atomic<std::uint64_t> decodedPackets { 0 };
    std::atomic<std::uint64_t> rejectedPackets { 0 };
    std::atomic<std::uint64_t> concealedPackets { 0 };
    std::atomic<std::uint64_t> sinkFailures { 0 };
    std::jthread worker;
};

ListenTogetherGuestAudioStream::ListenTogetherGuestAudioStream(PcmSink pcmSink)
    : impl_(std::make_unique<Impl>(std::move(pcmSink)))
{
}

ListenTogetherGuestAudioStream::~ListenTogetherGuestAudioStream() = default;

bool ListenTogetherGuestAudioStream::start(std::uint64_t streamEpoch, std::string& error)
{
    if (streamEpoch == 0)
    {
        error = "listen-together guest epoch must be non-zero";
        return false;
    }
    std::unique_lock<std::mutex> lock(impl_->controlMutex);
    impl_->expectedEpoch.store(streamEpoch, std::memory_order_release);
    impl_->running.store(true, std::memory_order_release);
    const auto requested = impl_->requestedGeneration.fetch_add(1, std::memory_order_acq_rel) + 1;
    impl_->wake.notify_all();
    const bool applied = impl_->controlApplied.wait_for(lock, std::chrono::seconds(2), [this, requested] {
        return impl_->appliedGeneration.load(std::memory_order_acquire) >= requested;
    });
    if (!applied || !impl_->running.load(std::memory_order_acquire))
    {
        error = applied ? "listen-together Opus decoder is unavailable" : "listen-together guest worker timed out";
        return false;
    }
    error.clear();
    return true;
}

void ListenTogetherGuestAudioStream::stop() noexcept
{
    std::unique_lock<std::mutex> lock(impl_->controlMutex);
    impl_->running.store(false, std::memory_order_release);
    impl_->expectedEpoch.store(0, std::memory_order_release);
    const auto requested = impl_->requestedGeneration.fetch_add(1, std::memory_order_acq_rel) + 1;
    impl_->wake.notify_all();
    impl_->controlApplied.wait_for(lock, std::chrono::seconds(2), [this, requested] {
        return impl_->appliedGeneration.load(std::memory_order_acquire) >= requested;
    });
}

bool ListenTogetherGuestAudioStream::pushPacket(const std::uint8_t* data, std::size_t size) noexcept
{
    if (!impl_->running.load(std::memory_order_acquire))
        return false;
    const auto packet = decodeAudioPacket(data, size);
    if (!packet || packet->streamEpoch != impl_->expectedEpoch.load(std::memory_order_acquire))
    {
        impl_->rejectedPackets.fetch_add(1, std::memory_order_relaxed);
        return false;
    }
    JitterBuffer::PushResult result;
    {
        std::lock_guard<std::mutex> lock(impl_->jitterMutex);
        result = impl_->jitter.push(*packet);
    }
    if (result == JitterBuffer::PushResult::Invalid
        || result == JitterBuffer::PushResult::TooLate
        || result == JitterBuffer::PushResult::Duplicate)
    {
        impl_->rejectedPackets.fetch_add(1, std::memory_order_relaxed);
        return false;
    }
    impl_->wake.notify_one();
    return true;
}

std::optional<double> ListenTogetherGuestAudioStream::streamFrameAt(std::uint64_t frames) const
{
    return impl_->playoutClock.resolve(frames);
}

GuestAudioStreamStats ListenTogetherGuestAudioStream::stats() const noexcept
{
    return {
        impl_->running.load(std::memory_order_acquire),
        impl_->decodedPackets.load(std::memory_order_relaxed),
        impl_->rejectedPackets.load(std::memory_order_relaxed),
        impl_->concealedPackets.load(std::memory_order_relaxed),
        impl_->sinkFailures.load(std::memory_order_relaxed),
    };
}

} // namespace echo::listen_together
