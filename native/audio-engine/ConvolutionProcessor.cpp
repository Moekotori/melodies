#include "ConvolutionProcessor.h"
#include "DspSafetyLimiter.h"

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/avutil.h>
#include <libavutil/channel_layout.h>
#include <libavutil/frame.h>
#include <libavutil/samplefmt.h>
#include <libswresample/swresample.h>
}

#include <algorithm>
#include <array>
#include <cmath>
#include <filesystem>
#include <memory>
#include <sys/stat.h>
#include <vector>

namespace echo
{
namespace
{

bool isRegularFile(const struct stat& fileStats)
{
#ifdef _WIN32
    return (fileStats.st_mode & _S_IFMT) == _S_IFREG;
#else
    return S_ISREG(fileStats.st_mode);
#endif
}

double dbToGain(double db)
{
    return std::pow(10.0, db / 20.0);
}

bool isFiniteBuffer(const FloatAudioBuffer& buffer)
{
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
        for (int sample = 0; sample < buffer.getNumSamples(); ++sample)
            if (! std::isfinite(buffer.getSample(channel, sample)))
                return false;

    return true;
}

struct DecodedImpulse
{
    std::vector<std::vector<double>> samples;
    double sampleRate = 0.0;
    std::string error;
};

bool isSupportedImpulseSampleFormat(AVSampleFormat format)
{
    switch (format)
    {
        case AV_SAMPLE_FMT_U8:
        case AV_SAMPLE_FMT_U8P:
        case AV_SAMPLE_FMT_S16:
        case AV_SAMPLE_FMT_S16P:
        case AV_SAMPLE_FMT_S32:
        case AV_SAMPLE_FMT_S32P:
        case AV_SAMPLE_FMT_FLT:
        case AV_SAMPLE_FMT_FLTP:
        case AV_SAMPLE_FMT_DBL:
        case AV_SAMPLE_FMT_DBLP:
            return true;
        default:
            return false;
    }
}

struct ScopedChannelLayout
{
    AVChannelLayout value {};
    ~ScopedChannelLayout() { av_channel_layout_uninit(&value); }
};

using SwrContextPtr = std::unique_ptr<SwrContext, void (*)(SwrContext*)>;

SwrContextPtr createImpulseConverter(
    const AVChannelLayout& inputLayout,
    AVSampleFormat inputFormat,
    int sampleRate,
    const AVChannelLayout& outputLayout)
{
    const auto deleter = [](SwrContext* context) {
        if (context != nullptr)
            swr_free(&context);
    };
    SwrContext* raw = nullptr;
    if (swr_alloc_set_opts2(
            &raw,
            &outputLayout,
            AV_SAMPLE_FMT_DBLP,
            sampleRate,
            &inputLayout,
            inputFormat,
            sampleRate,
            0,
            nullptr) < 0
        || raw == nullptr)
    {
        deleter(raw);
        return { nullptr, deleter };
    }

    SwrContextPtr converter(raw, deleter);
    if (inputLayout.nb_channels != outputLayout.nb_channels)
    {
        const int stride = inputLayout.nb_channels;
        std::vector<double> matrix(
            static_cast<size_t>(outputLayout.nb_channels * inputLayout.nb_channels),
            0.0);
        // Explicit stereo downmix: L/R stay discrete, centre and surrounds are
        // mixed at -3 dB, and LFE is intentionally omitted.
        if (swr_build_matrix2(
                &inputLayout,
                &outputLayout,
                std::sqrt(0.5),
                std::sqrt(0.5),
                0.0,
                1.0,
                1.0,
                matrix.data(),
                stride,
                AV_MATRIX_ENCODING_NONE,
                nullptr) < 0
            || swr_set_matrix(converter.get(), matrix.data(), stride) < 0)
            return { nullptr, deleter };
    }

    if (swr_init(converter.get()) < 0)
        return { nullptr, deleter };
    return converter;
}

DecodedImpulse decodeAudioFile(const std::string& path, double targetSampleRate)
{
    const auto failed = [](std::string error) {
        DecodedImpulse result;
        result.error = std::move(error);
        return result;
    };

    struct stat st {};
    if (stat(path.c_str(), &st) != 0 || !isRegularFile(st))
        return failed("missing_file");

    AVFormatContext* fmtCtx = nullptr;
    AVDictionary* inputOptions = nullptr;
    av_dict_set(&inputOptions, "protocol_whitelist", "file,pipe", 0);
    const int openResult = avformat_open_input(&fmtCtx, path.c_str(), nullptr, &inputOptions);
    av_dict_free(&inputOptions);
    if (openResult < 0)
        return failed("invalid_audio");
    auto fmtDeleter = [](AVFormatContext* p) { if (p) avformat_close_input(&p); };
    std::unique_ptr<AVFormatContext, decltype(fmtDeleter)> ctx(fmtCtx, fmtDeleter);

    if (avformat_find_stream_info(ctx.get(), nullptr) < 0)
        return failed("invalid_audio");

    // Find the first audio stream
    int streamIdx = -1;
    for (unsigned i = 0; i < ctx->nb_streams; ++i)
    {
        if (ctx->streams[i] && ctx->streams[i]->codecpar
            && ctx->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO)
        {
            streamIdx = static_cast<int>(i);
            break;
        }
    }
    if (streamIdx < 0)
        return failed("invalid_audio");

    AVStream* stream = ctx->streams[streamIdx];
    const AVCodec* codec = avcodec_find_decoder(stream->codecpar->codec_id);
    if (codec == nullptr)
        return failed("unsupported_codec");

    AVCodecContext* codecCtx = avcodec_alloc_context3(codec);
    if (codecCtx == nullptr)
        return failed("decoder_allocation_failed");
    auto codecDeleter = [](AVCodecContext* p) { if (p) avcodec_free_context(&p); };
    std::unique_ptr<AVCodecContext, decltype(codecDeleter)> cctx(codecCtx, codecDeleter);

    if (avcodec_parameters_to_context(cctx.get(), stream->codecpar) < 0)
        return failed("invalid_codec_parameters");

    if (avcodec_open2(cctx.get(), codec, nullptr) < 0)
        return failed("decoder_open_failed");

    if (cctx->ch_layout.nb_channels <= 0)
        return failed("missing_codec_channels");
    if (cctx->sample_rate <= 0)
        return failed("missing_sample_rate");

    const int outputChannels = cctx->ch_layout.nb_channels;
    if (outputChannels > convolutionMaxChannels) return failed("convolution_channel_limit");
    std::vector<std::vector<double>> channelBuffers(static_cast<size_t>(outputChannels));
    const double safeTargetRate = targetSampleRate > 0.0
        ? targetSampleRate
        : static_cast<double>(cctx->sample_rate);
    const int64_t maxSourceSamples = std::min<int64_t>(convolutionMaxCoefficients / outputChannels, std::max<int64_t>(
        1,
        static_cast<int64_t>(std::floor(
            static_cast<double>(roomCorrectionMaxTaps) * cctx->sample_rate / safeTargetRate))));
    for (auto& buffer : channelBuffers)
        buffer.reserve(static_cast<size_t>(std::min<int64_t>(maxSourceSamples, roomCorrectionMaxTaps)));

    AVPacket* pkt = av_packet_alloc();
    AVFrame* frame = av_frame_alloc();
    if (pkt == nullptr || frame == nullptr)
    {
        av_packet_free(&pkt);
        av_frame_free(&frame);
        return failed("decoder_allocation_failed");
    }

    ScopedChannelLayout configuredInputLayout;
    ScopedChannelLayout outputLayout;
    av_channel_layout_copy(&outputLayout.value, &cctx->ch_layout);
    if (! av_channel_layout_check(&outputLayout.value))
    {
        av_packet_free(&pkt);
        av_frame_free(&frame);
        return failed("invalid_output_channel_layout");
    }

    AVSampleFormat configuredInputFormat = AV_SAMPLE_FMT_NONE;
    const auto swrDeleter = [](SwrContext* context) {
        if (context != nullptr)
            swr_free(&context);
    };
    SwrContextPtr converter(nullptr, swrDeleter);
    std::string decodeError;
    bool decoderFinished = false;

    const auto receiveFrames = [&]() {
        while (!decoderFinished && decodeError.empty())
        {
            const int receiveResult = avcodec_receive_frame(cctx.get(), frame);
            if (receiveResult == AVERROR(EAGAIN))
                return;
            if (receiveResult == AVERROR_EOF)
            {
                decoderFinished = true;
                return;
            }
            if (receiveResult < 0)
            {
                decodeError = "decoder_receive_failed";
                return;
            }

            const auto frameFormat = static_cast<AVSampleFormat>(frame->format);
            if (frame->nb_samples <= 0)
                decodeError = "invalid_frame_sample_count";
            else if (static_cast<int64_t>(channelBuffers.front().size()) + frame->nb_samples > maxSourceSamples)
                decodeError = "impulse_too_long";
            else if (! av_channel_layout_check(&frame->ch_layout)
                || frame->ch_layout.nb_channels != outputChannels)
                decodeError = "missing_frame_channel_layout";
            else if (! isSupportedImpulseSampleFormat(frameFormat))
                decodeError = "unsupported_sample_format";
            else if (frame->extended_data == nullptr)
                decodeError = "missing_frame_data";

            if (! decodeError.empty())
            {
                av_frame_unref(frame);
                return;
            }

            if (converter == nullptr)
            {
                if (av_channel_layout_copy(&configuredInputLayout.value, &frame->ch_layout) < 0)
                    decodeError = "channel_layout_copy_failed";
                else
                {
                    configuredInputFormat = frameFormat;
                    converter = createImpulseConverter(
                        configuredInputLayout.value,
                        configuredInputFormat,
                        cctx->sample_rate,
                        outputLayout.value);
                    if (converter == nullptr)
                        decodeError = "swr_initialization_failed";
                }
            }
            else if (configuredInputFormat != frameFormat
                || av_channel_layout_compare(&configuredInputLayout.value, &frame->ch_layout) != 0)
                decodeError = "changing_frame_format";

            if (! decodeError.empty())
            {
                av_frame_unref(frame);
                return;
            }

            const int outputCapacity = swr_get_out_samples(converter.get(), frame->nb_samples);
            if (outputCapacity <= 0 || outputCapacity > frame->nb_samples + 32)
            {
                decodeError = "invalid_swr_output_capacity";
                av_frame_unref(frame);
                return;
            }

            std::vector<std::vector<double>> converted(
                static_cast<size_t>(outputChannels),
                std::vector<double>(static_cast<size_t>(outputCapacity), 0.0f));
            std::array<uint8_t*, convolutionMaxChannels> outputData {};
            for (int channel = 0; channel < outputChannels; ++channel)
                outputData[static_cast<size_t>(channel)] = reinterpret_cast<uint8_t*>(
                    converted[static_cast<size_t>(channel)].data());

            auto* inputData = const_cast<const uint8_t**>(frame->extended_data);
            const int convertedFrames = swr_convert(
                converter.get(),
                outputData.data(),
                outputCapacity,
                inputData,
                frame->nb_samples);
            if (convertedFrames != frame->nb_samples || convertedFrames > outputCapacity)
                decodeError = "invalid_swr_output_count";
            else if (static_cast<int64_t>(channelBuffers.front().size()) + convertedFrames > maxSourceSamples)
                decodeError = "impulse_too_long";
            else
            {
                for (int channel = 0; channel < outputChannels && decodeError.empty(); ++channel)
                {
                    const auto begin = converted[static_cast<size_t>(channel)].begin();
                    const auto end = begin + convertedFrames;
                    if (std::any_of(begin, end, [](double sample) { return ! std::isfinite(sample); }))
                        decodeError = "non_finite_sample";
                    else
                        channelBuffers[static_cast<size_t>(channel)].insert(
                            channelBuffers[static_cast<size_t>(channel)].end(),
                            begin,
                            end);
                }
            }

            av_frame_unref(frame);
        }
    };

    while (!decoderFinished && decodeError.empty())
    {
        const int readResult = av_read_frame(ctx.get(), pkt);
        if (readResult == AVERROR_EOF)
        {
            const int sendResult = avcodec_send_packet(cctx.get(), nullptr);
            if (sendResult < 0 && sendResult != AVERROR_EOF)
                decodeError = "decoder_drain_failed";
            receiveFrames();
            if (!decoderFinished && decodeError.empty())
                decodeError = "decoder_drain_incomplete";
            break;
        }
        if (readResult < 0)
        {
            decodeError = "demux_read_failed";
            break;
        }
        if (pkt->stream_index != streamIdx)
        {
            av_packet_unref(pkt);
            continue;
        }

        const int sendResult = avcodec_send_packet(cctx.get(), pkt);
        av_packet_unref(pkt);
        if (sendResult < 0)
        {
            decodeError = "decoder_send_failed";
            break;
        }
        receiveFrames();
    }

    av_packet_free(&pkt);
    av_frame_free(&frame);

    if (! decodeError.empty())
        return failed(decodeError);
    if (channelBuffers.empty() || channelBuffers.front().empty())
        return failed("empty_decoded_audio");

    return { std::move(channelBuffers), static_cast<double>(cctx->sample_rate), {} };
}

std::string normalizedIrPath(const std::string& path)
{
    std::error_code error;
    const auto canonical = std::filesystem::weakly_canonical(std::filesystem::path(path), error);
    return (error ? std::filesystem::path(path).lexically_normal() : canonical).generic_string();
}

} // namespace

float clampRoomCorrectionTrimDb(float value)
{
    if (! std::isfinite(value))
        return 0.0f;

    return std::max(roomCorrectionMinTrimDb, std::min(roomCorrectionMaxTrimDb, value));
}

ConvolutionProcessor::ConvolutionProcessor() = default;

ConvolutionProcessor::GraphLease::GraphLease(
    const ConvolutionProcessor* ownerToUse,
    GraphSlot* slotToUse,
    uint32_t readerEpochToUse) noexcept
    : owner(ownerToUse), slot(slotToUse), readerEpoch(readerEpochToUse)
{
}

ConvolutionProcessor::GraphLease::GraphLease(GraphLease&& other) noexcept
    : owner(other.owner), slot(other.slot), readerEpoch(other.readerEpoch)
{
    other.owner = nullptr;
    other.slot = nullptr;
    other.readerEpoch = 0;
}

ConvolutionProcessor::GraphLease& ConvolutionProcessor::GraphLease::operator=(GraphLease&& other) noexcept
{
    if (this != &other)
    {
        release();
        owner = other.owner;
        slot = other.slot;
        readerEpoch = other.readerEpoch;
        other.owner = nullptr;
        other.slot = nullptr;
        other.readerEpoch = 0;
    }
    return *this;
}

ConvolutionProcessor::GraphLease::~GraphLease()
{
    release();
}

void ConvolutionProcessor::GraphLease::release() noexcept
{
    if (slot == nullptr)
        return;
    auto& readers = owner->graphReaders[readerEpoch & 1u];
    readers.fetch_sub(1, std::memory_order_release);
    readers.notify_all();
    owner = nullptr;
    slot = nullptr;
    readerEpoch = 0;
}

ConvolutionProcessor::GraphLease ConvolutionProcessor::acquireGraph() const noexcept
{
    // A publisher stores the new graph before flipping activeReaderEpoch. A
    // reader pins the observed epoch, verifies it is still current, and only
    // then dereferences activeGraph. The control thread can therefore reclaim
    // every graph retired before the flip after that epoch reaches zero.
    for (int attempt = 0; attempt < 3; ++attempt)
    {
        const uint32_t epoch = activeReaderEpoch.load(std::memory_order_acquire) & 1u;
        auto& readers = graphReaders[epoch];
        readers.fetch_add(1, std::memory_order_acq_rel);
        if ((activeReaderEpoch.load(std::memory_order_acquire) & 1u) == epoch)
        {
            GraphSlot* slot = activeGraph.load(std::memory_order_acquire);
            if (slot != nullptr)
                return GraphLease(this, slot, epoch);
            readers.fetch_sub(1, std::memory_order_release);
            readers.notify_all();
            return {};
        }
        readers.fetch_sub(1, std::memory_order_release);
        readers.notify_all();
    }
    return {};
}

void ConvolutionProcessor::reclaimRetiredGraphsLocked()
{
    const uint32_t retiredEpoch = activeReaderEpoch.fetch_xor(1u, std::memory_order_acq_rel) & 1u;
    auto& readers = graphReaders[retiredEpoch];
    uint32_t readerCount = readers.load(std::memory_order_acquire);
    while (readerCount != 0)
    {
        readers.wait(readerCount, std::memory_order_acquire);
        readerCount = readers.load(std::memory_order_acquire);
    }

    GraphSlot* active = activeGraph.load(std::memory_order_acquire);
    graphArchive.erase(
        std::remove_if(
            graphArchive.begin(),
            graphArchive.end(),
            [active](const std::unique_ptr<GraphSlot>& slot) {
                return slot.get() != active;
            }),
        graphArchive.end());
}

bool ConvolutionProcessor::publishPrepared(std::unique_ptr<PreparedImpulse> impulse)
{
    if (impulse == nullptr)
        return false;

    auto graph = std::make_unique<GraphSlot>(std::move(*impulse), preparedChannels);
    if (!graph->engine.isPrepared())
        return false;

    std::lock_guard<std::mutex> lock(graphMutex);
    GraphSlot* published = graph.get();
    graphArchive.push_back(std::move(graph));
    activeGraph.store(published, std::memory_order_release);
    configurationRevision.fetch_add(1, std::memory_order_acq_rel);
    reclaimRetiredGraphsLocked();
    return true;
}

void ConvolutionProcessor::prepare(double sampleRate, int maximumBlockSize, int channelCount)
{
    std::lock_guard configurationLock(configurationMutex);
    currentSampleRate = sampleRate > 0.0 ? sampleRate : 44100.0;
    preparedChannels = std::max(1, channelCount);
    preparedBlockSize = std::max(1, maximumBlockSize);
    if (filterBank)
    {
        if (!loadSelectedBankFilter(false))
        {
            selectedFilter = nullptr;
            std::lock_guard<std::mutex> lock(graphMutex);
            activeGraph.store(nullptr, std::memory_order_release);
            reclaimRetiredGraphsLocked();
        }
        clippingRisk.store(false, std::memory_order_release);
        return;
    }
    auto current = acquireGraph();
    if (current && !current->impulse->sourceTaps.empty())
    {
        auto reprepared = createPreparedImpulseFromTaps(
            current->impulse->sourceTaps,
            current->impulse->sourceSampleRate,
            currentSampleRate,
            current->impulse->id,
            current->impulse->name);
        if (reprepared != nullptr)
        {
            reprepared->sourcePath = current->impulse->sourcePath;
            reprepared->sourceSize = current->impulse->sourceSize;
            reprepared->sourceMtime = current->impulse->sourceMtime;
            reprepared->externalIdentity = current->impulse->externalIdentity;
            current = {};
            if (publishPrepared(std::move(reprepared)))
            {
                hasError.store(false, std::memory_order_release);
                errorMessage.clear();
            }
            else
            {
                activeGraph.store(nullptr, std::memory_order_release);
                hasError.store(true, std::memory_order_release);
                errorMessage = "realtime_graph_prepare_failed";
            }
        }
        else
        {
            current = {};
            activeGraph.store(nullptr, std::memory_order_release);
            hasError.store(true, std::memory_order_release);
            errorMessage = "sample_rate_reprepare_failed";
        }
    }
    clippingRisk.store(false, std::memory_order_release);
}

void ConvolutionProcessor::reset()
{
    resetForHardReplace();
}

void ConvolutionProcessor::resetForHardReplace()
{
    auto graph = acquireGraph();
    if (graph)
        graph->engine.resetForHardReplace();
    clippingRisk.store(false, std::memory_order_release);
}

void ConvolutionProcessor::continueForGapless() noexcept
{
    auto graph = acquireGraph();
    if (graph)
        graph->engine.continueForGapless();
}

template <typename Sample>
void ConvolutionProcessor::processBlock(echo::PlanarAudioBuffer<Sample>& buffer, int startSample, int numSamples)
{
    if (numSamples <= 0)
        return;

    auto graph = acquireGraph();
    const bool enabled = targetEnabled.load(std::memory_order_acquire);
    const int channelCount = std::min(buffer.getNumChannels(), preparedChannels);
    if (! enabled || !graph || graph->impulse->tapCount <= 0 || channelCount <= 0
        || buffer.getChannelCapacity() < graph->engine.outputChannels()
        || (deviceOutputChannels.load(std::memory_order_acquire) > 0 && graph->engine.outputChannels() != deviceOutputChannels.load(std::memory_order_acquire)))
    {
        runtimeLatency.store(0, std::memory_order_release);
        clippingRisk.store(false, std::memory_order_release);
        return;
    }

    runtimeLatency.store(graph->impulse->latencySamples, std::memory_order_release);
    const double trimGain = dbToGain(atomicTrimDb.load(std::memory_order_acquire));
    bool risk = false;
    for (int channel = 0; channel < channelCount; ++channel)
    {
        auto* samples = buffer.getWritePointer(channel, startSample);
        for (int sample = 0; sample < numSamples; ++sample)
            samples[sample] = sanitize(samples[sample]);
    }
    graph->engine.processBlock(buffer, startSample, numSamples);
    for (int channel = 0; channel < buffer.getNumChannels(); ++channel)
    {
        auto* samples = buffer.getWritePointer(channel, startSample);
        for (int sample = 0; sample < numSamples; ++sample)
            samples[sample] = protectClippingSample(
                samples[sample] * trimGain,
                isDspSafetyLimiterEnabled(),
                risk);
    }

    clippingRisk.store(risk, std::memory_order_release);
}

int64_t ConvolutionProcessor::tailFramesRemaining() const noexcept
{
    auto graph = acquireGraph();
    return graph && targetEnabled.load(std::memory_order_acquire)
        && (deviceOutputChannels.load(std::memory_order_acquire) == 0 || graph->engine.outputChannels() == deviceOutputChannels.load(std::memory_order_acquire))
        ? graph->engine.tailFramesRemaining()
        : 0;
}

void ConvolutionProcessor::setEnabled(bool shouldBeEnabled)
{
    std::lock_guard configurationLock(configurationMutex);
    targetEnabled.store(shouldBeEnabled, std::memory_order_release);
    if (shouldBeEnabled && filterBank && !acquireGraph() && !hasError.load())
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = filterBank->select(currentSampleRate, preparedChannels, false)
            ? "convolution_filter_budget_exceeded" : "convolution_no_matching_channels";
    }
}

