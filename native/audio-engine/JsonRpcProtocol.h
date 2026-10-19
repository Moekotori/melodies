#pragma once
#include "ChannelBalanceProcessor.h"
#include "CompressorProcessor.h"
#include "ConvolutionProcessor.h"
#include "DeEsserProcessor.h"
#include "DspChain.h"
#include "DspHeadroomProcessor.h"
#include "DspRackOrder.h"
#include "EqProcessor.h"
#include "EqPresetStore.h"
#include "LevelMeterProcessor.h"
#include "MultibandCompressorProcessor.h"
#include "PlaybackRateProcessor.h"
#include "ReplayGainProcessor.h"
#include "SpatialDspProcessor.h"
#include "WorkshopAudioEffectProcessor.h"
#include "third_party/nlohmann_json.hpp"
#include <cstdint>
#include <functional>
#include <string>
namespace echo {
class JsonRpcProtocol {
public:
    using WriteCallback = std::function<void(const std::string&)>;
    static void setWriteCallback(WriteCallback callback);
    using OpenFileCallback = std::function<bool(
        const std::string& filePath,
        int targetSampleRate,
        double startSeconds,
        nlohmann::json& result)>;
    static void setOpenFileCallback(OpenFileCallback callback);
    using OpenSourceCallback = std::function<bool(
        const nlohmann::json& source,
        int targetSampleRate,
        double startSeconds,
        nlohmann::json& result)>;
    static void setOpenSourceCallback(OpenSourceCallback callback);
    using MixBalanceCallback = std::function<bool(const nlohmann::json&, nlohmann::json&)>;
    static void setMixBalanceCallback(MixBalanceCallback callback);

    using PauseCallback = std::function<bool(bool pause, uint64_t expectedOperationId)>;
    static void setPauseCallback(PauseCallback callback);

    using SeekCallback = std::function<bool(double positionSeconds, uint64_t expectedOperationId, nlohmann::json& result)>;
    static void setSeekCallback(SeekCallback callback);

    using StopCallback = std::function<bool(uint64_t expectedOperationId, nlohmann::json& result)>;
    static void setStopCallback(StopCallback callback);

    using PrefetchCallback = std::function<bool(const std::string& filePath, int targetSampleRate)>;
    static void setPrefetchCallback(PrefetchCallback callback);

    using GaplessPrepareCallback = std::function<bool(const nlohmann::json& request, nlohmann::json& result)>;
    static void setGaplessPrepareCallback(GaplessPrepareCallback callback);

    using AutomixPrepareCallback = std::function<bool(const nlohmann::json& request, nlohmann::json& result)>;
    static void setAutomixPrepareCallback(AutomixPrepareCallback callback);

    using AutomixCancelCallback = std::function<bool(const std::string& planId, nlohmann::json& result)>;
    static void setAutomixCancelCallback(AutomixCancelCallback callback);

    using AutomixStateCallback = std::function<nlohmann::json()>;
    static void setAutomixStateCallback(AutomixStateCallback callback);

    using VolumeCallback = std::function<void(float volume)>;
    static void setVolumeCallback(VolumeCallback callback);

    using PlaybackRateCallback = std::function<void(float rate, SpeedMode mode)>;
    static void setPlaybackRateCallback(PlaybackRateCallback callback);

    using QueueSetCallback = std::function<bool(
        const nlohmann::json& items,
        const std::string& repeatMode,
        uint64_t revision,
        const std::string& currentItemId,
        uint64_t cursorOperationId)>;
    static void setQueueSetCallback(QueueSetCallback callback);

    using QueueClearCallback = std::function<bool()>;
    static void setQueueClearCallback(QueueClearCallback callback);

    static std::string handleJsonLine(
        const std::string& line,
        EqProcessor& eq, ChannelBalanceProcessor& cb, ConvolutionProcessor& conv,
        DspHeadroomProcessor& headroom, ReplayGainProcessor& rg, CompressorProcessor& compressor,
        SpatialDspProcessor& spatialDsp,
        PlaybackRateProcessor& rate, LevelMeterProcessor& meter, DspRackOrder& rackOrder,
        WorkshopAudioEffectController& workshopAudioEffect,
        MultibandCompressorProcessor& multiband, DeEsserProcessor& deEsser,
        EqPresetStore& presets, bool proDspAllowed = true);
    static nlohmann::json buildCompressorTelemetry(const CompressorProcessor& compressor);
    static std::string createJsonRpcNotification(const std::string& method, const nlohmann::json& params);
private:
    static std::string createJsonRpcResponse(int id, const nlohmann::json& result);
    static std::string createJsonRpcError(int id, int code, const std::string& message);
    static WriteCallback writeCallback;
    static OpenFileCallback openFileCallback;
    static OpenSourceCallback openSourceCallback;
    static MixBalanceCallback mixBalanceCallback;
    static PauseCallback pauseCallback;
    static SeekCallback seekCallback;
    static StopCallback stopCallback;
    static PrefetchCallback prefetchCallback;
    static GaplessPrepareCallback gaplessPrepareCallback;
    static AutomixPrepareCallback automixPrepareCallback;
    static AutomixCancelCallback automixCancelCallback;
    static AutomixStateCallback automixStateCallback;
    static VolumeCallback volumeCallback;
    static PlaybackRateCallback playbackRateCallback;
    static QueueSetCallback queueSetCallback;
    static QueueClearCallback queueClearCallback;
};
}
