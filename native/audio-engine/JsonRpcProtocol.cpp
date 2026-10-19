#include "DspMeasurementJson.h"
#include "JsonRpcProtocol.h"
#include "HeadphoneCorrectionProtocol.h"
#include "DspSafetyLimiter.h"
#include "EqTypes.h"
#include "libav_decoder.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace echo
{
namespace
{
float getNumber(const nlohmann::json& obj, const char* key, float fallback)
{
    const auto value = obj.value(key, nlohmann::json());
    return value.is_number() ? value.get<float>() : fallback;
}

bool getBool(const nlohmann::json& obj, const char* key, bool fallback)
{
    const auto value = obj.value(key, nlohmann::json());
    return value.is_boolean() ? value.get<bool>() : fallback;
}

int getInt(const nlohmann::json& obj, const char* key, int fallback)
{
    const auto value = obj.value(key, nlohmann::json());
    return value.is_number() ? value.get<int>() : fallback;
}

std::string getString(const nlohmann::json& obj, const char* key)
{
    const auto value = obj.value(key, nlohmann::json());
    return value.is_string() ? value.get<std::string>() : std::string();
}

double normalizeStartSeconds(double requestedStartSeconds, double durationSeconds)
{
    if (requestedStartSeconds < 0.0)
        return 0.0;
    if (durationSeconds > 0.0 && requestedStartSeconds >= durationSeconds)
        return std::max(0.0, durationSeconds - 0.250);
    return requestedStartSeconds;
}

EqFilterType parseEqFilterType(const std::string& value, EqFilterType fallback)
{
    if (value == "lowShelf")  return EqFilterType::LowShelf;
    if (value == "highShelf") return EqFilterType::HighShelf;
    if (value == "lowPass")   return EqFilterType::LowPass;
    if (value == "highPass")  return EqFilterType::HighPass;
    if (value == "notch")     return EqFilterType::Notch;
    if (value == "peaking")   return EqFilterType::Peaking;
    return fallback;
}

bool isEqFilterTypeText(const std::string& value)
{
    return value == "peaking" || value == "lowShelf" || value == "highShelf"
        || value == "lowPass" || value == "highPass" || value == "notch";
}

std::string eqFilterTypeText(EqFilterType value)
{
    switch (value)
    {
        case EqFilterType::LowShelf:  return "lowShelf";
        case EqFilterType::HighShelf: return "highShelf";
        case EqFilterType::LowPass:   return "lowPass";
        case EqFilterType::HighPass:  return "highPass";
        case EqFilterType::Notch:     return "notch";
        case EqFilterType::Peaking:
        default:                      return "peaking";
    }
}

ChannelBalanceMonoMode parseMonoMode(const std::string& value, ChannelBalanceMonoMode fallback)
{
    if (value == "sum")   return ChannelBalanceMonoMode::SumToMono;
    if (value == "left")  return ChannelBalanceMonoMode::LeftOnly;
    if (value == "right") return ChannelBalanceMonoMode::RightOnly;
    if (value == "off")   return ChannelBalanceMonoMode::Off;
    return fallback;
}

std::string monoModeText(ChannelBalanceMonoMode mode)
{
    switch (mode)
    {
        case ChannelBalanceMonoMode::SumToMono: return "sum";
        case ChannelBalanceMonoMode::LeftOnly:  return "left";
        case ChannelBalanceMonoMode::RightOnly: return "right";
        case ChannelBalanceMonoMode::Off:
        default:                                return "off";
    }
}

float readBandGainDb(const nlohmann::json* bandsObject, const char* bandId, const char* sideKey, float fallback)
{
    if (bandsObject == nullptr)
        return fallback;

    auto bandIt = bandsObject->find(bandId);
    if (bandIt == bandsObject->end() || !bandIt->is_object())
        return fallback;

    return clampChannelBandGainDb(getNumber(*bandIt, sideKey, fallback));
}

ChannelBalanceState readChannelBalanceState(const nlohmann::json& obj, const ChannelBalanceState& fallback)
{
    ChannelBalanceState state = fallback;
    state.enabled = getBool(obj, "enabled", state.enabled);
    state.balance = clampChannelBalance(getNumber(obj, "balance", state.balance));
    state.leftGainDb = clampChannelGainDb(getNumber(obj, "leftGainDb", state.leftGainDb));
    state.rightGainDb = clampChannelGainDb(getNumber(obj, "rightGainDb", state.rightGainDb));

    auto bandsIt = obj.find("bandGains");
    const nlohmann::json* bandsObject = nullptr;
    if (bandsIt != obj.end() && bandsIt->is_object())
        bandsObject = &(*bandsIt);

    state.leftBandGainsDb[0] = readBandGainDb(bandsObject, "low", "leftGainDb", state.leftBandGainsDb[0]);
    state.rightBandGainsDb[0] = readBandGainDb(bandsObject, "low", "rightGainDb", state.rightBandGainsDb[0]);
    state.leftBandGainsDb[1] = readBandGainDb(bandsObject, "mid", "leftGainDb", state.leftBandGainsDb[1]);
    state.rightBandGainsDb[1] = readBandGainDb(bandsObject, "mid", "rightGainDb", state.rightBandGainsDb[1]);
    state.leftBandGainsDb[2] = readBandGainDb(bandsObject, "high", "leftGainDb", state.leftBandGainsDb[2]);
    state.rightBandGainsDb[2] = readBandGainDb(bandsObject, "high", "rightGainDb", state.rightBandGainsDb[2]);

    state.leftDelayMs = clampChannelDelayMs(getNumber(obj, "leftDelayMs", state.leftDelayMs));
    state.rightDelayMs = clampChannelDelayMs(getNumber(obj, "rightDelayMs", state.rightDelayMs));
    state.swapLeftRight = getBool(obj, "swapLeftRight", state.swapLeftRight);
    state.monoMode = parseMonoMode(getString(obj, "monoMode"), state.monoMode);
    state.invertLeft = getBool(obj, "invertLeft", state.invertLeft);
    state.invertRight = getBool(obj, "invertRight", state.invertRight);
    state.constantPower = getBool(obj, "constantPower", state.constantPower);
    return state;
}

SpeedMode parseSpeedMode(const std::string& value, SpeedMode fallback)
{
    if (value == "nightcore") return SpeedMode::Nightcore;
    if (value == "daycore")   return SpeedMode::Daycore;
    if (value == "speed")     return SpeedMode::Speed;
    return fallback;
}

std::string speedModeText(SpeedMode mode)
{
    switch (mode)
    {
        case SpeedMode::Nightcore: return "nightcore";
        case SpeedMode::Daycore:   return "daycore";
        case SpeedMode::Speed:     return "speed";
    }
    return "nightcore";
}

nlohmann::json buildEqState(const EqProcessor& processor)
{
    const auto state = processor.getState();
    nlohmann::json obj;
    obj["loudness"] = dspMeasurementJson(processor.loudnessMeasurement());
    obj["enabled"] = state.enabled;
    obj["preampDb"] = state.preampDb;
    obj["strengthPercent"] = state.strengthPercent;
    obj["tiltDb"] = state.tiltDb;
    obj["dspHeadroomDb"] = 0;
    obj["dspSafetyLimiterEnabled"] = isDspSafetyLimiterEnabled();
    const float limiterCeilingDb = dspLimiterCeilingDb();
    obj["dspLimiterCeilingDb"] = std::isfinite(limiterCeilingDb)
        ? nlohmann::json(limiterCeilingDb)
        : nlohmann::json(nullptr);
    obj["dspLimiterReleaseMs"] = dspLimiterReleaseMs();
    obj["presetId"] = "custom";
    obj["presetName"] = state.presetName;
    obj["clippingRisk"] = processor.hasClippingRisk();

    nlohmann::json bands = nlohmann::json::array();
    for (int i = 0; i < eqBandCount; ++i)
    {
        nlohmann::json band;
        band["frequencyHz"] = state.bandFrequenciesHz[static_cast<size_t>(i)];
        band["gainDb"] = state.bandGainsDb[static_cast<size_t>(i)];
        band["q"] = state.bandQ[static_cast<size_t>(i)];
        band["filterType"] = eqFilterTypeText(state.bandFilterTypes[static_cast<size_t>(i)]);
        band["enabled"] = state.bandEnabled[static_cast<size_t>(i)];
        band["scope"] = eqBandScopeText(state.bandScopes[static_cast<size_t>(i)]);
        band["dynamicMode"] = eqDynamicModeText(state.bandDynamicModes[static_cast<size_t>(i)]);
        band["dynamicThresholdDb"] = state.bandDynamicThresholdDb[static_cast<size_t>(i)];
        band["dynamicRatio"] = state.bandDynamicRatio[static_cast<size_t>(i)];
        band["dynamicAttackMs"] = state.bandDynamicAttackMs[static_cast<size_t>(i)];
        band["dynamicReleaseMs"] = state.bandDynamicReleaseMs[static_cast<size_t>(i)];
        bands.push_back(band);
    }
    obj["bands"] = bands;
    return obj;
}

nlohmann::json buildDspState(const DspHeadroomProcessor& processor)
{
    nlohmann::json obj;
    obj["headroomDb"] = processor.getHeadroomDb();
    obj["safetyLimiterEnabled"] = isDspSafetyLimiterEnabled();
    const float limiterCeilingDb = dspLimiterCeilingDb();
    obj["dspLimiterCeilingDb"] = std::isfinite(limiterCeilingDb)
        ? nlohmann::json(limiterCeilingDb)
        : nlohmann::json(nullptr);
    obj["dspLimiterReleaseMs"] = dspLimiterReleaseMs();
    return obj;
}

nlohmann::json buildWorkshopAudioEffectState(const WorkshopAudioEffectController& controller)
{
    const auto state = controller.getState();
    return nlohmann::json::object({
        {"enabled", state.enabled},
        {"effect", workshopAudioEffectTypeName(state.effect)},
        {"bitDepth", state.bitDepth},
        {"sampleRateHz", state.sampleRateHz},
        {"mix", state.mix},
        {"outputGainDb", state.outputGainDb},
        {"pulseMix", state.pulseMix},
        {"triangleMix", state.triangleMix},
        {"noiseMix", state.noiseMix},
        {"drive", state.drive},
        {"vocalCutStrength", state.vocalCutStrength},
        {"vocalCutBassPreserveHz", state.vocalCutBassPreserveHz},
        {"revision", controller.revision()}
    });
}

nlohmann::json buildChannelBalanceState(const ChannelBalanceProcessor& processor)
{
    const auto state = processor.getState();
    nlohmann::json obj;
    obj["enabled"] = state.enabled;
    obj["balance"] = state.balance;
    obj["leftGainDb"] = state.leftGainDb;
    obj["rightGainDb"] = state.rightGainDb;

    nlohmann::json bandGains;
    nlohmann::json low;
    low["leftGainDb"] = state.leftBandGainsDb[0];
    low["rightGainDb"] = state.rightBandGainsDb[0];
    bandGains["low"] = low;
    nlohmann::json mid;
    mid["leftGainDb"] = state.leftBandGainsDb[1];
    mid["rightGainDb"] = state.rightBandGainsDb[1];
    bandGains["mid"] = mid;
    nlohmann::json high;
    high["leftGainDb"] = state.leftBandGainsDb[2];
    high["rightGainDb"] = state.rightBandGainsDb[2];
    bandGains["high"] = high;
    obj["bandGains"] = bandGains;

    obj["leftDelayMs"] = state.leftDelayMs;
    obj["rightDelayMs"] = state.rightDelayMs;
    obj["swapLeftRight"] = state.swapLeftRight;
    obj["monoMode"] = monoModeText(state.monoMode);
    obj["invertLeft"] = state.invertLeft;
    obj["invertRight"] = state.invertRight;
    obj["constantPower"] = state.constantPower;
    obj["clippingRisk"] = processor.hasClippingRisk();
    return obj;
}

nlohmann::json buildRoomCorrectionState(const ConvolutionProcessor& processor)
{
    const auto state = processor.getState();
    nlohmann::json obj;
    obj["enabled"] = state.enabled;
    obj["status"] = state.status;
    obj["irId"] = state.irId;
    obj["irName"] = state.irName;
    obj["channelMode"] = state.channelMode;
    obj["filterBank"] = state.filterBank;
    obj["selectedFilter"] = state.selectedFilter;
    obj["sampleRate"] = state.sampleRate;
    obj["tapCount"] = state.tapCount;
    obj["trimDb"] = state.trimDb;
    obj["latencySamples"] = state.latencySamples;
    obj["clippingRisk"] = state.clippingRisk;
    obj["error"] = state.error;
    return obj;
}

nlohmann::json buildReplayGainConfig(const ReplayGainProcessor& processor)
{
    const auto config = processor.getConfig();
    nlohmann::json obj;
    obj["trackGainDb"] = config.trackGainDb;
    obj["albumGainDb"] = config.albumGainDb;
    obj["peak"] = config.peak;
    obj["mode"] = config.mode;
    obj["preampDb"] = config.preampDb;
    obj["preventClipping"] = config.preventClipping;
    obj["appliedGainDb"] = processor.getAppliedGainDb();
    obj["active"] = processor.isActive();
    return obj;
}

const char* compressorDetectorModeText(CompressorDetectorMode mode)
{
    return mode == CompressorDetectorMode::Rms ? "rms" : "peak";
}

CompressorDetectorMode parseCompressorDetectorMode(
    const std::string& value,
    CompressorDetectorMode fallback)
{
    if (value == "peak") return CompressorDetectorMode::Peak;
    if (value == "rms") return CompressorDetectorMode::Rms;
    return fallback;
}

nlohmann::json buildCompressorTelemetryObject(const CompressorProcessor& processor)
{
    const auto telemetry = processor.getTelemetry();
    return nlohmann::json::object({
        {"inputPeakDb", telemetry.inputPeakDb},
        {"inputRmsDb", telemetry.inputRmsDb},
        {"outputPeakDb", telemetry.outputPeakDb},
        {"outputRmsDb", telemetry.outputRmsDb},
        {"gainReductionDb", telemetry.gainReductionDb},
        {"gainReductionDbByChannel", telemetry.gainReductionDbByChannel},
        {"outputHeadroomDb", telemetry.outputHeadroomDb},
        {"clippingRisk", telemetry.clippingRisk}
    });
}

nlohmann::json buildCompressorState(const CompressorProcessor& processor)
{
    const auto state = processor.getState();
    auto result = nlohmann::json::object({
        {"enabled", state.enabled},
        {"thresholdDb", state.thresholdDb},
        {"ratio", state.ratio},
        {"attackMs", state.attackMs},
        {"releaseMs", state.releaseMs},
        {"kneeDb", state.kneeDb},
        {"makeupDb", state.makeupDb},
        {"mix", state.mix},
        {"detectorMode", compressorDetectorModeText(state.detectorMode)},
        {"sidechainHighpassEnabled", state.sidechainHighpassEnabled},
        {"sidechainHighpassHz", state.sidechainHighpassHz},
        {"autoRelease", state.autoRelease},
        {"rangeDb", state.rangeDb},
        {"stereoLink", state.stereoLink}
    });
    const auto telemetry = buildCompressorTelemetryObject(processor);
    for (const auto& [key, value] : telemetry.items())
        result[key] = value;
    return result;
}

CompressorState readCompressorState(const nlohmann::json& value, const CompressorState& fallback)
{
    CompressorState state = fallback;
    state.enabled = getBool(value, "enabled", fallback.enabled);
    state.thresholdDb = getNumber(value, "thresholdDb", fallback.thresholdDb);
    state.ratio = getNumber(value, "ratio", fallback.ratio);
    state.attackMs = getNumber(value, "attackMs", fallback.attackMs);
    state.releaseMs = getNumber(value, "releaseMs", fallback.releaseMs);
    state.kneeDb = getNumber(value, "kneeDb", fallback.kneeDb);
    state.makeupDb = getNumber(value, "makeupDb", fallback.makeupDb);
    state.mix = getNumber(value, "mix", fallback.mix);
    state.detectorMode = parseCompressorDetectorMode(
        getString(value, "detectorMode"), fallback.detectorMode);
    state.sidechainHighpassEnabled = getBool(
        value, "sidechainHighpassEnabled", fallback.sidechainHighpassEnabled);
    state.sidechainHighpassHz = getNumber(
        value, "sidechainHighpassHz", fallback.sidechainHighpassHz);
    state.autoRelease = getBool(value, "autoRelease", fallback.autoRelease);
    state.rangeDb = getNumber(value, "rangeDb", fallback.rangeDb);
    state.stereoLink = getNumber(value, "stereoLink", fallback.stereoLink);
    return state;
}

nlohmann::json buildCrossfeedState(const SpatialDspProcessor& processor)
{
    const auto state = processor.getCrossfeedState();
    return nlohmann::json::object({
        {"enabled", state.enabled},
        {"amount", state.amount},
        {"model", state.bs2b ? "bs2b" : "legacy"},
        {"feedDb", state.feedDb},
        {"cutoffHz", state.cutoffHz}
    });
}

nlohmann::json buildVirtualStageState(const SpatialDspProcessor& processor)
{
    const auto state = processor.getVirtualStageState();
    return nlohmann::json::object({
        {"enabled", state.enabled},
        {"strength", state.strength},
        {"speakersBypassed", state.speakersBypassed},
        {"azimuthDegrees", state.azimuthDegrees},
        {"reflection", state.reflection}
    });
}

nlohmann::json buildStereoFieldState(const SpatialDspProcessor& processor)
{
    const auto state = processor.getStereoFieldState();
    return nlohmann::json::object({
        {"enabled", state.enabled},
        {"width", state.width},
        {"centerGainDb", state.centerGainDb},
        {"sideGainDb", state.sideGainDb},
        {"clippingRisk", processor.hasStereoFieldClippingRisk()}
    });
}

nlohmann::json buildChannelMatrixState(const SpatialDspProcessor& processor)
{
    const auto state = processor.getChannelMatrixState();
    return nlohmann::json::object({
        {"enabled", state.enabled},
        {"leftToLeft", state.leftToLeft},
        {"rightToLeft", state.rightToLeft},
        {"leftToRight", state.leftToRight},
        {"rightToRight", state.rightToRight},
        {"clippingRisk", processor.hasChannelMatrixClippingRisk()}
    });
}

CrossfeedState readCrossfeedState(const nlohmann::json& value, const CrossfeedState& fallback)
{
    return {
        getBool(value, "enabled", fallback.enabled),
        getNumber(value, "amount", fallback.amount),
        getNumber(value, "cutoffHz", fallback.cutoffHz),
        (value.contains("model") ? getString(value, "model") == "bs2b" : fallback.bs2b),
        getNumber(value, "feedDb", fallback.feedDb)
    };
}

VirtualStageState readVirtualStageState(const nlohmann::json& value, const VirtualStageState& fallback)
{
    return {
        getBool(value, "enabled", fallback.enabled),
        static_cast<float>(getNumber(value, "strength", fallback.strength)),
        getBool(value, "speakersBypassed", fallback.speakersBypassed),
        static_cast<float>(getNumber(value, "azimuthDegrees", fallback.azimuthDegrees)),
        static_cast<float>(getNumber(value, "reflection", fallback.reflection))
    };
}

StereoFieldState readStereoFieldState(const nlohmann::json& value, const StereoFieldState& fallback)
{
    return {
        getBool(value, "enabled", fallback.enabled),
        getNumber(value, "width", fallback.width),
        getNumber(value, "centerGainDb", fallback.centerGainDb),
        getNumber(value, "sideGainDb", fallback.sideGainDb)
    };
}

ChannelMatrixState readChannelMatrixState(const nlohmann::json& value, const ChannelMatrixState& fallback)
{
    return {
        getBool(value, "enabled", fallback.enabled),
        getNumber(value, "leftToLeft", fallback.leftToLeft),
        getNumber(value, "rightToLeft", fallback.rightToLeft),
        getNumber(value, "leftToRight", fallback.leftToRight),
        getNumber(value, "rightToRight", fallback.rightToRight)
    };
}

nlohmann::json buildPresetList()
{
    const auto presets = EqPresetStore::createBuiltInPresets();
    nlohmann::json result = nlohmann::json::array();
    for (const auto& preset : presets)
    {
        nlohmann::json obj;
        obj["id"] = preset.id;
        obj["name"] = preset.name;
        obj["preampDb"] = preset.preampDb;
        obj["readonly"] = preset.readonlyPreset;

        nlohmann::json bands = nlohmann::json::array();
        for (const auto& band : preset.bands)
        {
            nlohmann::json b;
            b["frequencyHz"] = band.frequencyHz;
            b["gainDb"] = band.gainDb;
            b["q"] = band.q;
            b["filterType"] = eqFilterTypeText(band.filterType);
            b["enabled"] = band.enabled;
            bands.push_back(b);
        }
        obj["bands"] = bands;
        result.push_back(obj);
    }
    return result;
}

nlohmann::json buildDeEsserState(const DeEsserProcessor& processor)
{
    const auto state = processor.getState();
    return nlohmann::json::object({
        {"enabled", state.enabled},
        {"frequencyHz", state.frequencyHz},
        {"q", state.q},
        {"thresholdDb", state.thresholdDb},
        {"ratio", state.ratio},
        {"attackMs", state.attackMs},
        {"releaseMs", state.releaseMs},
        {"mix", state.mix},
        {"kneeDb", state.kneeDb},
        {"rangeDb", state.rangeDb},
        {"gainReductionDb", processor.gainReductionDb()}
    });
}

DeEsserState readDeEsserState(const nlohmann::json& value, const DeEsserState& fallback)
{
    return {
        getBool(value, "enabled", fallback.enabled),
        getNumber(value, "frequencyHz", fallback.frequencyHz),
        getNumber(value, "q", fallback.q),
        getNumber(value, "thresholdDb", fallback.thresholdDb),
        getNumber(value, "ratio", fallback.ratio),
        getNumber(value, "attackMs", fallback.attackMs),
        getNumber(value, "releaseMs", fallback.releaseMs),
        getNumber(value, "mix", fallback.mix),
        getNumber(value, "kneeDb", fallback.kneeDb),
        getNumber(value, "rangeDb", fallback.rangeDb)
    };
}

nlohmann::json buildMultibandState(const MultibandCompressorProcessor& processor)
{
    const auto state = processor.getState();
    nlohmann::json bands = nlohmann::json::array();
    nlohmann::json gainReductionDb = nlohmann::json::array();
    for (int band = 0; band < multibandBandCount; ++band)
    {
        const auto& source = state.bands[static_cast<size_t>(band)];
        bands.push_back(nlohmann::json::object({
            {"enabled", source.enabled},
            {"thresholdDb", source.thresholdDb},
            {"ratio", source.ratio},
            {"attackMs", source.attackMs},
            {"releaseMs", source.releaseMs},
            {"makeupDb", source.makeupDb},
            {"kneeDb", source.kneeDb}
        }));
        gainReductionDb.push_back(processor.gainReductionDb(band));
    }
    return nlohmann::json::object({
        {"enabled", state.enabled},
        {"lowCrossoverHz", state.lowCrossoverHz},
        {"highCrossoverHz", state.highCrossoverHz},
        {"mix", state.mix},
        {"stereoLink", state.stereoLink},
        {"bands", bands},
        {"gainReductionDb", gainReductionDb}
    });
}

MultibandCompressorState readMultibandState(const nlohmann::json& value, const MultibandCompressorState& fallback)
{
    MultibandCompressorState state = fallback;
    state.enabled = getBool(value, "enabled", fallback.enabled);
    state.lowCrossoverHz = getNumber(value, "lowCrossoverHz", fallback.lowCrossoverHz);
    state.highCrossoverHz = getNumber(value, "highCrossoverHz", fallback.highCrossoverHz);
    state.mix = getNumber(value, "mix", fallback.mix);
    state.stereoLink = getNumber(value, "stereoLink", fallback.stereoLink);
    const auto bands = value.find("bands");
    if (bands != value.end() && bands->is_array())
    {
        const auto count = std::min(multibandBandCount, static_cast<int>(bands->size()));
        for (int band = 0; band < count; ++band)
        {
            const auto& item = (*bands)[static_cast<size_t>(band)];
            if (!item.is_object())
                continue;
            auto& target = state.bands[static_cast<size_t>(band)];
            target.enabled = getBool(item, "enabled", target.enabled);
            target.thresholdDb = getNumber(item, "thresholdDb", target.thresholdDb);
            target.ratio = getNumber(item, "ratio", target.ratio);
            target.attackMs = getNumber(item, "attackMs", target.attackMs);
            target.releaseMs = getNumber(item, "releaseMs", target.releaseMs);
            target.makeupDb = getNumber(item, "makeupDb", target.makeupDb);
            target.kneeDb = getNumber(item, "kneeDb", target.kneeDb);
        }
    }
    return state;
}

nlohmann::json buildDspRackState(
    const DspRackOrder& rackOrder,
    const CompressorProcessor& compressor,
    const MultibandCompressorProcessor& multiband,
    const DeEsserProcessor& deEsser,
    const SpatialDspProcessor& spatialDsp)
{
    nlohmann::json order = nlohmann::json::array();
    for (const auto module : rackOrder.snapshot())
        order.push_back(DspRackOrder::moduleIdText(module));

    return nlohmann::json::object({
        {"schemaVersion", 5},
        {"filters", rackOrder.filters.state()["filters"]},
        {"sequence", rackOrder.filters.state()["sequence"]},
        {"procedural", rackOrder.procedural.getState()},
        {"order", order},
        {"compressor", buildCompressorState(compressor)},
        {"multiband", buildMultibandState(multiband)},
        {"deesser", buildDeEsserState(deEsser)},
        {"crossfeed", buildCrossfeedState(spatialDsp)},
        {"virtualStage", buildVirtualStageState(spatialDsp)},
        {"stereoField", buildStereoFieldState(spatialDsp)},
        {"channelMatrix", buildChannelMatrixState(spatialDsp)},
        {"reorderableModules", nlohmann::json::array({
            "equalizer", "procedural", "convolution", "replayGain", "compressor",
            "multiband", "deesser",
            "crossfeed", "stereoField", "channelMatrix", "channelBalance"
        })},
        {"fixedPostStages", nlohmann::json::array({
            "headroom", "truePeakLimiter", "playbackRate", "levelMeter"
        })}
    });
}

const nlohmann::json* getParamsObject(const nlohmann::json& params)
{
    if (params.is_array())
    {
        if (params.size() > 0 && params[0].is_object())
            return &params[0];
        return nullptr;
    }
    if (params.is_object())
        return &params;
    return nullptr;
}

float getParamsNumber(const nlohmann::json& params, float fallback)
{
    if (params.is_array())
    {
        if (params.size() > 0 && params[0].is_number())
            return params[0].get<float>();
        return fallback;
    }
    if (params.is_number())
        return params.get<float>();
    return fallback;
}

bool getParamsBool(const nlohmann::json& params, bool fallback)
{
    if (params.is_array())
    {
        if (params.size() > 0 && params[0].is_boolean())
            return params[0].get<bool>();
        return fallback;
    }
    if (params.is_boolean())
        return params.get<bool>();
    return fallback;
}

std::string getParamsString(const nlohmann::json& params)
{
    if (params.is_array())
    {
        if (params.size() > 0 && params[0].is_string())
            return params[0].get<std::string>();
        return std::string();
    }
    if (params.is_string())
        return params.get<std::string>();
    return std::string();
}

int getParamsInt(const nlohmann::json& params, int fallback)
{
    if (params.is_array())
    {
        if (params.size() > 0 && params[0].is_number())
            return params[0].get<int>();
        return fallback;
    }
    if (params.is_number())
        return params.get<int>();
    return fallback;
}

bool stateRequestEnablesProcessing(const nlohmann::json& params)
{
    const auto* paramsObj = getParamsObject(params);
    return paramsObj != nullptr && getBool(*paramsObj, "enabled", false);
}

bool requestRequiresProDsp(const std::string& method, const nlohmann::json& params)
{
    // Safety limiter is a fixed post-stage, not a Pro processor. Its RPC
    // defaults to enabled; treating that default as Pro made ordinary
    // hydrate (and therefore every local play) fail closed on -32003.
    if (method == "dspRack.setState") {
        const auto* object = getParamsObject(params);
        if (object && object->contains("filters") && (*object)["filters"].is_array())
            for (const auto& filter : (*object)["filters"]) if (!filter.is_object() || filter.value("enabled", nlohmann::json(false)) != false) return true;
        return object && object->contains("procedural")
            && (*object)["procedural"].is_object()
            && (*object)["procedural"].value("enabled", nlohmann::json(false)) != false;
    }
    if (method == "roomCorrection.setEnabled")
        return getParamsBool(params, false);
    if (method == "compressor.setState" || method == "multiband.setState" || method == "deesser.setState"
        || method == "crossfeed.setState"
        || method == "virtualStage.setState"
        || method == "stereoField.setState" || method == "channelMatrix.setState"
        || method == "channelBalance.setState")
        return stateRequestEnablesProcessing(params);
    if (method == "deesser.setListen")
        return getParamsBool(params, false);
    if (method == "workshopAudioEffect.setState")
        return stateRequestEnablesProcessing(params);
    return false;
}

} // namespace

nlohmann::json JsonRpcProtocol::buildCompressorTelemetry(const CompressorProcessor& compressor)
{
    return buildCompressorTelemetryObject(compressor);
}

JsonRpcProtocol::WriteCallback JsonRpcProtocol::writeCallback;
JsonRpcProtocol::OpenFileCallback JsonRpcProtocol::openFileCallback = nullptr;
JsonRpcProtocol::OpenSourceCallback JsonRpcProtocol::openSourceCallback = nullptr;
JsonRpcProtocol::MixBalanceCallback JsonRpcProtocol::mixBalanceCallback = nullptr;
void JsonRpcProtocol::setMixBalanceCallback(MixBalanceCallback callback) { mixBalanceCallback = std::move(callback); }
JsonRpcProtocol::PauseCallback JsonRpcProtocol::pauseCallback;
JsonRpcProtocol::SeekCallback JsonRpcProtocol::seekCallback;
JsonRpcProtocol::StopCallback JsonRpcProtocol::stopCallback;
JsonRpcProtocol::PrefetchCallback JsonRpcProtocol::prefetchCallback;
JsonRpcProtocol::GaplessPrepareCallback JsonRpcProtocol::gaplessPrepareCallback;
JsonRpcProtocol::AutomixPrepareCallback JsonRpcProtocol::automixPrepareCallback;
JsonRpcProtocol::AutomixCancelCallback JsonRpcProtocol::automixCancelCallback;
JsonRpcProtocol::AutomixStateCallback JsonRpcProtocol::automixStateCallback;
JsonRpcProtocol::VolumeCallback JsonRpcProtocol::volumeCallback;
JsonRpcProtocol::PlaybackRateCallback JsonRpcProtocol::playbackRateCallback;
JsonRpcProtocol::QueueSetCallback JsonRpcProtocol::queueSetCallback;
JsonRpcProtocol::QueueClearCallback JsonRpcProtocol::queueClearCallback;

void JsonRpcProtocol::setWriteCallback(WriteCallback callback)
{
    writeCallback = std::move(callback);
}

void JsonRpcProtocol::setOpenFileCallback(OpenFileCallback callback)
{
    openFileCallback = callback;
}

void JsonRpcProtocol::setOpenSourceCallback(OpenSourceCallback callback)
{
    openSourceCallback = callback;
}

void JsonRpcProtocol::setPauseCallback(PauseCallback cb) { pauseCallback = cb; }
void JsonRpcProtocol::setSeekCallback(SeekCallback cb) { seekCallback = cb; }
void JsonRpcProtocol::setStopCallback(StopCallback cb) { stopCallback = cb; }
void JsonRpcProtocol::setPrefetchCallback(PrefetchCallback cb) { prefetchCallback = cb; }
void JsonRpcProtocol::setGaplessPrepareCallback(GaplessPrepareCallback cb) { gaplessPrepareCallback = std::move(cb); }
void JsonRpcProtocol::setAutomixPrepareCallback(AutomixPrepareCallback cb) { automixPrepareCallback = std::move(cb); }
void JsonRpcProtocol::setAutomixCancelCallback(AutomixCancelCallback cb) { automixCancelCallback = std::move(cb); }
void JsonRpcProtocol::setAutomixStateCallback(AutomixStateCallback cb) { automixStateCallback = std::move(cb); }
void JsonRpcProtocol::setVolumeCallback(VolumeCallback cb) { volumeCallback = cb; }
void JsonRpcProtocol::setPlaybackRateCallback(PlaybackRateCallback cb) { playbackRateCallback = std::move(cb); }
void JsonRpcProtocol::setQueueSetCallback(QueueSetCallback cb) { queueSetCallback = std::move(cb); }
void JsonRpcProtocol::setQueueClearCallback(QueueClearCallback cb) { queueClearCallback = std::move(cb); }

std::string JsonRpcProtocol::createJsonRpcResponse(int id, const nlohmann::json& result)
{
    nlohmann::json obj;
    obj["jsonrpc"] = "2.0";
    obj["result"] = result;
    obj["id"] = id;
    return obj.dump();
}

std::string JsonRpcProtocol::createJsonRpcError(int id, int code, const std::string& message)
{
    nlohmann::json errorObj;
    errorObj["code"] = code;
    errorObj["message"] = message;

    nlohmann::json obj;
    obj["jsonrpc"] = "2.0";
    obj["error"] = errorObj;
    obj["id"] = id;
    return obj.dump();
}

std::string JsonRpcProtocol::createJsonRpcNotification(const std::string& method, const nlohmann::json& params)
{
    nlohmann::json obj;
    obj["jsonrpc"] = "2.0";
    obj["method"] = method;
    obj["params"] = params;
    return obj.dump();
}

std::string JsonRpcProtocol::handleJsonLine(
    const std::string& line,
    EqProcessor& eq, ChannelBalanceProcessor& cb, ConvolutionProcessor& conv,
    DspHeadroomProcessor& headroom, ReplayGainProcessor& rg, CompressorProcessor& compressor,
    SpatialDspProcessor& spatialDsp,
    PlaybackRateProcessor& rate, LevelMeterProcessor& meter, DspRackOrder& rackOrder,
    WorkshopAudioEffectController& workshopAudioEffect,
    MultibandCompressorProcessor& multiband, DeEsserProcessor& deEsser,
    EqPresetStore& /*presets*/, bool proDspAllowed)
{
    nlohmann::json parsed;
    try
    {
        parsed = nlohmann::json::parse(line);
    }
    catch (const nlohmann::json::parse_error&)
    {
        return createJsonRpcError(0, -32700, "Parse error");
    }

    if (!parsed.is_object())
        return createJsonRpcError(0, -32700, "Parse error");

    if (!parsed.contains("jsonrpc"))
        return createJsonRpcError(0, -32600, "Invalid Request: missing jsonrpc");

    const auto& methodValue = parsed["method"];
    if (!methodValue.is_string())
        return createJsonRpcError(0, -32600, "Invalid Request: missing method");

    const auto method = methodValue.get<std::string>();
    const auto& params = parsed.value("params", nlohmann::json());
    const bool hasId = parsed.contains("id");
    const int id = hasId ? parsed["id"].get<int>() : 0;

    if (!hasId)
        return std::string();

    if (! proDspAllowed && requestRequiresProDsp(method, params))
        return createJsonRpcError(id, -32003, "ECHO Pro audio entitlement required");

    // ── EQ Methods ──

    // All signal-mutating commands invalidate observed programme measurements.
    // Reads and UI-only meter configuration leave the current window intact.
    if (method.find(".set") != std::string::npos || method.find(".reset") != std::string::npos
        || method.find(".load") != std::string::npos || method.find(".clear") != std::string::npos)
        meter.dspMeasurements.invalidate();

    if (method == "eq.getState")
    {
        eq.requestLoudnessMeasurement();
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.setEnabled")
    {
        eq.setEnabled(getParamsBool(params, false));
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.audition")
    {
        const auto* object = getParamsObject(params);
        if (!object) return createJsonRpcError(id, -32602, "Invalid audition params");
        const bool active = getBool(*object, "active", false);
        const float frequency = getNumber(*object, "frequencyHz", 1000.0f);
        const float q = getNumber(*object, "q", 1.0f);
        if (!eq.setAudition(active, frequency, q))
            return createJsonRpcError(id, -32602, "Invalid audition frequency or Q");
        return createJsonRpcResponse(id, nlohmann::json::object({
            {"active", eq.isAuditionActive()}, {"frequencyHz", frequency}, {"q", q}
        }));
    }

    if (method == "eq.setBandGain")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid params");
        const int band = getInt(*paramsObj, "band", -1);
        if (!eq.setBandGainDb(band, getNumber(*paramsObj, "gainDb", 0.0f)))
            return createJsonRpcError(id, -32000, "Invalid EQ band index");
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.setBandFrequency")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid params");
        const int band = getInt(*paramsObj, "band", -1);
        if (!eq.setBandFrequencyHz(band, getNumber(*paramsObj, "frequencyHz", eqFrequenciesHz[0])))
            return createJsonRpcError(id, -32000, "Invalid EQ band index");
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.setBandQ")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid params");
        const int band = getInt(*paramsObj, "band", -1);
        if (!eq.setBandQ(band, getNumber(*paramsObj, "q", 1.0f)))
            return createJsonRpcError(id, -32000, "Invalid EQ band index");
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.setBandFilterType")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid params");
        const int band = getInt(*paramsObj, "band", -1);
        const auto filterTypeStr = getString(*paramsObj, "filterType");
        if (!isEqFilterTypeText(filterTypeStr))
            return createJsonRpcError(id, -32000, "Invalid filter type");
        if (!eq.setBandFilterType(band, parseEqFilterType(filterTypeStr, EqFilterType::Peaking)))
            return createJsonRpcError(id, -32000, "Invalid EQ band index");
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.setBandEnabled")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid params");
        const int band = getInt(*paramsObj, "band", -1);
        if (!eq.setBandEnabled(band, getBool(*paramsObj, "enabled", true)))
            return createJsonRpcError(id, -32000, "Invalid EQ band index");
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.setPreamp")
    {
        eq.setPreampDb(getParamsNumber(params, 0.0f));
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    if (method == "eq.setPreset")
    {
        const auto presetId = getParamsString(params);
        eq.setPreampDb(0.0f);
        const auto presets = EqPresetStore::createBuiltInPresets();
        for (const auto& preset : presets)
        {
            if (preset.id == presetId)
            {
                for (size_t i = 0; i < preset.bands.size() && i < static_cast<size_t>(eqBandCount); ++i)
                {
                    const auto& band = preset.bands[i];
                    eq.setBandFrequencyHz(static_cast<int>(i), band.frequencyHz);
                    eq.setBandGainDb(static_cast<int>(i), band.gainDb);
                    eq.setBandFilterType(static_cast<int>(i), band.filterType);
                    eq.setBandQ(static_cast<int>(i), band.q);
                    eq.setBandEnabled(static_cast<int>(i), band.enabled);
                    eq.setBandProcessing(
                        static_cast<int>(i),
                        band.scope,
                        band.dynamicMode,
                        band.dynamicThresholdDb,
                        band.dynamicRatio,
                        band.dynamicAttackMs,
                        band.dynamicReleaseMs);
                }
                for (int bandIndex = static_cast<int>(preset.bands.size()); bandIndex < eqBandCount; ++bandIndex)
                {
                    eq.setBandProcessing(
                        bandIndex,
                        EqBandScope::Stereo,
                        EqDynamicMode::Off,
                        eqDynamicThresholdDefaultDb,
                        eqDynamicRatioDefault,
                        eqDynamicAttackDefaultMs,
                        eqDynamicReleaseDefaultMs);
                }
                eq.setPreampDb(preset.preampDb);
                eq.setTiltDb(preset.tiltDb);
                return createJsonRpcResponse(id, buildEqState(eq));
            }
        }
        return createJsonRpcError(id, -32003, "Preset not found");
    }

    if (method == "eq.reset")
    {
        eq.resetFlat();
        return createJsonRpcResponse(id, buildEqState(eq));
    }

    // ── DSP Methods ──

    if (method == "dsp.getState") {
        meter.dspMeasurements.requestLoudness();
        auto result = buildDspState(headroom);
        result["measurement"] = dspMeasurementJson(meter.dspMeasurements.read());
        return createJsonRpcResponse(id, result);
    }

    if (method == "dsp.setHeadroom")
    {
        headroom.setHeadroomDb(getParamsNumber(params, 0.0f));
        return createJsonRpcResponse(id, buildDspState(headroom));
    }

    if (method == "dsp.setSafetyLimiter")
    {
        DspChain::setSafetyLimiterEnabled(getParamsBool(params, true));
        return createJsonRpcResponse(id, buildDspState(headroom));
    }

    if (method == "dsp.setLimiterCeiling")
    {
        const bool clear = params.is_null()
            || (params.is_array() && !params.empty() && params[0].is_null());
        if (clear)
            DspChain::setLimiterCeilingDb(std::numeric_limits<float>::quiet_NaN());
        else
            DspChain::setLimiterCeilingDb(getParamsNumber(params, std::numeric_limits<float>::quiet_NaN()));
        return createJsonRpcResponse(id, buildDspState(headroom));
    }

    if (method == "dsp.setLimiterRelease")
    {
        DspChain::setLimiterReleaseMs(getParamsNumber(params, dspLimiterReleaseDefaultMs));
        return createJsonRpcResponse(id, buildDspState(headroom));
    }

    if (method == "dspRack.getState")
        return createJsonRpcResponse(id, buildDspRackState(rackOrder, compressor, multiband, deEsser, spatialDsp));

    if (method == "dspRack.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr || !paramsObj->contains("order") || !(*paramsObj)["order"].is_array())
            return createJsonRpcError(id, -32602, "dspRack.setState requires an order array");

        std::vector<DspRackModuleId> order;
        for (const auto& item : (*paramsObj)["order"])
        {
            if (!item.is_string())
                return createJsonRpcError(id, -32602, "DSP rack module IDs must be strings");
            DspRackModuleId module;
            if (!DspRackOrder::parseModuleId(item.get<std::string>(), module))
                return createJsonRpcError(id, -32004, "Unsupported DSP rack module");
            order.push_back(module);
        }

        // Validate the entire transaction before publishing either part.
        if (order.size() == DspRackOrder::moduleCount - 1
            && std::find(order.begin(), order.end(), DspRackModuleId::Procedural) == order.end())
            order.insert(order.begin() + std::min<size_t>(1, order.size()), DspRackModuleId::Procedural);
        auto sorted = order;
        std::sort(sorted.begin(), sorted.end());
        if (order.size() != DspRackOrder::moduleCount || std::adjacent_find(sorted.begin(), sorted.end()) != sorted.end())
            return createJsonRpcError(id, -32004, "Invalid DSP rack order");
        std::string error;
        ProceduralDspController candidateProcedural;
        if (paramsObj->contains("procedural") && !candidateProcedural.setState((*paramsObj)["procedural"], error))
            return createJsonRpcError(id, -32602, error);
        nlohmann::json baseOrder = nlohmann::json::array();
        for (auto m : order) baseOrder.push_back(DspRackOrder::moduleIdText(m));
        const auto* filters = paramsObj->contains("filters") ? &(*paramsObj)["filters"] : nullptr;
        const auto* sequence = paramsObj->contains("sequence") ? &(*paramsObj)["sequence"] : nullptr;
        if (!rackOrder.filters.setState(baseOrder, filters, sequence, error))
            return createJsonRpcError(id, -32602, error);
        if (paramsObj->contains("procedural")) rackOrder.procedural.setState((*paramsObj)["procedural"], error);
        if (!rackOrder.setOrder(order, error, false))
            return createJsonRpcError(id, -32004, error);
        return createJsonRpcResponse(id, buildDspRackState(rackOrder, compressor, multiband, deEsser, spatialDsp));
    }

    if (method == "workshopAudioEffect.getState")
        return createJsonRpcResponse(id, buildWorkshopAudioEffectState(workshopAudioEffect));

    if (method == "workshopAudioEffect.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "workshopAudioEffect.setState requires a state object");
        const auto effect = getString(*paramsObj, "effect");
        if (!effect.empty() && effect != "bitcrusher" && effect != "chiptune" && effect != "vocalCut")
            return createJsonRpcError(id, -32004, "Unsupported Workshop audio effect");

        auto state = workshopAudioEffect.getState();
        if (effect == "bitcrusher")
            state.effect = WorkshopAudioEffectType::Bitcrusher;
        else if (effect == "chiptune")
            state.effect = WorkshopAudioEffectType::Chiptune;
        else if (effect == "vocalCut")
            state.effect = WorkshopAudioEffectType::VocalCut;
        state.enabled = getBool(*paramsObj, "enabled", state.enabled);
        state.bitDepth = getInt(*paramsObj, "bitDepth", state.bitDepth);
        state.sampleRateHz = getInt(*paramsObj, "sampleRateHz", state.sampleRateHz);
        state.mix = getNumber(*paramsObj, "mix", state.mix);
        state.outputGainDb = getNumber(*paramsObj, "outputGainDb", state.outputGainDb);
        state.pulseMix = getNumber(*paramsObj, "pulseMix", state.pulseMix);
        state.triangleMix = getNumber(*paramsObj, "triangleMix", state.triangleMix);
        state.noiseMix = getNumber(*paramsObj, "noiseMix", state.noiseMix);
        state.drive = getNumber(*paramsObj, "drive", state.drive);
        state.vocalCutStrength = getNumber(*paramsObj, "vocalCutStrength", state.vocalCutStrength);
        state.vocalCutBassPreserveHz = getNumber(
            *paramsObj, "vocalCutBassPreserveHz", state.vocalCutBassPreserveHz);
        std::string error;
        if (!workshopAudioEffect.setState(state, &error))
            return createJsonRpcError(id, -32602, error);
        return createJsonRpcResponse(id, buildWorkshopAudioEffectState(workshopAudioEffect));
    }

    if (method == "compressor.getState")
        return createJsonRpcResponse(id, buildCompressorState(compressor));

    if (method == "compressor.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "compressor.setState requires a state object");
        compressor.setState(readCompressorState(*paramsObj, compressor.getState()));
        return createJsonRpcResponse(id, buildCompressorState(compressor));
    }

    if (method == "multiband.getState")
        return createJsonRpcResponse(id, buildMultibandState(multiband));

    if (method == "multiband.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "multiband.setState requires a state object");
        multiband.setState(readMultibandState(*paramsObj, multiband.getState()));
        return createJsonRpcResponse(id, buildMultibandState(multiband));
    }

    if (method == "deesser.getState")
        return createJsonRpcResponse(id, buildDeEsserState(deEsser));

    if (method == "deesser.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "deesser.setState requires a state object");
        deEsser.setState(readDeEsserState(*paramsObj, deEsser.getState()));
        return createJsonRpcResponse(id, buildDeEsserState(deEsser));
    }

    if (method == "deesser.setListen")
    {
        deEsser.setListen(getParamsBool(params, false));
        return createJsonRpcResponse(id, nlohmann::json::object({ {"listening", deEsser.isListening()} }));
    }

    if (method == "crossfeed.getState")
        return createJsonRpcResponse(id, buildCrossfeedState(spatialDsp));
    if (method == "crossfeed.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "crossfeed.setState requires a state object");
        spatialDsp.setCrossfeedState(readCrossfeedState(*paramsObj, spatialDsp.getCrossfeedState()));
        return createJsonRpcResponse(id, buildCrossfeedState(spatialDsp));
    }

    if (method == "virtualStage.getState")
        return createJsonRpcResponse(id, buildVirtualStageState(spatialDsp));
    if (method == "virtualStage.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "virtualStage.setState requires a state object");
        spatialDsp.setVirtualStageState(readVirtualStageState(*paramsObj, spatialDsp.getVirtualStageState()));
        return createJsonRpcResponse(id, buildVirtualStageState(spatialDsp));
    }

    if (method == "stereoField.getState")
        return createJsonRpcResponse(id, buildStereoFieldState(spatialDsp));
    if (method == "stereoField.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "stereoField.setState requires a state object");
        spatialDsp.setStereoFieldState(readStereoFieldState(*paramsObj, spatialDsp.getStereoFieldState()));
        return createJsonRpcResponse(id, buildStereoFieldState(spatialDsp));
    }

    if (method == "channelMatrix.getState")
        return createJsonRpcResponse(id, buildChannelMatrixState(spatialDsp));
    if (method == "channelMatrix.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "channelMatrix.setState requires a state object");
        spatialDsp.setChannelMatrixState(readChannelMatrixState(*paramsObj, spatialDsp.getChannelMatrixState()));
        return createJsonRpcResponse(id, buildChannelMatrixState(spatialDsp));
    }

    // ── Channel Balance Methods ──

    if (method == "channelBalance.getState")
        return createJsonRpcResponse(id, buildChannelBalanceState(cb));

    if (method == "channelBalance.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj != nullptr)
        {
            const auto nextState = readChannelBalanceState(*paramsObj, cb.getState());
            cb.setState(nextState);
        }
        return createJsonRpcResponse(id, buildChannelBalanceState(cb));
    }

    if (method == "channelBalance.reset")
    {
        cb.resetToDefault();
        return createJsonRpcResponse(id, buildChannelBalanceState(cb));
    }

    // ── Room Correction Methods ──

    if (method == "roomCorrection.getState")
        return createJsonRpcResponse(id, buildRoomCorrectionState(conv));

    if (method == "roomCorrection.loadIr")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid params");
        const auto path = getString(*paramsObj, "path");
        const auto irId = getString(*paramsObj, "irId");
        const auto irName = getString(*paramsObj, "irName");
        const auto identityToken = paramsObj->contains("identity")
            ? (*paramsObj)["identity"].dump()
            : std::string();
        const bool forceReload = getBool(*paramsObj, "forceReload", false);
        if (path.empty())
            return createJsonRpcError(id, -32005, "Missing IR path");
        conv.loadImpulseResponse(
            path,
            irId,
            irName.empty() ? "Room correction IR" : irName,
            identityToken,
            forceReload);
        return createJsonRpcResponse(id, buildRoomCorrectionState(conv));
    }

    if (method == "roomCorrection.setEnabled")
    {
        conv.setEnabled(getParamsBool(params, false));
        return createJsonRpcResponse(id, buildRoomCorrectionState(conv));
    }

    if (method == "roomCorrection.setTrim")
    {
        conv.setTrimDb(getParamsNumber(params, 0.0f));
        return createJsonRpcResponse(id, buildRoomCorrectionState(conv));
    }

    if (method == "roomCorrection.clear")
    {
        conv.clearImpulseResponse();
        return createJsonRpcResponse(id, buildRoomCorrectionState(conv));
    }

    // ── ReplayGain Methods ──

    if (method == "replayGain.getConfig")
        return createJsonRpcResponse(id, buildReplayGainConfig(rg));

    if (method == "replayGain.setConfig")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj != nullptr)
        {
            ReplayGainConfig config;
            config.trackGainDb = getNumber(*paramsObj, "trackGainDb", 0.0f);
            config.albumGainDb = getNumber(*paramsObj, "albumGainDb", 0.0f);
            config.peak = getNumber(*paramsObj, "peak", 1.0f);
            config.mode = getInt(*paramsObj, "mode", replayGainModeOff);
            config.preampDb = getNumber(*paramsObj, "preampDb", 0.0f);
            config.preventClipping = getBool(*paramsObj, "preventClipping", true);
            rg.setConfig(config);
            rg.setTrackPreparationRequired(getBool(*paramsObj, "requireTrackPreparation", false));
            std::vector<float> sequenceGains;
            const auto sequence = paramsObj->find("sequenceGainsDb");
            if (sequence != paramsObj->end() && sequence->is_array()) {
                for (const auto& value : *sequence) {
                    if (sequenceGains.size() >= 32) break;
                    sequenceGains.push_back(value.is_number() ? value.get<float>() : 0.0f);
                }
            }
            rg.setSequenceGains(sequenceGains);
            if (getBool(*paramsObj, "resetSequenceIndex", false)) rg.selectSequenceIndex(0);
        }
        return createJsonRpcResponse(id, buildReplayGainConfig(rg));
    }

    // ── PlaybackRate Methods ──

    if (method == "playbackRate.configure")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr
            || ! paramsObj->contains("rate")
            || ! (*paramsObj)["rate"].is_number()
            || ! paramsObj->contains("mode")
            || ! (*paramsObj)["mode"].is_string())
            return createJsonRpcError(id, -32602, "Invalid playback rate configuration");

        const float requestedRate = (*paramsObj)["rate"].get<float>();
        const std::string modeText = (*paramsObj)["mode"].get<std::string>();
        if (! std::isfinite(requestedRate)
            || requestedRate < playbackRateMin
            || requestedRate > playbackRateMax
            || (modeText != "nightcore" && modeText != "daycore" && modeText != "speed"))
            return createJsonRpcError(id, -32602, "Invalid playback rate configuration");

        std::string error;
        const auto requestedMode = parseSpeedMode(modeText, SpeedMode::Nightcore);
        if (! rate.configure(requestedRate, requestedMode, &error))
            return createJsonRpcError(id, -32004, error);
        if (playbackRateCallback)
            playbackRateCallback(rate.getRate(), rate.getSpeedMode());

        nlohmann::json obj;
        obj["rate"] = rate.getRate();
        obj["mode"] = speedModeText(rate.getSpeedMode());
        return createJsonRpcResponse(id, obj);
    }

    if (method == "playbackRate.setRate")
    {
        std::string error;
        if (! rate.setRate(getParamsNumber(params, 1.0f), &error))
            return createJsonRpcError(id, -32004, error);
        if (playbackRateCallback)
            playbackRateCallback(rate.getRate(), rate.getSpeedMode());
        nlohmann::json obj;
        obj["rate"] = rate.getRate();
        obj["mode"] = speedModeText(rate.getSpeedMode());
        return createJsonRpcResponse(id, obj);
    }

    if (method == "playbackRate.setMode")
    {
        const auto modeStr = getParamsString(params);
        if (modeStr != "nightcore" && modeStr != "daycore" && modeStr != "speed")
            return createJsonRpcError(id, -32602, "Invalid playback rate mode");
        std::string error;
        if (! rate.setSpeedMode(parseSpeedMode(modeStr, SpeedMode::Nightcore), &error))
            return createJsonRpcError(id, -32004, error);
        if (playbackRateCallback)
            playbackRateCallback(rate.getRate(), rate.getSpeedMode());
        nlohmann::json obj;
        obj["rate"] = rate.getRate();
        obj["mode"] = speedModeText(rate.getSpeedMode());
        return createJsonRpcResponse(id, obj);
    }

    // ── LevelMeter Methods ──

    if (method == "levelMeter.setInterval")
    {
        meter.setIntervalMs(getParamsInt(params, 100));
        nlohmann::json obj;
        obj["intervalMs"] = meter.getIntervalMs();
        return createJsonRpcResponse(id, obj);
    }

    if (method == "levelMeter.setVisualSpectrumEnabled")
    {
        meter.setVisualSpectrumEnabled(getParamsBool(params, false));
        nlohmann::json obj;
        obj["enabled"] = meter.isVisualSpectrumEnabled();
        return createJsonRpcResponse(id, obj);
    }

    // ── Preset Methods ──

    if (method == "preset.list")
        return createJsonRpcResponse(id, buildPresetList());

    if (method == "preset.save")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj != nullptr)
        {
            EqPreset preset;
            preset.id = getString(*paramsObj, "id");
            preset.name = getString(*paramsObj, "name");
            preset.preampDb = getNumber(*paramsObj, "preampDb", 0.0f);
            preset.readonlyPreset = false;
            if (EqPresetStore::validatePreset(preset))
            {
                nlohmann::json obj;
                obj["id"] = preset.id;
                obj["name"] = preset.name;
                obj["preampDb"] = preset.preampDb;
                obj["readonly"] = false;
                return createJsonRpcResponse(id, obj);
            }
        }
        return createJsonRpcError(id, -32003, "Invalid preset data");
    }

    if (method == "preset.delete")
    {
        return createJsonRpcResponse(id, buildPresetList());
    }

    // ── Profile Methods ──
    // Profile data is managed by Electron's EqStateStore.
    // Native host only provides DSP application, not profile storage.

    if (method == "profile.list")
    {
        return createJsonRpcResponse(id, nlohmann::json::array());
    }

    if (method == "profile.save")
    {
        return createJsonRpcResponse(id, params);
    }

    if (method == "profile.apply")
    {
        return createJsonRpcResponse(id, nlohmann::json::object());
    }

    if (method == "profile.delete")
    {
        return createJsonRpcResponse(id, nlohmann::json::array());
    }

    if (method == "profile.bind")
    {
        nlohmann::json obj;
        obj["profileId"] = "";
        obj["target"] = params;
        return createJsonRpcResponse(id, obj);
    }

    if (method == "profile.getBinding")
    {
        return createJsonRpcResponse(id, nullptr);
    }

    if (method == "profile.applyBound")
    {
        return createJsonRpcResponse(id, nlohmann::json::object());
    }

    if (method == "eq.syncState")
    {
        return createJsonRpcResponse(id, nlohmann::json::object({
            {"enabled", eq.isEnabled()},
            {"preampDb", eq.getState().preampDb}
        }));
    }

    if (method == "headphoneCorrection.getState")
        return createJsonRpcResponse(id, buildEqState(rackOrder.headphoneCorrection));

    if (method == "eq.setState" || method == "headphoneCorrection.setState")
    {
        const auto* paramsObj = getParamsObject(params);
        const bool correction = method == "headphoneCorrection.setState";
        EqProcessor& stateProcessor = correction ? rackOrder.headphoneCorrection : eq;
        if (correction && (paramsObj == nullptr || !validHeadphoneCorrectionProfile(*paramsObj)))
            return createJsonRpcError(id, -32602, "Headphone correction requires a complete supported profile; parameters are never approximated");
        if (paramsObj != nullptr)
        {
            EqState state = stateProcessor.getState();
            state.enabled = getBool(*paramsObj, "enabled", state.enabled);
            state.preampDb = static_cast<float>(getNumber(*paramsObj, "preampDb", static_cast<double>(state.preampDb)));
            state.strengthPercent = getNumber(*paramsObj, "strengthPercent", 100.0f);
            state.tiltDb = getNumber(*paramsObj, "tiltDb", state.tiltDb);

            if (paramsObj->contains("bands") && (*paramsObj)["bands"].is_array())
            {
                const auto& bands = (*paramsObj)["bands"];
                int bandIndex = 0;
                for (const auto& band : bands)
                {
                    if (bandIndex >= eqBandCount) break;
                    state.bandEnabled[static_cast<size_t>(bandIndex)] = band.value("enabled", true);
                    state.bandFilterTypes[static_cast<size_t>(bandIndex)] = parseEqFilterType(band.value("filterType", "peaking"), EqFilterType::Peaking);
                    state.bandFrequenciesHz[static_cast<size_t>(bandIndex)] = static_cast<float>(band.value("frequencyHz", 1000.0));
                    state.bandGainsDb[static_cast<size_t>(bandIndex)] = static_cast<float>(band.value("gainDb", 0.0));
                    state.bandQ[static_cast<size_t>(bandIndex)] = static_cast<float>(band.value("q", 1.0));
                    state.bandScopes[static_cast<size_t>(bandIndex)] = parseEqBandScope(band.value("scope", std::string("stereo")));
                    state.bandDynamicModes[static_cast<size_t>(bandIndex)] = parseEqDynamicMode(band.value("dynamicMode", std::string("off")));
                    state.bandDynamicThresholdDb[static_cast<size_t>(bandIndex)] = static_cast<float>(band.value("dynamicThresholdDb", static_cast<double>(eqDynamicThresholdDefaultDb)));
                    state.bandDynamicRatio[static_cast<size_t>(bandIndex)] = static_cast<float>(band.value("dynamicRatio", static_cast<double>(eqDynamicRatioDefault)));
                    state.bandDynamicAttackMs[static_cast<size_t>(bandIndex)] = static_cast<float>(band.value("dynamicAttackMs", static_cast<double>(eqDynamicAttackDefaultMs)));
                    state.bandDynamicReleaseMs[static_cast<size_t>(bandIndex)] = static_cast<float>(band.value("dynamicReleaseMs", static_cast<double>(eqDynamicReleaseDefaultMs)));
                    ++bandIndex;
                }
            }
            stateProcessor.setState(state);
        }
        return createJsonRpcResponse(id, buildEqState(stateProcessor));
    }

    if (method == "rpc.shutdown")
    {
        return createJsonRpcResponse(id, "ok");
    }

    // ── Audio Playback Control ──

    if (method == "audio.pause") {
        const auto* paramsObj = getParamsObject(params);
        const uint64_t expectedOperationId = static_cast<uint64_t>(std::max(0, paramsObj ? getInt(*paramsObj, "expectedOperationId", 0) : 0));
        return createJsonRpcResponse(id, !pauseCallback || pauseCallback(true, expectedOperationId));
    }
    if (method == "audio.resume") {
        const auto* paramsObj = getParamsObject(params);
        const uint64_t expectedOperationId = static_cast<uint64_t>(std::max(0, paramsObj ? getInt(*paramsObj, "expectedOperationId", 0) : 0));
        return createJsonRpcResponse(id, !pauseCallback || pauseCallback(false, expectedOperationId));
    }
    if (method == "audio.play") {
        const auto* paramsObj = getParamsObject(params);
        const uint64_t expectedOperationId = static_cast<uint64_t>(std::max(0, paramsObj ? getInt(*paramsObj, "expectedOperationId", 0) : 0));
        return createJsonRpcResponse(id, !pauseCallback || pauseCallback(false, expectedOperationId));
    }

    if (method == "audio.setVolume") {
        const auto* paramsObj = getParamsObject(params);
        float volume = paramsObj ? getNumber(*paramsObj, "volume", 1.0f) : 1.0f;
        if (!std::isfinite(volume))
            volume = 1.0f;
        volume = std::max(0.0f, std::min(1.0f, volume));
        if (volumeCallback) volumeCallback(volume);
        nlohmann::json obj;
        obj["volume"] = volume;
        return createJsonRpcResponse(id, obj);
    }

    if (method == "audio.seek") {
        const auto* paramsObj = getParamsObject(params);
        double pos = paramsObj ? getNumber(*paramsObj, "positionSeconds", 0.0) : 0.0;
        const uint64_t expectedOperationId = static_cast<uint64_t>(std::max(0, paramsObj ? getInt(*paramsObj, "expectedOperationId", 0) : 0));
        nlohmann::json result;
        bool ok = seekCallback ? seekCallback(pos, expectedOperationId, result) : false;
        return ok ? createJsonRpcResponse(id, result.is_null() ? nlohmann::json(true) : result) : createJsonRpcError(id, -32000, "seek failed");
    }

    if (method == "audio.stop") {
        const auto* paramsObj = getParamsObject(params);
        const uint64_t expectedOperationId = static_cast<uint64_t>(std::max(0, paramsObj ? getInt(*paramsObj, "expectedOperationId", 0) : 0));
        nlohmann::json result;
        const bool accepted = !stopCallback || stopCallback(expectedOperationId, result);
        return accepted
            ? createJsonRpcResponse(id, result.is_null() ? nlohmann::json(true) : result)
            : createJsonRpcError(id, -32002, "stale playback operation");
    }

    if (method == "audio.prefetch") {
        const auto* paramsObj = getParamsObject(params);
        std::string filePath = paramsObj ? getString(*paramsObj, "filePath") : "";
        int sr = paramsObj ? getInt(*paramsObj, "sampleRate", 0) : 0;
        bool ok = prefetchCallback ? prefetchCallback(filePath, sr) : false;
        return ok ? createJsonRpcResponse(id, true) : createJsonRpcError(id, -32000, "prefetch failed");
    }

    // ── Queue Methods ──

    if (method == "queue.set" && queueSetCallback) {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid queue snapshot");
        const auto items = paramsObj->contains("items") && (*paramsObj)["items"].is_array()
            ? (*paramsObj)["items"] : nlohmann::json::array();
        const std::string repeatMode = getString(*paramsObj, "repeatMode");
        const uint64_t revision = static_cast<uint64_t>(std::max(0, getInt(*paramsObj, "revision", 0)));
        const std::string currentItemId = getString(*paramsObj, "currentItemId");
        const uint64_t cursorOperationId = static_cast<uint64_t>(std::max(0, getInt(*paramsObj, "cursorOperationId", 0)));
        if (revision == 0 || !queueSetCallback(items, repeatMode, revision, currentItemId, cursorOperationId))
            return createJsonRpcError(id, -32000, "queue snapshot rejected");
        return createJsonRpcResponse(id, {{"queueRevision", revision}});
    }

    if (method == "queue.clear" && queueClearCallback) {
        queueClearCallback();
        return createJsonRpcResponse(id, nlohmann::json::object());
    }

    // ── Audio File Methods ──

    if (method == "audio.mixBalance")
    {
        const auto* request = getParamsObject(params);
        if (!request || !mixBalanceCallback) return createJsonRpcError(id, -32602, "Mix unavailable");
        nlohmann::json result;
        try {
            return mixBalanceCallback(*request, result)
                ? createJsonRpcResponse(id, result)
                : createJsonRpcError(id, -32000, "Mix unavailable or stale");
        } catch (...) { return createJsonRpcError(id, -32000, "Mix control failed"); }
    }
    if (method == "audio.openSource")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr || !paramsObj->contains("source") || !(*paramsObj)["source"].is_object())
            return createJsonRpcError(id, -32602, "Invalid source");
        const auto& source = (*paramsObj)["source"];
        const std::string kind = getString(source, "kind");
        const std::string uri = getString(source, "uri");
        if ((kind != "local" && kind != "http") || uri.empty())
            return createJsonRpcError(id, -32602, "Invalid source");

        double requestedStartSeconds = 0.0;
        const auto startSecondsIt = paramsObj->find("startSeconds");
        if (startSecondsIt != paramsObj->end())
        {
            if (!startSecondsIt->is_number())
                return createJsonRpcError(id, -32602, "Invalid startSeconds");
            requestedStartSeconds = startSecondsIt->get<double>();
            if (!std::isfinite(requestedStartSeconds))
                return createJsonRpcError(id, -32602, "Invalid startSeconds");
        }
        if (!openSourceCallback)
            return createJsonRpcError(id, -32601, "openSource unavailable");

        const int targetSampleRate = getInt(*paramsObj, "sampleRate", 0);
        nlohmann::json result;
        try
        {
            const bool ok = openSourceCallback(source, targetSampleRate, requestedStartSeconds, result);
            return ok
                ? createJsonRpcResponse(id, result)
                : createJsonRpcError(id, -32000, result.value("error", "openSource failed"));
        }
        catch (const std::exception& e)
        {
            return createJsonRpcError(id, -32000, e.what());
        }
    }

    if (method == "audio.gaplessPrepare") {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid gapless request");
        nlohmann::json result;
        const bool ok = gaplessPrepareCallback ? gaplessPrepareCallback(*paramsObj, result) : false;
        const std::string error = result.is_object()
            ? result.value("error", "gapless prepare failed")
            : "gapless prepare failed";
        return ok
            ? createJsonRpcResponse(id, result.is_null() ? nlohmann::json(true) : result)
            : createJsonRpcError(id, -32000, error);
    }

    if (method == "automix.prepare") {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid automix request");
        nlohmann::json result;
        const bool ok = automixPrepareCallback ? automixPrepareCallback(*paramsObj, result) : false;
        const std::string error = result.is_object()
            ? result.value("error", "automix prepare failed")
            : "automix prepare failed";
        return ok
            ? createJsonRpcResponse(id, result)
            : createJsonRpcError(id, -32000, error);
    }

    if (method == "automix.cancel") {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid automix cancel request");
        const std::string planId = getString(*paramsObj, "planId");
        if (planId.empty())
            return createJsonRpcError(id, -32602, "Missing planId");
        nlohmann::json result;
        const bool ok = automixCancelCallback ? automixCancelCallback(planId, result) : false;
        const std::string error = result.is_object()
            ? result.value("error", "automix cancel failed")
            : "automix cancel failed";
        return ok
            ? createJsonRpcResponse(id, result)
            : createJsonRpcError(id, -32000, error);
    }

    if (method == "automix.state") {
        return createJsonRpcResponse(
            id,
            automixStateCallback ? automixStateCallback() : nlohmann::json{
                {"state", "idle"},
                {"planId", nullptr},
                {"queueRevision", nullptr},
                {"operationId", nullptr},
                {"reason", "automix_unavailable"},
            });
    }

    if (method == "audio.openFile")
    {
        const auto* paramsObj = getParamsObject(params);
        if (paramsObj == nullptr)
            return createJsonRpcError(id, -32602, "Invalid params");
        std::string filePath = getString(*paramsObj, "filePath");
        if (filePath.empty())
            return createJsonRpcError(id, -32602, "Missing filePath");

        double requestedStartSeconds = 0.0;
        const auto startSecondsIt = paramsObj->find("startSeconds");
        if (startSecondsIt != paramsObj->end())
        {
            if (!startSecondsIt->is_number())
                return createJsonRpcError(id, -32602, "Invalid startSeconds");
            requestedStartSeconds = startSecondsIt->get<double>();
            if (!std::isfinite(requestedStartSeconds))
                return createJsonRpcError(id, -32602, "Invalid startSeconds");
        }

        if (openFileCallback)
        {
            const int targetSampleRate = getInt(*paramsObj, "sampleRate", 0);
            nlohmann::json result;
            try
            {
                bool ok = openFileCallback(filePath, targetSampleRate, requestedStartSeconds, result);
                if (ok)
                    return createJsonRpcResponse(id, result);
                else
                    return createJsonRpcError(id, -32000,
                        result.value("error", "openFile failed"));
            }
            catch (const std::exception& e)
            {
                return createJsonRpcError(id, -32000, e.what());
            }
        }

        // Fallback: probe-only
        try
        {
            echo::AudioProbe probe = echo::LibavDecoder::probe(filePath);
            nlohmann::json result;
            result["status"] = "probed";
            result["filePath"] = filePath;
            result["sampleRate"] = probe.sampleRate;
            result["channels"] = probe.channels;
            result["durationSeconds"] = probe.durationSeconds;
            result["startSeconds"] = normalizeStartSeconds(requestedStartSeconds, probe.durationSeconds);
            result["codec"] = probe.codec;
            result["container"] = probe.container;
            result["bitDepth"] = probe.bitDepth;
            result["bitrate"] = probe.bitrate;
            return createJsonRpcResponse(id, result);
        }
        catch (const std::exception& e)
        {
            return createJsonRpcError(id, -32000, e.what());
        }
    }

    // ── Lifecycle Methods ──

    if (method == "rpc.ping")
    {
        return createJsonRpcResponse(id, "pong");
    }

    return createJsonRpcError(id, -32601, "Method not found: " + method);
}

} // namespace echo