void ConvolutionProcessor::setTrimDb(float value)
{
    atomicTrimDb.store(clampRoomCorrectionTrimDb(value), std::memory_order_release);
}

bool ConvolutionProcessor::loadImpulseResponse(
    const std::string& path,
    const std::string& id,
    const std::string& name,
    const std::string& identityToken,
    bool forceReload)
{
    std::lock_guard configurationLock(configurationMutex);
    configurationRevision.fetch_add(1, std::memory_order_acq_rel);
    if (std::filesystem::path(path).extension() == ".json")
    {
        try
        {
            auto bank = ConvolutionFilterBank::read(path);
            const auto oldBank = filterBank;
            const auto oldId = filterBankId;
            const auto oldName = filterBankName;
            const auto oldSelection = selectedFilter;
            filterBank = std::move(bank); filterBankId = id; filterBankName = name;
            if (filterBank->select(currentSampleRate, preparedChannels) == nullptr && !targetEnabled.load())
            {
                std::lock_guard<std::mutex> lock(graphMutex);
                activeGraph.store(nullptr, std::memory_order_release);
                reclaimRetiredGraphsLocked();
                selectedFilter = nullptr;
                hasError.store(false, std::memory_order_release); errorMessage.clear();
                return true;
            }
            if (loadSelectedBankFilter(forceReload)) return true;
            filterBank = oldBank; filterBankId = oldId; filterBankName = oldName;
            selectedFilter = oldSelection;
            return false;
        }
        catch (...)
        {
            hasError.store(true, std::memory_order_release);
            errorMessage = "convolution_manifest_invalid";
            return false;
        }
    }
    const auto previousBank = filterBank;
    filterBank.reset();
    if (!loadSingleImpulseResponse(path, id, name, identityToken, forceReload)) { filterBank = previousBank; return false; }
    selectedFilter = nullptr;
    return true;
}

bool ConvolutionProcessor::loadSelectedBankFilter(bool forceReload)
{
    const auto* selected = filterBank->select(currentSampleRate, preparedChannels);
    if (selected == nullptr)
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = filterBank->select(currentSampleRate, preparedChannels, false)
            ? "convolution_filter_budget_exceeded" : "convolution_no_matching_channels";
        selectedFilter = nullptr;
        return false;
    }
    if (!loadSingleImpulseResponse(selected->path, filterBankId, filterBankName, {}, forceReload)) return false;
    selectedFilter = { {"name", selected->name}, {"sourceSampleRate", selected->sampleRate},
        {"sourceChannels", selected->routes.empty() ? selected->channels : selected->inputChannels}, {"processingChannels", selected->routes.empty() ? preparedChannels : selected->channels},
        {"resampled", std::abs(currentSampleRate - selected->sampleRate) >= 0.5},
        {"processingPrecisionBits", 64}, {"routingPaths", selected->routes.size()} };
    selectedFilter["inputChannels"] = preparedChannels;
    auto routes = nlohmann::json::array();
    for (const auto& route : selected->routes) routes.push_back({{"input", route.input}, {"output", route.output}});
    selectedFilter["routes"] = std::move(routes);
    return true;
}

bool ConvolutionProcessor::loadSingleImpulseResponse(const std::string& path, const std::string& id,
    const std::string& name, const std::string& identityToken, bool forceReload)
{
    struct stat sourceStats {};
    if (stat(path.c_str(), &sourceStats) != 0 || !isRegularFile(sourceStats))
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = "missing_file";
        return false;
    }
    const auto sourcePath = normalizedIrPath(path);
    const auto sourceSize = static_cast<uint64_t>(std::max<int64_t>(0, sourceStats.st_size));
    std::error_code metadataError;
    const auto sourceMtime = static_cast<int64_t>(
        std::filesystem::last_write_time(std::filesystem::path(path), metadataError)
            .time_since_epoch().count());
    const auto stableSourceMtime = metadataError
        ? static_cast<int64_t>(sourceStats.st_mtime)
        : sourceMtime;
    {
        const auto current = acquireGraph();
        if (!forceReload
            && !hasError.load(std::memory_order_acquire)
            && current
            && current->channels == preparedChannels
            && current->impulse->sourcePath == sourcePath
            && current->impulse->sourceSize == sourceSize
            && current->impulse->sourceMtime == stableSourceMtime
            && (identityToken.empty() || current->impulse->externalIdentity == identityToken)
            && current->impulse->id == id
            && current->impulse->name == name
            && std::abs(current->impulse->sampleRate - currentSampleRate) < 0.5)
        {
            // Metadata/name changes do not alter the prepared convolution graph.
            // The persisted IR identity (path/size/mtime/target rate) is the
            // authoritative no-op key for a live host.
            return true;
        }
    }

    impulseDecodeCount.fetch_add(1, std::memory_order_relaxed);
    auto decoded = decodeAudioFile(path, currentSampleRate);
    auto& source = decoded.samples;
    if (source.empty() || source.front().empty())
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = decoded.error.empty() ? "invalid_audio" : decoded.error;
        return false;
    }

    const int sourceChannels = static_cast<int>(source.size());
    const int sourceSamples = static_cast<int>(source.front().size());
    const auto* expected = filterBank ? filterBank->select(currentSampleRate, preparedChannels) : nullptr;
    const std::vector<ConvolutionRoute> routes = expected ? expected->routes : std::vector<ConvolutionRoute>{};
    if ((routes.empty() && sourceChannels != 1 && sourceChannels != preparedChannels)
        || static_cast<int64_t>(sourceSamples) * sourceChannels > convolutionMaxCoefficients)
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = "convolution_channel_or_coefficient_limit";
        return false;
    }

    // Check impulse length against max taps
    const double sourceSampleRate = decoded.sampleRate > 0.0 ? decoded.sampleRate : currentSampleRate;
    if (filterBank)
    {
        if (expected == nullptr || expected->coefficientChannels != sourceChannels || expected->frames != sourceSamples
            || std::abs(expected->sampleRate - sourceSampleRate) >= 0.5)
        {
            hasError.store(true, std::memory_order_release);
            errorMessage = "convolution_filter_metadata_mismatch";
            return false;
        }
    }
    const double estimatedOutputSamples = static_cast<double>(sourceSamples)
        * currentSampleRate / std::max(1.0, sourceSampleRate);
    if (! std::isfinite(estimatedOutputSamples) || estimatedOutputSamples > static_cast<double>(roomCorrectionMaxTaps))
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = "impulse_too_long";
        return false;
    }

    auto prepared = createPreparedImpulseFromTaps(source, sourceSampleRate, currentSampleRate, id, name, routes, !routes.empty() && expected ? expected->channels : preparedChannels);
    if (prepared == nullptr)
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = "invalid_impulse";
        return false;
    }

    prepared->sourcePath = sourcePath;
    prepared->sourceSize = sourceSize;
    prepared->sourceMtime = stableSourceMtime;
    prepared->sourceSampleRate = sourceSampleRate;
    prepared->externalIdentity = identityToken;
    if (!publishPrepared(std::move(prepared)))
    {
        hasError.store(true, std::memory_order_release);
        errorMessage = "realtime_graph_prepare_failed";
        return false;
    }
    hasError.store(false, std::memory_order_release);
    errorMessage.clear();
    return true;
}

void ConvolutionProcessor::clearImpulseResponse()
{
    std::lock_guard configurationLock(configurationMutex);
    std::lock_guard<std::mutex> lock(graphMutex);
    configurationRevision.fetch_add(1, std::memory_order_acq_rel);
    activeGraph.store(nullptr, std::memory_order_release);
    filterBank.reset(); selectedFilter = nullptr;
    reclaimRetiredGraphsLocked();
    hasError.store(false, std::memory_order_release);
    errorMessage.clear();
}

RoomCorrectionState ConvolutionProcessor::getState() const
{
    std::lock_guard configurationLock(configurationMutex);
    auto graph = acquireGraph();
    RoomCorrectionState state;
    state.inputChannels = preparedChannels;
    state.outputChannels = requestedOutputChannels();
    state.deviceOutputChannels = deviceOutputChannels.load(std::memory_order_acquire);
    state.enabled = targetEnabled.load(std::memory_order_acquire);
    state.trimDb = atomicTrimDb.load(std::memory_order_acquire);
    state.clippingRisk = clippingRisk.load(std::memory_order_acquire);
    state.error = hasError.load(std::memory_order_acquire) ? errorMessage : std::string();
    if (filterBank)
    {
        state.filterBank = filterBank->summary();
        state.selectedFilter = selectedFilter;
        state.irId = filterBankId; state.irName = filterBankName;
    }

    if (graph && state.enabled && deviceOutputChannels.load(std::memory_order_acquire) > 0
        && graph->engine.outputChannels() != deviceOutputChannels.load(std::memory_order_acquire)) state.error = "convolution_output_channels_pending";
    if (state.enabled && state.outputChannels > 8) state.error = "convolution_output_device_layout_unsupported";
    if (!state.selectedFilter.is_null()) state.selectedFilter["deviceOutputChannels"] = deviceOutputChannels.load(std::memory_order_acquire);

    if (graph)
    {
        state.status = !state.error.empty() ? "error" : state.enabled ? "active" : "loaded";
        state.irId = graph->impulse->id;
        state.irName = graph->impulse->name;
        state.channelMode = graph->impulse->channelMode;
        state.sampleRate = graph->impulse->sampleRate;
        state.tapCount = graph->impulse->tapCount;
        state.latencySamples = graph->impulse->latencySamples;
    }
    else
    {
        state.status = state.error.empty() ? filterBank ? "loaded" : "empty" : "error";
        state.channelMode = "none";
    }

    return state;
}

bool ConvolutionProcessor::isEnabled() const
{
    auto graph = acquireGraph();
    return targetEnabled.load(std::memory_order_acquire) && graph && graph->impulse->tapCount > 0
        && (deviceOutputChannels.load(std::memory_order_acquire) == 0 || graph->engine.outputChannels() == deviceOutputChannels.load(std::memory_order_acquire));
}

int ConvolutionProcessor::requestedOutputChannels(int inputChannels, double sampleRate) const noexcept
{
    if (targetEnabled.load(std::memory_order_acquire) && filterBank && inputChannels > 0) {
        const auto* selected = filterBank->select(sampleRate > 0 ? sampleRate : currentSampleRate, inputChannels);
        if (selected) return selected->routes.empty() ? inputChannels : selected->channels;
    }
    auto graph = acquireGraph();
    return targetEnabled.load(std::memory_order_acquire) && graph ? graph->engine.outputChannels() : preparedChannels;
}

bool ConvolutionProcessor::hasClippingRisk() const
{
    return clippingRisk.load(std::memory_order_acquire);
}

std::unique_ptr<ConvolutionProcessor::PreparedImpulse> ConvolutionProcessor::createPreparedImpulse(
    const echo::FloatAudioBuffer& source,
    double sourceSampleRate,
    double targetSampleRate,
    const std::string& id,
    const std::string& name)
{
    if (source.getNumChannels() <= 0 || source.getNumSamples() <= 0 || ! isFiniteBuffer(source))
        return nullptr;

    std::vector<std::vector<double>> sourceTaps(
        static_cast<size_t>(source.getNumChannels()),
        std::vector<double>(static_cast<size_t>(source.getNumSamples()), 0.0f));
    for (int channel = 0; channel < static_cast<int>(sourceTaps.size()); ++channel)
    {
        const float* sourceChannel = source.getReadPointer(channel);
        std::copy_n(sourceChannel, sourceTaps[static_cast<size_t>(channel)].size(), sourceTaps[static_cast<size_t>(channel)].data());
    }
    return createPreparedImpulseFromTaps(sourceTaps, sourceSampleRate, targetSampleRate, id, name);
}

std::unique_ptr<ConvolutionProcessor::PreparedImpulse> ConvolutionProcessor::createPreparedImpulseFromTaps(
    const std::vector<std::vector<double>>& sourceTaps,
    double sourceSampleRate,
    double targetSampleRate,
    const std::string& id,
    const std::string& name, const std::vector<ConvolutionRoute>& routes, int outputChannels)
{
    if (sourceTaps.empty() || sourceTaps.size() > convolutionMaxChannels || sourceTaps.front().empty())
        return nullptr;
    const size_t sourceSamples = sourceTaps.front().size();
    for (const auto& channel : sourceTaps)
        if (channel.size() != sourceSamples
            || std::any_of(channel.begin(), channel.end(), [](double sample) { return !std::isfinite(sample); }))
            return nullptr;

    const double safeSourceRate = sourceSampleRate > 0.0 ? sourceSampleRate : targetSampleRate;
    const double safeTargetRate = targetSampleRate > 0.0 ? targetSampleRate : safeSourceRate;
    auto resampled = resampleImpulseResponse(sourceTaps, safeSourceRate, safeTargetRate,
        routes.empty() ? preparedChannels : static_cast<int>(sourceTaps.size()));
    if (resampled.taps.empty()) return nullptr;

    auto impulse = std::make_unique<PreparedImpulse>();
    impulse->id = id;
    impulse->name = name;
    impulse->sourceTaps = sourceTaps;
    impulse->routes = routes;
    impulse->outputChannels = outputChannels > 0 ? outputChannels : preparedChannels;
    impulse->sourceSampleRate = safeSourceRate;
    impulse->sampleRate = safeTargetRate;
    impulse->tapCount = static_cast<int>(resampled.taps.front().size());
    impulse->latencySamples = resampled.latencySamples;
    const int modeChannels = routes.empty() ? static_cast<int>(sourceTaps.size()) : impulse->outputChannels;
    impulse->channelMode = modeChannels > 2 ? "multichannel" : modeChannels == 2 ? "stereo" : "mono";
    impulse->taps = std::move(resampled.taps);

    return impulse;
}

double ConvolutionProcessor::sanitize(double value)
{
    return std::isfinite(value) ? value : 0.0f;
}

double ConvolutionProcessor::protectClippingSample(double sample, bool shouldProtect, bool& risk)
{
    (void)shouldProtect;

    if (! std::isfinite(sample))
        return 0.0f;

    constexpr float riskThreshold = 0.98f;
    const double magnitude = std::abs(sample);
    risk = risk || magnitude > riskThreshold;
    return sample;
}

#if defined(ECHO_AUDIO_ENGINE_TESTS) && ECHO_AUDIO_ENGINE_TESTS
bool ConvolutionProcessor::loadImpulseResponseForTests(
    const std::vector<std::vector<float>>& taps,
    double sourceSampleRate,
    const std::string& id,
    const std::string& name)
{
    if (taps.empty() || taps[0].empty() || taps[0].size() > static_cast<size_t>(roomCorrectionMaxTaps))
        return false;

    const int channels = static_cast<int>(taps.size());
    const int samples = static_cast<int>(taps[0].size());
    if (channels > convolutionMaxChannels || (channels != 1 && channels != preparedChannels)
        || static_cast<int64_t>(samples) * channels > convolutionMaxCoefficients
        || std::any_of(taps.begin(), taps.end(), [samples](const auto& channel) { return channel.size() != static_cast<size_t>(samples); }))
        return false;
    FloatAudioBuffer source(channels, samples);
    for (int ch = 0; ch < channels; ++ch)
    {
        float* dst = source.getWritePointer(ch);
        for (int s = 0; s < std::min(samples, static_cast<int>(taps[static_cast<size_t>(ch)].size())); ++s)
            dst[s] = taps[static_cast<size_t>(ch)][static_cast<size_t>(s)];
    }

    auto prepared = createPreparedImpulse(source, sourceSampleRate, currentSampleRate, id, name);
    if (prepared == nullptr)
        return false;

    if (!publishPrepared(std::move(prepared)))
        return false;
    hasError.store(false, std::memory_order_release);
    errorMessage.clear();
    return true;
}
#endif

template void ConvolutionProcessor::processBlock<float>(echo::PlanarAudioBuffer<float>&, int, int);
template void ConvolutionProcessor::processBlock<double>(echo::PlanarAudioBuffer<double>&, int, int);
} // namespace echo
