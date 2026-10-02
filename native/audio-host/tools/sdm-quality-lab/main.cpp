#include "NativeFormatProcessor.h"

#include <algorithm>
#include <bit>
#include <chrono>
#include <cmath>
#include <complex>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <iterator>
#include <string>
#include <vector>

namespace
{
constexpr double pi = 3.14159265358979323846;
constexpr double signalFrequency = 1'000.0;
constexpr double signalAmplitude = 0.1;
constexpr double smallSignalAmplitude = 0.001;
constexpr double analysisCutoffHz = 20'000.0;
constexpr double imageSignalFrequency = 20'000.0;

struct ProfileSpec
{
    const char* name;
    echo::SdmQualityProfile profile;
    // Extended-compensation override for lab A/B cells: -1 keeps the profile
    // default (EC on for adaptive, off elsewhere), 0 forces EC off, 1 forces
    // EC on. Playback never uses this override.
    int forceExtendedCompensation = -1;
};

void applyProfileExtendedCompensation(echo::SdmProcessor& processor, const ProfileSpec& profile)
{
    if (profile.forceExtendedCompensation >= 0)
        processor.setExtendedCompensationEnabled(profile.forceExtendedCompensation != 0);
}

struct RateSpec
{
    const char* name;
    int transportSampleRate;
};

struct InterpolationSpec
{
    const char* name;
    echo::SdmInterpolationMode mode;
};

struct Measurement
{
    std::string rate;
    int transportSampleRate = 0;
    int nativeSampleRate = 0;
    std::string profile;
    std::string interpolation;
    std::string extraLoop;
    int modulatorOrder = 0;
    double ntfPeakGain = 0.0;
    double profileHeadroomDb = 0.0;
    double ditherAmplitude = 0.0;
    int preModulatorUpsampleFactor = 1;
    int preModulatorDelayBits = 0;
    std::string preModulatorProfile;
    double inBandResidualDb = 0.0;
    double signalOneDensity = 0.0;
    double idlePatternRatio = 0.0;
    double peakFeedbackState = 0.0;
    std::uint64_t stabilityRecoveries = 0;
    double processingMs = 0.0;
    double realtimeRatio = 0.0;
    std::uint64_t checksum = 0;
    std::uint64_t chunkedChecksum = 0;
    bool deterministic = false;
    bool chunkInvariant = false;
};

std::vector<float> createSine(
    int transportSampleRate,
    int frames,
    double frequencyHz,
    double amplitude);

void writeLe32(std::ostream& output, std::uint32_t value)
{
    for (int byte = 0; byte < 4; ++byte)
        output.put(static_cast<char>((value >> (byte * 8)) & 0xffu));
}

void writeLe64(std::ostream& output, std::uint64_t value)
{
    for (int byte = 0; byte < 8; ++byte)
        output.put(static_cast<char>((value >> (byte * 8)) & 0xffu));
}

void writeDsf(
    const std::filesystem::path& path,
    const std::vector<std::uint8_t>& bytes,
    std::uint32_t nativeSampleRate,
    std::uint64_t sampleCount)
{
    constexpr std::uint32_t blockSize = 4096;
    const std::uint64_t paddedBytes = ((bytes.size() + blockSize - 1u) / blockSize) * blockSize;
    const std::uint64_t fileSize = 28u + 52u + 12u + paddedBytes;
    std::ofstream output(path, std::ios::binary);
    if (!output)
        throw std::runtime_error("failed to create DSF comparison artifact");
    output.write("DSD ", 4); writeLe64(output, 28); writeLe64(output, fileSize); writeLe64(output, 0);
    output.write("fmt ", 4); writeLe64(output, 52);
    writeLe32(output, 1); writeLe32(output, 0); writeLe32(output, 1); writeLe32(output, 1);
    writeLe32(output, nativeSampleRate); writeLe32(output, 1); writeLe64(output, sampleCount);
    writeLe32(output, blockSize); writeLe32(output, 0);
    output.write("data", 4); writeLe64(output, 12u + paddedBytes);
    output.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    for (std::uint64_t index = bytes.size(); index < paddedBytes; ++index)
        output.put(static_cast<char>(0x69));
}

void exportComparisonArtifacts(const std::filesystem::path& directory)
{
    std::filesystem::create_directories(directory);
    struct ExportSpec
    {
        const char* name;
        int transportSampleRate;
        echo::SdmDecisionMode decisionMode;
        echo::SdmPreModulatorProfile preModulatorProfile;
    };
    const ExportSpec specs[] {
        { "current-reference-dsd512-44k", 1'411'200, echo::SdmDecisionMode::Greedy, echo::SdmPreModulatorProfile::Standard },
        { "ultimate-reference-dsd512-44k", 1'411'200, echo::SdmDecisionMode::Greedy, echo::SdmPreModulatorProfile::UltimateReference },
        { "ultimate-transient-dsd512-44k", 1'411'200, echo::SdmDecisionMode::Greedy, echo::SdmPreModulatorProfile::UltimateTransient },
        { "reference-x-dsd512-44k-lab-only", 1'411'200, echo::SdmDecisionMode::ReferenceX, echo::SdmPreModulatorProfile::Standard },
        { "ultimate-reference-dsd512-48k", 1'536'000, echo::SdmDecisionMode::Greedy, echo::SdmPreModulatorProfile::UltimateReference },
    };
    for (const auto& spec : specs)
    {
        const int frames = spec.transportSampleRate / 8;
        const auto input = createSine(
            spec.transportSampleRate, frames, signalFrequency, signalAmplitude);
        echo::SdmProcessor processor;
        processor.configure(
            1,
            echo::SdmQualityProfile::Reference,
            spec.transportSampleRate,
            echo::SdmInterpolationMode::Linear,
            spec.decisionMode,
            false,
            spec.preModulatorProfile);
        const auto output = processor.processNativeDsd(input.data(), frames);
        writeDsf(
            directory / (std::string(spec.name) + ".dsf"),
            output,
            static_cast<std::uint32_t>(spec.transportSampleRate * echo::sdmBitsPerTransportSample),
            static_cast<std::uint64_t>(frames) * echo::sdmBitsPerTransportSample);
    }
}

struct SmallSignalMeasurement
{
    std::string rate;
    int transportSampleRate = 0;
    std::string profile;
    std::string extraLoop;
    std::string stimulus;
    double frequencyHz = 0.0;
    double amplitude = 0.0;
    int preModulatorUpsampleFactor = 1;
    int preModulatorDelayBits = 0;
    double ditherAmplitude = 0.0;
    double inBandResidualDb = 0.0;
    double strongestToneHz = 0.0;
    double strongestToneDb = 0.0;
    double dominantWordRatio = 0.0;
    double oneDensity = 0.0;
    double peakFeedbackState = 0.0;
    std::uint64_t stabilityRecoveries = 0;
};

struct ImageMeasurement
{
    std::string rate;
    int transportSampleRate = 0;
    std::string profile;
    double signalHz = 0.0;
    double imageHz = 0.0;
    int preModulatorUpsampleFactor = 1;
    bool heavyPreMod = false;
    std::string preModulatorProfile;
    double signalMagnitude = 0.0;
    double imageMagnitude = 0.0;
    double imageRejectionDb = 0.0;
    double highestImageHz = 0.0;
    double highestImageMagnitude = 0.0;
    double highestImageRejectionDb = 0.0;
    std::uint64_t stabilityRecoveries = 0;
};

struct PreModulatorResponseMeasurement
{
    std::string rate;
    int transportSampleRate = 0;
    int nativeSampleRate = 0;
    int upsampleFactor = 1;
    bool heavyPreMod = false;
    std::string preModulatorProfile;
    double passbandRippleDb = 0.0;
    double response1kDb = 0.0;
    double response10kDb = 0.0;
    double response20kDb = 0.0;
    double phase1kRadians = 0.0;
    double phase10kRadians = 0.0;
    double phase20kRadians = 0.0;
    double groupDelay1kBits = 0.0;
    double groupDelay10kBits = 0.0;
    double groupDelay20kBits = 0.0;
    int impulsePeakBit = 0;
    double preRingingEnergyRatio = 0.0;
    double postRingingEnergyRatio = 0.0;
};

struct LevelSweepPoint
{
    double levelDbfs = 0.0;
    double inBandResidualDb = 0.0;
    double oneDensity = 0.0;
    double peakFeedbackState = 0.0;
    std::uint64_t stabilityRecoveries = 0;
};

struct LevelSweepMeasurement
{
    std::string rate;
    int transportSampleRate = 0;
    std::string profile;
    std::string extraLoop;
    double frequencyHz = 0.0;
    double profileHeadroomDb = 0.0;
    std::vector<LevelSweepPoint> points;
    double noiseModulationSpreadDb = 0.0;
};

const char* decisionModeName(echo::SdmDecisionMode mode)
{
    switch (mode)
    {
        case echo::SdmDecisionMode::ReferenceX: return "reference-x";
        case echo::SdmDecisionMode::Horizon: return "horizon";
        case echo::SdmDecisionMode::Extra: return "extra";
        case echo::SdmDecisionMode::Greedy:
        default: return "off";
    }
}

std::vector<float> createSine(int transportSampleRate, int frames, double frequencyHz, double amplitude)
{
    std::vector<float> signal(static_cast<std::size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
    {
        signal[static_cast<std::size_t>(frame)] = static_cast<float>(
            std::sin(2.0 * pi * frequencyHz * static_cast<double>(frame)
                / static_cast<double>(transportSampleRate))
            * amplitude);
    }
    return signal;
}

std::vector<float> createDc(int frames, double amplitude)
{
    return std::vector<float>(static_cast<std::size_t>(frames), static_cast<float>(amplitude));
}

std::uint64_t checksum(const std::vector<std::uint8_t>& bytes)
{
    std::uint64_t hash = 1469598103934665603ull;
    for (const auto byte : bytes)
    {
        hash ^= byte;
        hash *= 1099511628211ull;
    }
    return hash;
}

double oneDensity(
    const std::vector<std::uint8_t>& output,
    int startTransportFrame,
    int frameCount)
{
    const int endFrame = std::min(
        startTransportFrame + frameCount,
        static_cast<int>(output.size() / 2u));
    std::uint64_t ones = 0;
    std::uint64_t bits = 0;
    for (int frame = std::max(0, startTransportFrame); frame < endFrame; ++frame)
    {
        const auto base = static_cast<std::size_t>(frame * 2);
        ones += static_cast<std::uint64_t>(std::popcount(output[base]));
        ones += static_cast<std::uint64_t>(std::popcount(output[base + 1]));
        bits += 16;
    }
    return bits > 0 ? static_cast<double>(ones) / static_cast<double>(bits) : 0.0;
}

double idlePatternRatio(const std::vector<std::uint8_t>& output, int startTransportFrame)
{
    const int totalFrames = static_cast<int>(output.size() / 2u);
    int matchingFrames = 0;
    int measuredFrames = 0;
    for (int frame = std::max(0, startTransportFrame); frame < totalFrames; ++frame)
    {
        const auto base = static_cast<std::size_t>(frame * 2);
        matchingFrames += output[base] == 0x69u && output[base + 1] == 0x69u ? 1 : 0;
        ++measuredFrames;
    }
    return measuredFrames > 0
        ? static_cast<double>(matchingFrames) / static_cast<double>(measuredFrames)
        : 0.0;
}

double dominantWordRatio(
    const std::vector<std::uint8_t>& output,
    int startTransportFrame,
    int frameCount)
{
    const int endFrame = std::min(
        startTransportFrame + frameCount,
        static_cast<int>(output.size() / 2u));
    std::vector<int> counts(65'536, 0);
    int measuredFrames = 0;
    int dominantCount = 0;
    for (int frame = std::max(0, startTransportFrame); frame < endFrame; ++frame)
    {
        const auto word = static_cast<std::uint16_t>(
            output[static_cast<std::size_t>(frame * 2)]
            | (static_cast<std::uint16_t>(output[static_cast<std::size_t>(frame * 2 + 1)]) << 8));
        dominantCount = std::max(dominantCount, ++counts[word]);
        ++measuredFrames;
    }
    return measuredFrames > 0
        ? static_cast<double>(dominantCount) / static_cast<double>(measuredFrames)
        : 0.0;
}

void appendNativeDsdBits(
    const std::vector<std::uint8_t>& output,
    int startTransportFrame,
    int frameCount,
    std::vector<double>& bits)
{
    const int endFrame = std::min(
        startTransportFrame + frameCount,
        static_cast<int>(output.size() / 2u));
    for (int frame = std::max(0, startTransportFrame); frame < endFrame; ++frame)
    {
        for (int bit = 0; bit < 16; ++bit)
        {
            const auto byte = output[static_cast<std::size_t>(frame * 2 + bit / 8)];
            bits.push_back((byte & static_cast<std::uint8_t>(1u << (bit % 8))) != 0u ? 1.0 : -1.0);
        }
    }
}

double goertzelMagnitude(const std::vector<double>& samples, double frequencyHz, double sampleRate)
{
    if (samples.empty() || sampleRate <= 0.0)
        return 0.0;
    const double omega = 2.0 * pi * frequencyHz / sampleRate;
    const double coeff = 2.0 * std::cos(omega);
    double previous = 0.0;
    double previousPrevious = 0.0;
    for (const double sample : samples)
    {
        const double current = sample + coeff * previous - previousPrevious;
        previousPrevious = previous;
        previous = current;
    }
    const double power = previous * previous
        + previousPrevious * previousPrevious
        - coeff * previous * previousPrevious;
    return 2.0 * std::sqrt(std::max(0.0, power)) / static_cast<double>(samples.size());
}

const char* preModulatorProfileName(echo::SdmPreModulatorProfile profile)
{
    switch (profile)
    {
        case echo::SdmPreModulatorProfile::Heavy: return "heavy";
        case echo::SdmPreModulatorProfile::UltimateReference: return "ultimate-reference";
        case echo::SdmPreModulatorProfile::UltimateTransient: return "ultimate-transient";
        case echo::SdmPreModulatorProfile::Standard:
        default: return "standard";
    }
}

std::vector<double> renderPreModulator(
    const RateSpec& rate,
    const std::vector<float>& input,
    echo::SdmPreModulatorProfile profile)
{
    echo::SdmHalfbandUpsampler upsampler;
    echo::SdmUltimatePolyphaseUpsampler ultimateUpsampler;
    ultimateUpsampler.configure(1, profile);
    upsampler.configure(
        1,
        ultimateUpsampler.active()
            ? 0
            : echo::resolveSdmPreModulatorStageCount(
                rate.transportSampleRate,
                profile == echo::SdmPreModulatorProfile::Heavy));
    const int factor = ultimateUpsampler.active()
        ? ultimateUpsampler.factor()
        : std::max(1, upsampler.factor());
    const int bitsPerCarrier = echo::sdmBitsPerTransportSample / factor;
    std::vector<double> output(input.size() * echo::sdmBitsPerTransportSample, 0.0);
    double previous = 0.0;
    double carriers[echo::sdmUltimatePreModulatorFactor] {};
    std::size_t outputIndex = 0;
    for (const float inputSample : input)
    {
        if (ultimateUpsampler.active())
            ultimateUpsampler.processSample(0, static_cast<double>(inputSample), carriers);
        else
            upsampler.processSample(0, static_cast<double>(inputSample), carriers);
        for (int carrier = 0; carrier < factor; ++carrier)
        {
            const double current = carriers[carrier];
            for (int bit = 0; bit < bitsPerCarrier; ++bit)
            {
                const double phase = static_cast<double>(bit + 1) / static_cast<double>(bitsPerCarrier);
                output[outputIndex++] = previous + (current - previous) * phase;
            }
            previous = current;
        }
    }
    return output;
}

std::complex<double> impulseResponseAt(
    const std::vector<double>& impulse,
    int inputImpulseBit,
    double frequencyHz,
    double sampleRate)
{
    const double omega = 2.0 * pi * frequencyHz / sampleRate;
    std::complex<double> response { 0.0, 0.0 };
    for (std::size_t index = 0; index < impulse.size(); ++index)
    {
        const double phase = -omega * (static_cast<double>(index) - static_cast<double>(inputImpulseBit));
        response += impulse[index] * std::complex<double>(std::cos(phase), std::sin(phase));
    }
    return response;
}

double wrapPhase(double phase)
{
    while (phase > pi)
        phase -= 2.0 * pi;
    while (phase < -pi)
        phase += 2.0 * pi;
    return phase;
}

PreModulatorResponseMeasurement measurePreModulatorResponse(
    const RateSpec& rate,
    echo::SdmPreModulatorProfile profile = echo::SdmPreModulatorProfile::Standard)
{
    constexpr int impulseFrame = 256;
    constexpr int impulseFrames = 1'024;
    std::vector<float> input(impulseFrames, 0.0f);
    input[impulseFrame] = 1.0f;
    const auto impulse = renderPreModulator(rate, input, profile);
    const int nativeSampleRate = rate.transportSampleRate * echo::sdmBitsPerTransportSample;
    const int inputImpulseBit = impulseFrame * echo::sdmBitsPerTransportSample;
    const double dcMagnitude = std::max(
        std::abs(impulseResponseAt(impulse, inputImpulseBit, 0.0, nativeSampleRate)),
        1.0e-15);
    const auto magnitudeDbAt = [&](double frequencyHz)
    {
        return 20.0 * std::log10(std::max(
            std::abs(impulseResponseAt(impulse, inputImpulseBit, frequencyHz, nativeSampleRate))
                / dcMagnitude,
            1.0e-15));
    };
    const auto phaseAt = [&](double frequencyHz)
    {
        return std::arg(impulseResponseAt(impulse, inputImpulseBit, frequencyHz, nativeSampleRate));
    };
    const auto groupDelayBitsAt = [&](double frequencyHz)
    {
        constexpr double deltaHz = 10.0;
        const double lower = std::max(0.0, frequencyHz - deltaHz);
        const double upper = frequencyHz + deltaHz;
        const double phaseDelta = wrapPhase(phaseAt(upper) - phaseAt(lower));
        return -phaseDelta / (2.0 * pi * (upper - lower) / static_cast<double>(nativeSampleRate));
    };

    double minimumPassbandDb = 0.0;
    double maximumPassbandDb = 0.0;
    for (int point = 0; point <= 200; ++point)
    {
        const double magnitudeDb = magnitudeDbAt(100.0 * static_cast<double>(point));
        minimumPassbandDb = std::min(minimumPassbandDb, magnitudeDb);
        maximumPassbandDb = std::max(maximumPassbandDb, magnitudeDb);
    }
    int peakBit = 0;
    double peakMagnitude = 0.0;
    double totalEnergy = 0.0;
    for (std::size_t index = 0; index < impulse.size(); ++index)
    {
        const double magnitude = std::abs(impulse[index]);
        const double energy = impulse[index] * impulse[index];
        totalEnergy += energy;
        if (magnitude > peakMagnitude)
        {
            peakMagnitude = magnitude;
            peakBit = static_cast<int>(index) - inputImpulseBit;
        }
    }
    double preEnergy = 0.0;
    double postEnergy = 0.0;
    const int absolutePeakBit = peakBit + inputImpulseBit;
    for (std::size_t index = 0; index < impulse.size(); ++index)
    {
        const double energy = impulse[index] * impulse[index];
        if (static_cast<int>(index) < absolutePeakBit)
            preEnergy += energy;
        else if (static_cast<int>(index) > absolutePeakBit)
            postEnergy += energy;
    }

    echo::SdmHalfbandUpsampler upsampler;
    echo::SdmUltimatePolyphaseUpsampler ultimateUpsampler;
    ultimateUpsampler.configure(1, profile);
    upsampler.configure(
        1,
        ultimateUpsampler.active()
            ? 0
            : echo::resolveSdmPreModulatorStageCount(
                rate.transportSampleRate,
                profile == echo::SdmPreModulatorProfile::Heavy));
    PreModulatorResponseMeasurement result;
    result.rate = rate.name;
    result.transportSampleRate = rate.transportSampleRate;
    result.nativeSampleRate = nativeSampleRate;
    result.upsampleFactor = ultimateUpsampler.active() ? ultimateUpsampler.factor() : upsampler.factor();
    result.heavyPreMod = profile == echo::SdmPreModulatorProfile::Heavy;
    result.preModulatorProfile = preModulatorProfileName(profile);
    result.passbandRippleDb = maximumPassbandDb - minimumPassbandDb;
    result.response1kDb = magnitudeDbAt(1'000.0);
    result.response10kDb = magnitudeDbAt(10'000.0);
    result.response20kDb = magnitudeDbAt(20'000.0);
    result.phase1kRadians = phaseAt(1'000.0);
    result.phase10kRadians = phaseAt(10'000.0);
    result.phase20kRadians = phaseAt(20'000.0);
    result.groupDelay1kBits = groupDelayBitsAt(1'000.0);
    result.groupDelay10kBits = groupDelayBitsAt(10'000.0);
    result.groupDelay20kBits = groupDelayBitsAt(20'000.0);
    result.impulsePeakBit = peakBit;
    result.preRingingEnergyRatio = totalEnergy > 0.0 ? preEnergy / totalEnergy : 0.0;
    result.postRingingEnergyRatio = totalEnergy > 0.0 ? postEnergy / totalEnergy : 0.0;
    return result;
}

double measureInBandResidualDb(
    const std::vector<float>& input,
    const std::vector<std::uint8_t>& output,
    const echo::SdmProcessorConfig& configuration,
    double frequencyHz,
    double amplitude,
    bool constantStimulus)
{
    const int transportSampleRate = configuration.transportSampleRate;
    const int nativeSampleRate = transportSampleRate * 16;
    const int rateScale = std::max(1, transportSampleRate / 176'400);
    const int decimation = 64 * rateScale;
    const int tapCount = 510 * rateScale + 1;
    const int center = (tapCount - 1) / 2;
    (void)frequencyHz;
    (void)amplitude;
    (void)constantStimulus;

    echo::SdmHalfbandUpsampler idealUpsampler;
    echo::SdmUltimatePolyphaseUpsampler idealUltimateUpsampler;
    idealUltimateUpsampler.configure(1, configuration.preModulatorProfile);
    idealUpsampler.configure(
        1,
        idealUltimateUpsampler.active()
            ? 0
            : echo::resolveSdmPreModulatorStageCount(
                transportSampleRate,
                configuration.preModulatorProfile == echo::SdmPreModulatorProfile::Heavy));
    const int factor = idealUltimateUpsampler.active()
        ? idealUltimateUpsampler.factor()
        : std::max(1, idealUpsampler.factor());
    const int bitsPerCarrier = echo::sdmBitsPerTransportSample / factor;
    std::vector<double> residual(input.size() * 16u, 0.0);
    double previous = 0.0;
    double carriers[echo::sdmUltimatePreModulatorFactor] {};
    std::size_t bitIndex = 0;
    for (std::size_t frame = 0; frame < input.size(); ++frame)
    {
        const double transitionGain = std::min(
            1.0,
            static_cast<double>(frame + 1u)
                / static_cast<double>(std::max(1, configuration.transitionRampFrames)));
        const double protectedSample =
            static_cast<double>(input[frame]) * configuration.profileHeadroomGain * transitionGain;
        if (idealUltimateUpsampler.active())
            idealUltimateUpsampler.processSample(0, protectedSample, carriers);
        else
            idealUpsampler.processSample(0, protectedSample, carriers);
        int frameBit = 0;
        for (int carrier = 0; carrier < factor; ++carrier)
        {
            const double current = carriers[carrier];
            for (int bit = 0; bit < bitsPerCarrier; ++bit)
            {
                const double phase = static_cast<double>(bit + 1) / static_cast<double>(bitsPerCarrier);
                const double interpolationWeight =
                    configuration.interpolationMode == echo::SdmInterpolationMode::SmoothstepExperimental
                    ? phase * phase * (3.0 - 2.0 * phase)
                    : phase;
                const double ideal = previous + (current - previous) * interpolationWeight;
                const auto byte = output[frame * 2u + static_cast<std::size_t>(frameBit / 8)];
                const double actual = (byte & static_cast<std::uint8_t>(1u << (frameBit % 8))) != 0u ? 1.0 : -1.0;
                residual[bitIndex++] = actual - ideal;
                ++frameBit;
            }
            previous = current;
        }
    }

    std::vector<double> taps(static_cast<std::size_t>(tapCount), 0.0);
    const double normalizedCutoff = analysisCutoffHz / static_cast<double>(nativeSampleRate);
    double tapSum = 0.0;
    for (int tap = 0; tap < tapCount; ++tap)
    {
        const int offset = tap - center;
        const double sinc = offset == 0
            ? 2.0 * normalizedCutoff
            : std::sin(2.0 * pi * normalizedCutoff * static_cast<double>(offset))
                / (pi * static_cast<double>(offset));
        const double window = 0.5 - 0.5 * std::cos(
            2.0 * pi * static_cast<double>(tap) / static_cast<double>(tapCount - 1));
        taps[static_cast<std::size_t>(tap)] = sinc * window;
        tapSum += taps[static_cast<std::size_t>(tap)];
    }
    for (auto& tap : taps)
        tap /= tapSum;

    const int firstBit = nativeSampleRate / 50;
    const int lastBit = std::min(nativeSampleRate / 10, static_cast<int>(residual.size()) - center);
    double sumSquares = 0.0;
    int sampleCount = 0;
    for (int outputBit = firstBit; outputBit < lastBit; outputBit += decimation)
    {
        if (outputBit - center < 0 || outputBit + center >= static_cast<int>(residual.size()))
            continue;
        double filtered = 0.0;
        for (int tap = 0; tap < tapCount; ++tap)
        {
            filtered += residual[static_cast<std::size_t>(outputBit + tap - center)]
                * taps[static_cast<std::size_t>(tap)];
        }
        sumSquares += filtered * filtered;
        ++sampleCount;
    }
    const double rms = sampleCount > 0
        ? std::sqrt(sumSquares / static_cast<double>(sampleCount))
        : 1.0;
    return 20.0 * std::log10(std::max(rms, 1.0e-15));
}

void measureStrongestTone(
    const std::vector<std::uint8_t>& output,
    int transportSampleRate,
    int startTransportFrame,
    int frameCount,
    double excludeFrequencyHz,
    double& strongestHz,
    double& strongestDb)
{
    std::vector<double> bits;
    appendNativeDsdBits(output, startTransportFrame, frameCount, bits);
    double mean = 0.0;
    for (const double sample : bits)
        mean += sample;
    if (!bits.empty())
        mean /= static_cast<double>(bits.size());
    for (double& sample : bits)
        sample -= mean;

    const double sampleRate = static_cast<double>(transportSampleRate) * 16.0;
    strongestHz = 0.0;
    double strongestMagnitude = 0.0;
    for (int frequencyHz = 50; frequencyHz <= 20'000; frequencyHz += 50)
    {
        if (std::abs(static_cast<double>(frequencyHz) - excludeFrequencyHz) < 80.0)
            continue;
        const double magnitude = goertzelMagnitude(
            bits,
            static_cast<double>(frequencyHz),
            sampleRate);
        if (magnitude > strongestMagnitude)
        {
            strongestMagnitude = magnitude;
            strongestHz = static_cast<double>(frequencyHz);
        }
    }
    strongestDb = 20.0 * std::log10(std::max(strongestMagnitude, 1.0e-15));
}

Measurement measure(
    const RateSpec& rate,
    const ProfileSpec& profile,
    const InterpolationSpec& interpolation,
    echo::SdmDecisionMode decisionMode = echo::SdmDecisionMode::Greedy,
    echo::SdmPreModulatorProfile preModulatorProfile = echo::SdmPreModulatorProfile::Standard)
{
    const int frames = rate.transportSampleRate / 8;
    const auto input = createSine(rate.transportSampleRate, frames, signalFrequency, signalAmplitude);
    echo::SdmProcessor processor;
    processor.configure(
        1, profile.profile, rate.transportSampleRate, interpolation.mode, decisionMode, false, preModulatorProfile);
    applyProfileExtendedCompensation(processor, profile);
    const auto configuration = processor.configuration();

    const auto startedAt = std::chrono::steady_clock::now();
    const auto output = processor.processNativeDsd(input.data(), frames);
    const auto endedAt = std::chrono::steady_clock::now();
    const double processingMs = std::chrono::duration<double, std::milli>(endedAt - startedAt).count();

    echo::SdmProcessor deterministicProcessor;
    deterministicProcessor.configure(
        1, profile.profile, rate.transportSampleRate, interpolation.mode, decisionMode, false, preModulatorProfile);
    applyProfileExtendedCompensation(deterministicProcessor, profile);
    const auto repeatedOutput = deterministicProcessor.processNativeDsd(input.data(), frames);

    echo::SdmProcessor chunkedProcessor;
    chunkedProcessor.configure(
        1, profile.profile, rate.transportSampleRate, interpolation.mode, decisionMode, false, preModulatorProfile);
    applyProfileExtendedCompensation(chunkedProcessor, profile);
    std::vector<std::uint8_t> chunkedOutput;
    chunkedOutput.reserve(output.size());
    constexpr int chunkPattern[] = { 1, 17, 257, 1'021, 4'093 };
    int offset = 0;
    int chunkIndex = 0;
    while (offset < frames)
    {
        const int chunkFrames = std::min(
            chunkPattern[chunkIndex % static_cast<int>(std::size(chunkPattern))],
            frames - offset);
        const auto chunk = chunkedProcessor.processNativeDsd(input.data() + offset, chunkFrames);
        chunkedOutput.insert(chunkedOutput.end(), chunk.begin(), chunk.end());
        offset += chunkFrames;
        ++chunkIndex;
    }

    echo::SdmProcessor idleProcessor;
    idleProcessor.configure(
        1, profile.profile, rate.transportSampleRate, interpolation.mode, decisionMode, false, preModulatorProfile);
    applyProfileExtendedCompensation(idleProcessor, profile);
    const int idleFrames = rate.transportSampleRate / 20;
    std::vector<float> silence(static_cast<std::size_t>(idleFrames), 0.0f);
    const auto idleOutput = idleProcessor.processNativeDsd(silence.data(), idleFrames);

    Measurement result;
    result.rate = rate.name;
    result.transportSampleRate = rate.transportSampleRate;
    result.nativeSampleRate = rate.transportSampleRate * 16;
    result.profile = profile.name;
    result.interpolation = interpolation.name;
    result.extraLoop = decisionModeName(decisionMode);
    result.modulatorOrder = processor.modulatorOrder();
    result.ntfPeakGain = processor.ntfPeakGain();
    result.profileHeadroomDb = -20.0 * std::log10(configuration.profileHeadroomGain);
    result.ditherAmplitude = configuration.ditherAmplitude;
    result.preModulatorUpsampleFactor = configuration.preModulatorUpsampleFactor;
    result.preModulatorDelayBits = configuration.preModulatorDelayBits;
    result.preModulatorProfile = preModulatorProfileName(configuration.preModulatorProfile);
    result.inBandResidualDb = measureInBandResidualDb(
        input, output, configuration, signalFrequency, signalAmplitude, false);
    result.signalOneDensity = oneDensity(output, rate.transportSampleRate / 20, rate.transportSampleRate / 20);
    result.idlePatternRatio = idlePatternRatio(idleOutput, rate.transportSampleRate / 40);
    result.peakFeedbackState = processor.peakFeedbackState();
    result.stabilityRecoveries = processor.stabilityRecoveryCount();
    result.processingMs = processingMs;
    result.realtimeRatio = processingMs / 1'000.0 / (static_cast<double>(frames) / rate.transportSampleRate);
    result.checksum = checksum(output);
    result.chunkedChecksum = checksum(chunkedOutput);
    result.deterministic = output == repeatedOutput;
    result.chunkInvariant = output == chunkedOutput;
    return result;
}

SmallSignalMeasurement measureSmallSignal(
    const RateSpec& rate,
    const ProfileSpec& profile,
    bool constantStimulus,
    echo::SdmDecisionMode decisionMode = echo::SdmDecisionMode::Greedy,
    double amplitude = smallSignalAmplitude)
{
    const int frames = rate.transportSampleRate / 8;
    const auto input = constantStimulus
        ? createDc(frames, amplitude)
        : createSine(rate.transportSampleRate, frames, signalFrequency, amplitude);
    echo::SdmProcessor processor;
    processor.configure(1, profile.profile, rate.transportSampleRate, echo::SdmInterpolationMode::Linear, decisionMode);
    applyProfileExtendedCompensation(processor, profile);
    const auto configuration = processor.configuration();
    const auto output = processor.processNativeDsd(input.data(), frames);

    SmallSignalMeasurement result;
    result.rate = rate.name;
    result.transportSampleRate = rate.transportSampleRate;
    result.profile = profile.name;
    result.extraLoop = decisionModeName(decisionMode);
    result.stimulus = constantStimulus ? "dc-offset" : "sine";
    result.frequencyHz = constantStimulus ? 0.0 : signalFrequency;
    result.amplitude = amplitude;
    result.preModulatorUpsampleFactor = configuration.preModulatorUpsampleFactor;
    result.preModulatorDelayBits = configuration.preModulatorDelayBits;
    result.ditherAmplitude = configuration.ditherAmplitude;
    result.inBandResidualDb = measureInBandResidualDb(
        input, output, configuration, signalFrequency, amplitude, constantStimulus);
    measureStrongestTone(
        output,
        rate.transportSampleRate,
        rate.transportSampleRate / 50,
        rate.transportSampleRate / 20,
        constantStimulus ? 0.0 : signalFrequency,
        result.strongestToneHz,
        result.strongestToneDb);
    result.dominantWordRatio = dominantWordRatio(
        output, rate.transportSampleRate / 50, rate.transportSampleRate / 20);
    result.oneDensity = oneDensity(output, rate.transportSampleRate / 50, rate.transportSampleRate / 20);
    result.peakFeedbackState = processor.peakFeedbackState();
    result.stabilityRecoveries = processor.stabilityRecoveryCount();
    return result;
}

// Steps a 1 kHz tone from 0 dBFS down to -120 dBFS and tracks the in-band
// residual (stimulus excluded) at each level. A clean modulator keeps that
// noise floor level-independent; a spread that follows the stimulus level is
// noise modulation. Measurement only: no gate until a baseline exists.
LevelSweepMeasurement measureLevelSweep(
    const RateSpec& rate,
    const ProfileSpec& profile,
    echo::SdmDecisionMode decisionMode = echo::SdmDecisionMode::Greedy)
{
    constexpr double levelsDbfs[] = { 0.0, -20.0, -40.0, -60.0, -80.0, -100.0, -120.0 };
    const int frames = rate.transportSampleRate / 8;
    LevelSweepMeasurement result;
    result.rate = rate.name;
    result.transportSampleRate = rate.transportSampleRate;
    result.profile = profile.name;
    result.extraLoop = decisionModeName(decisionMode);
    result.frequencyHz = signalFrequency;
    for (const double levelDbfs : levelsDbfs)
    {
        const double amplitude = std::pow(10.0, levelDbfs / 20.0);
        const auto input = createSine(rate.transportSampleRate, frames, signalFrequency, amplitude);
        echo::SdmProcessor processor;
        processor.configure(
            1,
            profile.profile,
            rate.transportSampleRate,
            echo::SdmInterpolationMode::Linear,
            decisionMode);
        applyProfileExtendedCompensation(processor, profile);
        const auto configuration = processor.configuration();
        const auto output = processor.processNativeDsd(input.data(), frames);

        LevelSweepPoint point;
        point.levelDbfs = levelDbfs;
        point.inBandResidualDb = measureInBandResidualDb(
            input, output, configuration, signalFrequency, amplitude, false);
        point.oneDensity = oneDensity(output, rate.transportSampleRate / 20, rate.transportSampleRate / 20);
        point.peakFeedbackState = processor.peakFeedbackState();
        point.stabilityRecoveries = processor.stabilityRecoveryCount();
        result.profileHeadroomDb = -20.0 * std::log10(configuration.profileHeadroomGain);
        result.points.push_back(point);
    }
    double minimumResidualDb = result.points.front().inBandResidualDb;
    double maximumResidualDb = minimumResidualDb;
    for (const auto& point : result.points)
    {
        minimumResidualDb = std::min(minimumResidualDb, point.inBandResidualDb);
        maximumResidualDb = std::max(maximumResidualDb, point.inBandResidualDb);
    }
    result.noiseModulationSpreadDb = maximumResidualDb - minimumResidualDb;
    return result;
}

ImageMeasurement measureImage(const RateSpec& rate, const ProfileSpec& profile, bool heavyPreMod = false)
{
    const int frames = rate.transportSampleRate / 8;
    echo::SdmHalfbandUpsampler upsampler;
    upsampler.configure(1, echo::resolveSdmPreModulatorStageCount(rate.transportSampleRate, heavyPreMod));
    const int factor = std::max(1, upsampler.factor());
    const int bitsPerCarrier = echo::sdmBitsPerTransportSample / factor;
    std::vector<double> interpolated(static_cast<std::size_t>(frames) * 16u, 0.0);
    double previous = 0.0;
    double carriers[echo::sdmPreModulatorMaxFactor] {};
    std::size_t bitIndex = 0;
    for (int frame = 0; frame < frames; ++frame)
    {
        const double sample = std::sin(
            2.0 * pi * imageSignalFrequency * static_cast<double>(frame)
                / static_cast<double>(rate.transportSampleRate))
            * signalAmplitude;
        upsampler.processSample(0, sample, carriers);
        for (int carrier = 0; carrier < factor; ++carrier)
        {
            const double current = carriers[carrier];
            for (int bit = 0; bit < bitsPerCarrier; ++bit)
            {
                const double phase = static_cast<double>(bit + 1) / static_cast<double>(bitsPerCarrier);
                interpolated[bitIndex++] = previous + (current - previous) * phase;
            }
            previous = current;
        }
    }
    const int nativeSampleRate = rate.transportSampleRate * 16;
    const int startBit = std::max(upsampler.delayBits() + nativeSampleRate / 50, 0);
    const int bitCount = nativeSampleRate / 20;
    const std::vector<double> window(
        interpolated.begin() + startBit,
        interpolated.begin() + startBit + bitCount);
    const double imageHz = static_cast<double>(rate.transportSampleRate) - imageSignalFrequency;
    const double highestImageHz = static_cast<double>(nativeSampleRate) * 0.5 - imageSignalFrequency;
    ImageMeasurement result;
    result.rate = rate.name;
    result.transportSampleRate = rate.transportSampleRate;
    result.profile = profile.name;
    result.signalHz = imageSignalFrequency;
    result.imageHz = imageHz;
    result.preModulatorUpsampleFactor = factor;
    result.heavyPreMod = heavyPreMod;
    result.preModulatorProfile = heavyPreMod ? "heavy" : "standard";
    result.signalMagnitude = goertzelMagnitude(window, imageSignalFrequency, static_cast<double>(nativeSampleRate));
    result.imageMagnitude = goertzelMagnitude(window, imageHz, static_cast<double>(nativeSampleRate));
    result.imageRejectionDb = 20.0 * std::log10(
        std::max(result.imageMagnitude, 1.0e-15) / std::max(result.signalMagnitude, 1.0e-15));
    result.highestImageHz = highestImageHz;
    result.highestImageMagnitude = goertzelMagnitude(window, highestImageHz, static_cast<double>(nativeSampleRate));
    result.highestImageRejectionDb = 20.0 * std::log10(
        std::max(result.highestImageMagnitude, 1.0e-15) / std::max(result.signalMagnitude, 1.0e-15));
    result.stabilityRecoveries = 0;
    return result;
}

ImageMeasurement measureUltimateImage(
    const RateSpec& rate,
    echo::SdmPreModulatorProfile preModulatorProfile)
{
    const int frames = rate.transportSampleRate / 8;
    std::vector<float> input(static_cast<std::size_t>(frames), 0.0f);
    for (int frame = 0; frame < frames; ++frame)
    {
        input[static_cast<std::size_t>(frame)] = static_cast<float>(
            std::sin(2.0 * pi * imageSignalFrequency * static_cast<double>(frame)
                / static_cast<double>(rate.transportSampleRate)) * signalAmplitude);
    }
    const auto interpolated = renderPreModulator(rate, input, preModulatorProfile);
    echo::SdmUltimatePolyphaseUpsampler upsampler;
    upsampler.configure(1, preModulatorProfile);
    const int nativeSampleRate = rate.transportSampleRate * echo::sdmBitsPerTransportSample;
    const int startBit = std::max(upsampler.delayBits() + nativeSampleRate / 50, 0);
    const int bitCount = nativeSampleRate / 20;
    const std::vector<double> window(
        interpolated.begin() + startBit,
        interpolated.begin() + startBit + bitCount);
    ImageMeasurement result;
    result.rate = rate.name;
    result.transportSampleRate = rate.transportSampleRate;
    result.profile = preModulatorProfileName(preModulatorProfile);
    result.preModulatorProfile = result.profile;
    result.signalHz = imageSignalFrequency;
    result.imageHz = static_cast<double>(rate.transportSampleRate) - imageSignalFrequency;
    result.highestImageHz = static_cast<double>(nativeSampleRate) * 0.5 - imageSignalFrequency;
    result.preModulatorUpsampleFactor = upsampler.factor();
    result.signalMagnitude = goertzelMagnitude(window, result.signalHz, nativeSampleRate);
    result.imageMagnitude = goertzelMagnitude(window, result.imageHz, nativeSampleRate);
    result.imageRejectionDb = 20.0 * std::log10(
        std::max(result.imageMagnitude, 1.0e-15) / std::max(result.signalMagnitude, 1.0e-15));
    result.highestImageMagnitude = goertzelMagnitude(window, result.highestImageHz, nativeSampleRate);
    result.highestImageRejectionDb = 20.0 * std::log10(
        std::max(result.highestImageMagnitude, 1.0e-15) / std::max(result.signalMagnitude, 1.0e-15));
    return result;
}

void writeMeasurement(const Measurement& measurement, bool last)
{
    std::cout << "    {\n"
              << "      \"rate\": \"" << measurement.rate << "\",\n"
              << "      \"transportSampleRate\": " << measurement.transportSampleRate << ",\n"
              << "      \"nativeSampleRate\": " << measurement.nativeSampleRate << ",\n"
              << "      \"profile\": \"" << measurement.profile << "\",\n"
              << "      \"interpolation\": \"" << measurement.interpolation << "\",\n"
              << "      \"extraLoop\": \"" << measurement.extraLoop << "\",\n"
              << "      \"modulatorOrder\": " << measurement.modulatorOrder << ",\n"
              << "      \"ntfPeakGain\": " << measurement.ntfPeakGain << ",\n"
              << "      \"profileHeadroomDb\": " << measurement.profileHeadroomDb << ",\n"
              << "      \"ditherAmplitude\": " << measurement.ditherAmplitude << ",\n"
              << "      \"preModulatorUpsampleFactor\": " << measurement.preModulatorUpsampleFactor << ",\n"
              << "      \"preModulatorDelayBits\": " << measurement.preModulatorDelayBits << ",\n"
              << "      \"preModulatorProfile\": \"" << measurement.preModulatorProfile << "\",\n"
              << "      \"inBandResidualDb\": " << measurement.inBandResidualDb << ",\n"
              << "      \"signalOneDensity\": " << measurement.signalOneDensity << ",\n"
              << "      \"idlePatternRatio\": " << measurement.idlePatternRatio << ",\n"
              << "      \"peakFeedbackState\": " << measurement.peakFeedbackState << ",\n"
              << "      \"stabilityRecoveries\": " << measurement.stabilityRecoveries << ",\n"
              << "      \"processingMs\": " << measurement.processingMs << ",\n"
              << "      \"realtimeRatio\": " << measurement.realtimeRatio << ",\n"
              << "      \"checksumFnv1a64\": \"" << std::hex << measurement.checksum << std::dec << "\",\n"
              << "      \"chunkedChecksumFnv1a64\": \"" << std::hex << measurement.chunkedChecksum << std::dec << "\",\n"
              << "      \"deterministic\": " << (measurement.deterministic ? "true" : "false") << ",\n"
              << "      \"chunkInvariant\": " << (measurement.chunkInvariant ? "true" : "false") << "\n"
              << "    }" << (last ? "\n" : ",\n");
}

void writeSmallSignalMeasurement(const SmallSignalMeasurement& measurement, bool last)
{
    std::cout << "    {\n"
              << "      \"rate\": \"" << measurement.rate << "\",\n"
              << "      \"transportSampleRate\": " << measurement.transportSampleRate << ",\n"
              << "      \"profile\": \"" << measurement.profile << "\",\n"
              << "      \"extraLoop\": \"" << measurement.extraLoop << "\",\n"
              << "      \"stimulus\": \"" << measurement.stimulus << "\",\n"
              << "      \"frequencyHz\": " << measurement.frequencyHz << ",\n"
              << "      \"amplitude\": " << measurement.amplitude << ",\n"
              << "      \"preModulatorUpsampleFactor\": " << measurement.preModulatorUpsampleFactor << ",\n"
              << "      \"preModulatorDelayBits\": " << measurement.preModulatorDelayBits << ",\n"
              << "      \"ditherAmplitude\": " << measurement.ditherAmplitude << ",\n"
              << "      \"inBandResidualDb\": " << measurement.inBandResidualDb << ",\n"
              << "      \"strongestToneHz\": " << measurement.strongestToneHz << ",\n"
              << "      \"strongestToneDb\": " << measurement.strongestToneDb << ",\n"
              << "      \"dominantWordRatio\": " << measurement.dominantWordRatio << ",\n"
              << "      \"oneDensity\": " << measurement.oneDensity << ",\n"
              << "      \"peakFeedbackState\": " << measurement.peakFeedbackState << ",\n"
              << "      \"stabilityRecoveries\": " << measurement.stabilityRecoveries << "\n"
              << "    }" << (last ? "\n" : ",\n");
}

void writeLevelSweepMeasurement(const LevelSweepMeasurement& measurement, bool last)
{
    std::cout << "    {\n"
              << "      \"rate\": \"" << measurement.rate << "\",\n"
              << "      \"transportSampleRate\": " << measurement.transportSampleRate << ",\n"
              << "      \"profile\": \"" << measurement.profile << "\",\n"
              << "      \"extraLoop\": \"" << measurement.extraLoop << "\",\n"
              << "      \"frequencyHz\": " << measurement.frequencyHz << ",\n"
              << "      \"profileHeadroomDb\": " << measurement.profileHeadroomDb << ",\n"
              << "      \"noiseModulationSpreadDb\": " << measurement.noiseModulationSpreadDb << ",\n"
              << "      \"points\": [\n";
    for (std::size_t index = 0; index < measurement.points.size(); ++index)
    {
        const auto& point = measurement.points[index];
        std::cout << "        { \"levelDbfs\": " << point.levelDbfs
                  << ", \"inBandResidualDb\": " << point.inBandResidualDb
                  << ", \"oneDensity\": " << point.oneDensity
                  << ", \"peakFeedbackState\": " << point.peakFeedbackState
                  << ", \"stabilityRecoveries\": " << point.stabilityRecoveries << " }"
                  << (index + 1u == measurement.points.size() ? "\n" : ",\n");
    }
    std::cout << "      ]\n"
              << "    }" << (last ? "\n" : ",\n");
}

struct StressMeasurement
{
    std::string rate;
    int transportSampleRate = 0;
    std::string profile;
    std::string extraLoop;
    double peakFeedbackState = 0.0;
    std::uint64_t stabilityRecoveries = 0;
};

// Mirrors the full-scale suite in audio_engine_tests: full-scale +/- steps,
// a dual-tone at 0 dBFS, and uniform noise, streamed in 2048-frame chunks.
StressMeasurement measureFullScaleStress(
    const RateSpec& rate,
    const ProfileSpec& profile,
    echo::SdmDecisionMode decisionMode)
{
    const int frames = rate.transportSampleRate / 8;
    std::uint32_t randomState = 0x12345678u;
    std::vector<float> stress(static_cast<std::size_t>(frames));
    for (int frame = 0; frame < frames; ++frame)
    {
        const int section = (frame / 2'048) % 4;
        if (section == 0)
            stress[static_cast<std::size_t>(frame)] = 1.0f;
        else if (section == 1)
            stress[static_cast<std::size_t>(frame)] = -1.0f;
        else if (section == 2)
            stress[static_cast<std::size_t>(frame)] = static_cast<float>(
                0.57 * std::sin(2.0 * pi * 997.0 * frame / rate.transportSampleRate)
                + 0.43 * std::sin(2.0 * pi * 19'001.0 * frame / rate.transportSampleRate));
        else
        {
            randomState = randomState * 1664525u + 1013904223u;
            stress[static_cast<std::size_t>(frame)] =
                static_cast<float>(static_cast<double>(randomState) / 2147483648.0 - 1.0);
        }
    }
    echo::SdmProcessor processor;
    processor.configure(
        1, profile.profile, rate.transportSampleRate, echo::SdmInterpolationMode::Linear, decisionMode);
    applyProfileExtendedCompensation(processor, profile);
    for (int offset = 0; offset < frames; offset += 2'048)
    {
        const int chunkFrames = std::min(2'048, frames - offset);
        (void)processor.processNativeDsd(stress.data() + offset, chunkFrames);
    }
    StressMeasurement result;
    result.rate = rate.name;
    result.transportSampleRate = rate.transportSampleRate;
    result.profile = profile.name;
    result.extraLoop = decisionModeName(decisionMode);
    result.peakFeedbackState = processor.peakFeedbackState();
    result.stabilityRecoveries = processor.stabilityRecoveryCount();
    return result;
}

void writeStressMeasurement(const StressMeasurement& measurement, bool last)
{
    std::cout << "    { \"rate\": \"" << measurement.rate << "\""
              << ", \"transportSampleRate\": " << measurement.transportSampleRate
              << ", \"profile\": \"" << measurement.profile << "\""
              << ", \"extraLoop\": \"" << measurement.extraLoop << "\""
              << ", \"peakFeedbackState\": " << measurement.peakFeedbackState
              << ", \"stabilityRecoveries\": " << measurement.stabilityRecoveries << " }"
              << (last ? "\n" : ",\n");
}

// Focused decision-weight sweep cell: only the cells the Horizon-vs-Extra
// trade-off depends on, so a weight grid rebuilt per candidate stays fast.
// Full-scale stress covers every profile because the riskiest zero-recovery
// evidence lives on DSD64 Insane.
int runHorizonWeightSweep()
{
    const std::vector<RateSpec> rates {
        { "DSD64", 176'400 },
        { "DSD128", 352'800 },
    };
    const std::vector<ProfileSpec> profiles {
        { "safe", echo::SdmQualityProfile::Safe },
        { "hifi", echo::SdmQualityProfile::Hifi },
        { "reference", echo::SdmQualityProfile::Reference },
        { "insane", echo::SdmQualityProfile::Insane },
    };
    const InterpolationSpec linear { "linear", echo::SdmInterpolationMode::Linear };
    const std::vector<echo::SdmDecisionMode> decisionModes {
        echo::SdmDecisionMode::Extra,
        echo::SdmDecisionMode::Horizon,
    };

    std::vector<Measurement> measurements;
    std::vector<SmallSignalMeasurement> smallSignals;
    std::vector<StressMeasurement> stresses;
    for (const auto& rate : rates)
    {
        for (const auto& profile : { profiles[1], profiles[2] })
        {
            for (const auto decisionMode : decisionModes)
            {
                measurements.push_back(measure(rate, profile, linear, decisionMode));
                smallSignals.push_back(measureSmallSignal(rate, profile, false, decisionMode));
                // Multiple DC levels around -60 dBFS guard against picking
                // weights that only win on one deterministic stimulus.
                for (const double amplitude : { 0.002, 0.0015, 0.001, 0.0007, 0.0005 })
                    smallSignals.push_back(measureSmallSignal(rate, profile, true, decisionMode, amplitude));
            }
        }
        for (const auto& profile : profiles)
        {
            for (const auto decisionMode : decisionModes)
                stresses.push_back(measureFullScaleStress(rate, profile, decisionMode));
        }
    }

    std::cout << std::fixed << std::setprecision(6)
              << "{\n"
              << "  \"schemaVersion\": 1,\n"
              << "  \"generator\": \"echo-sdm-horizon-weight-sweep\",\n"
              << "  \"weights\": { \"extraQuantErrorWeight\": " << echo::sdmExtraQuantErrorWeight
              << ", \"horizonFutureWeight\": " << echo::sdmHorizonFutureWeight << " },\n"
              << "  \"measurementOnly\": true,\n"
              << "  \"measurements\": [\n";
    for (std::size_t index = 0; index < measurements.size(); ++index)
        writeMeasurement(measurements[index], index + 1u == measurements.size());
    std::cout << "  ],\n"
              << "  \"smallSignalMeasurements\": [\n";
    for (std::size_t index = 0; index < smallSignals.size(); ++index)
        writeSmallSignalMeasurement(smallSignals[index], index + 1u == smallSignals.size());
    std::cout << "  ],\n"
              << "  \"stressMeasurements\": [\n";
    for (std::size_t index = 0; index < stresses.size(); ++index)
        writeStressMeasurement(stresses[index], index + 1u == stresses.size());
    std::cout << "  ]\n}\n";
    return 0;
}

// Focused extended-compensation sweep cells: the EC-enabled profiles
// (adaptive, adaptive-super, reference-ec) against their EC-free anchors
// (adaptive-noec, reference), across all three decision modes. Small-signal
// DC uses multiple amplitudes so the calibrated constants do not overfit
// one deterministic stimulus. Full-scale stress replicates the zero-recovery
// gate on every rate class an EC profile could ship on (DSD64-512), because
// reference-ec has no envelope brake and its risk cells live above DSD64.
int runEcSweep()
{
    const std::vector<RateSpec> rates {
        { "DSD64", 176'400 },
        { "DSD128", 352'800 },
    };
    const std::vector<RateSpec> stressRates {
        { "DSD64", 176'400 },
        { "DSD128", 352'800 },
        { "DSD256", 705'600 },
        { "DSD512", 1'411'200 },
    };
    const ProfileSpec adaptiveEc { "adaptive", echo::SdmQualityProfile::Adaptive, -1 };
    const ProfileSpec adaptiveNoEc { "adaptive-noec", echo::SdmQualityProfile::Adaptive, 0 };
    const ProfileSpec adaptiveSuper { "adaptive-super", echo::SdmQualityProfile::AdaptiveSuper, -1 };
    const ProfileSpec referenceEc { "reference-ec", echo::SdmQualityProfile::ReferenceEc, -1 };
    const ProfileSpec reference { "reference", echo::SdmQualityProfile::Reference, -1 };
    const InterpolationSpec linear { "linear", echo::SdmInterpolationMode::Linear };
    const std::vector<echo::SdmDecisionMode> decisionModes {
        echo::SdmDecisionMode::Greedy,
        echo::SdmDecisionMode::Extra,
        echo::SdmDecisionMode::Horizon,
    };

    std::vector<Measurement> measurements;
    std::vector<SmallSignalMeasurement> smallSignals;
    std::vector<StressMeasurement> stresses;
    for (const auto& rate : rates)
    {
        for (const auto& profile : { adaptiveEc, adaptiveNoEc, adaptiveSuper, referenceEc, reference })
        {
            for (const auto decisionMode : decisionModes)
            {
                measurements.push_back(measure(rate, profile, linear, decisionMode));
                smallSignals.push_back(measureSmallSignal(rate, profile, false, decisionMode));
                for (const double amplitude : { 0.002, 0.0015, 0.001, 0.0007, 0.0005 })
                    smallSignals.push_back(measureSmallSignal(rate, profile, true, decisionMode, amplitude));
            }
        }
    }
    for (const auto& rate : stressRates)
    {
        for (const auto& profile : { adaptiveEc, adaptiveSuper, referenceEc, reference })
        {
            for (const auto decisionMode : decisionModes)
                stresses.push_back(measureFullScaleStress(rate, profile, decisionMode));
        }
    }

    std::cout << std::fixed << std::setprecision(6)
              << "{\n"
              << "  \"schemaVersion\": 2,\n"
              << "  \"generator\": \"echo-sdm-ec-sweep\",\n"
              << "  \"extendedCompensation\": { \"correlationAlpha\": " << echo::sdmEcCorrelationAlpha
              << ", \"biasWeight\": " << echo::sdmEcBiasWeight
              << ", \"maxDecisionBias\": " << echo::sdmEcMaxDecisionBias
              << ", \"superTier\": { \"correlationAlpha\": " << echo::sdmEcSuperTuning.correlationAlpha
              << ", \"biasWeight\": " << echo::sdmEcSuperTuning.biasWeight
              << ", \"activationLimit\": " << echo::sdmEcSuperTuning.activationLimit
              << ", \"maxDecisionBias\": " << echo::sdmEcSuperTuning.maxDecisionBias
              << ", \"envelopeGated\": " << (echo::sdmEcSuperTuning.envelopeGated ? "true" : "false") << " }"
              << ", \"referenceTier\": { \"correlationAlpha\": " << echo::sdmEcReferenceTuning.correlationAlpha
              << ", \"biasWeight\": " << echo::sdmEcReferenceTuning.biasWeight
              << ", \"activationLimit\": " << echo::sdmEcReferenceTuning.activationLimit
              << ", \"maxDecisionBias\": " << echo::sdmEcReferenceTuning.maxDecisionBias
              << ", \"envelopeGated\": " << (echo::sdmEcReferenceTuning.envelopeGated ? "true" : "false") << " } },\n"
              << "  \"measurementOnly\": true,\n"
              << "  \"measurements\": [\n";
    for (std::size_t index = 0; index < measurements.size(); ++index)
        writeMeasurement(measurements[index], index + 1u == measurements.size());
    std::cout << "  ],\n"
              << "  \"smallSignalMeasurements\": [\n";
    for (std::size_t index = 0; index < smallSignals.size(); ++index)
        writeSmallSignalMeasurement(smallSignals[index], index + 1u == smallSignals.size());
    std::cout << "  ],\n"
              << "  \"stressMeasurements\": [\n";
    for (std::size_t index = 0; index < stresses.size(); ++index)
        writeStressMeasurement(stresses[index], index + 1u == stresses.size());
    std::cout << "  ]\n}\n";
    return 0;
}

void writeImageMeasurement(const ImageMeasurement& measurement, bool last)
{
    std::cout << "    {\n"
              << "      \"rate\": \"" << measurement.rate << "\",\n"
              << "      \"transportSampleRate\": " << measurement.transportSampleRate << ",\n"
              << "      \"profile\": \"" << measurement.profile << "\",\n"
              << "      \"signalHz\": " << measurement.signalHz << ",\n"
              << "      \"imageHz\": " << measurement.imageHz << ",\n"
              << "      \"preModulatorUpsampleFactor\": " << measurement.preModulatorUpsampleFactor << ",\n"
              << "      \"heavyPreMod\": " << (measurement.heavyPreMod ? "true" : "false") << ",\n"
              << "      \"preModulatorProfile\": \"" << measurement.preModulatorProfile << "\",\n"
              << "      \"signalMagnitude\": " << measurement.signalMagnitude << ",\n"
              << "      \"imageMagnitude\": " << measurement.imageMagnitude << ",\n"
              << "      \"imageRejectionDb\": " << measurement.imageRejectionDb << ",\n"
              << "      \"highestImageHz\": " << measurement.highestImageHz << ",\n"
              << "      \"highestImageMagnitude\": " << measurement.highestImageMagnitude << ",\n"
              << "      \"highestImageRejectionDb\": " << measurement.highestImageRejectionDb << ",\n"
              << "      \"stabilityRecoveries\": " << measurement.stabilityRecoveries << "\n"
              << "    }" << (last ? "\n" : ",\n");
}

void writePreModulatorResponseMeasurement(
    const PreModulatorResponseMeasurement& measurement,
    bool last)
{
    std::cout << "    {\n"
              << "      \"rate\": \"" << measurement.rate << "\",\n"
              << "      \"transportSampleRate\": " << measurement.transportSampleRate << ",\n"
              << "      \"nativeSampleRate\": " << measurement.nativeSampleRate << ",\n"
              << "      \"upsampleFactor\": " << measurement.upsampleFactor << ",\n"
              << "      \"heavyPreMod\": " << (measurement.heavyPreMod ? "true" : "false") << ",\n"
              << "      \"preModulatorProfile\": \"" << measurement.preModulatorProfile << "\",\n"
              << "      \"passbandRippleDb0To20k\": " << measurement.passbandRippleDb << ",\n"
              << "      \"responseDb\": { \"1000\": " << measurement.response1kDb
              << ", \"10000\": " << measurement.response10kDb
              << ", \"20000\": " << measurement.response20kDb << " },\n"
              << "      \"phaseRadians\": { \"1000\": " << measurement.phase1kRadians
              << ", \"10000\": " << measurement.phase10kRadians
              << ", \"20000\": " << measurement.phase20kRadians << " },\n"
              << "      \"groupDelayBits\": { \"1000\": " << measurement.groupDelay1kBits
              << ", \"10000\": " << measurement.groupDelay10kBits
              << ", \"20000\": " << measurement.groupDelay20kBits << " },\n"
              << "      \"impulsePeakBit\": " << measurement.impulsePeakBit << ",\n"
              << "      \"preRingingEnergyRatio\": " << measurement.preRingingEnergyRatio << ",\n"
              << "      \"postRingingEnergyRatio\": " << measurement.postRingingEnergyRatio << "\n"
              << "    }" << (last ? "\n" : ",\n");
}
} // namespace

int main(int argc, char** argv)
{
    for (int argument = 1; argument + 1 < argc; ++argument)
    {
        if (std::string(argv[argument]) == "--export-comparison")
            exportComparisonArtifacts(argv[argument + 1]);
    }
    for (int argument = 1; argument < argc; ++argument)
    {
        if (std::string(argv[argument]) == "--horizon-weight-sweep")
            return runHorizonWeightSweep();
        if (std::string(argv[argument]) == "--ec-sweep")
            return runEcSweep();
    }
    const std::vector<RateSpec> rates {
        { "DSD64", 176'400 },
        { "DSD128", 352'800 },
        { "DSD256", 705'600 },
        { "DSD512", 1'411'200 },
    };
    const std::vector<RateSpec> family48Rates {
        { "DSD64-48k", 192'000 },
        { "DSD128-48k", 384'000 },
        { "DSD256-48k", 768'000 },
        { "DSD512-48k", 1'536'000 },
    };
    const std::vector<ProfileSpec> profiles {
        { "safe", echo::SdmQualityProfile::Safe },
        { "hifi", echo::SdmQualityProfile::Hifi },
        { "reference", echo::SdmQualityProfile::Reference },
        { "insane", echo::SdmQualityProfile::Insane },
        { "adaptive", echo::SdmQualityProfile::Adaptive },
        { "adaptive-light", echo::SdmQualityProfile::AdaptiveLight },
        { "adaptive-super", echo::SdmQualityProfile::AdaptiveSuper },
        { "reference-ec", echo::SdmQualityProfile::ReferenceEc },
    };
    const std::vector<InterpolationSpec> interpolations {
        { "linear", echo::SdmInterpolationMode::Linear },
        { "smoothstep-experimental", echo::SdmInterpolationMode::SmoothstepExperimental },
    };

    std::vector<Measurement> measurements;
    measurements.reserve(rates.size() * profiles.size() * interpolations.size() + rates.size() * 6);
    for (const auto& rate : rates)
    {
        for (const auto& profile : profiles)
        {
            for (const auto& interpolation : interpolations)
                measurements.push_back(measure(rate, profile, interpolation));
        }
    }
    for (const auto& rate : rates)
    {
        measurements.push_back(measure(
            rate,
            profiles[2],
            interpolations[0],
            echo::SdmDecisionMode::Greedy,
            echo::SdmPreModulatorProfile::UltimateReference));
        measurements.push_back(measure(
            rate,
            profiles[2],
            interpolations[0],
            echo::SdmDecisionMode::Greedy,
            echo::SdmPreModulatorProfile::UltimateTransient));
        measurements.push_back(measure(
            rate,
            profiles[2],
            interpolations[0],
            echo::SdmDecisionMode::ReferenceX));
    }
    for (const auto& rate : family48Rates)
    {
        measurements.push_back(measure(rate, profiles[2], interpolations[0]));
        measurements.push_back(measure(
            rate, profiles[2], interpolations[0], echo::SdmDecisionMode::Greedy,
            echo::SdmPreModulatorProfile::UltimateReference));
        measurements.push_back(measure(
            rate, profiles[2], interpolations[0], echo::SdmDecisionMode::Greedy,
            echo::SdmPreModulatorProfile::UltimateTransient));
    }
    for (const auto& rate : rates)
    {
        for (const auto& profile : { profiles[1], profiles[2], profiles[4], profiles[5], profiles[6], profiles[7] })
        {
            measurements.push_back(measure(
                rate,
                profile,
                interpolations[0],
                echo::SdmDecisionMode::Extra));
            measurements.push_back(measure(
                rate,
                profile,
                interpolations[0],
                echo::SdmDecisionMode::Horizon));
        }
    }

    // Extended-compensation A/B label (lab only): adaptive-noec is the
    // adaptive profile with EC forced off, isolating the compensator from
    // the envelope. Reference + EC is now the real reference-ec profile
    // above (schema 8), so the old forced-EC label is gone.
    const ProfileSpec adaptiveNoEc { "adaptive-noec", echo::SdmQualityProfile::Adaptive, 0 };

    std::vector<SmallSignalMeasurement> smallSignals;
    for (const auto& rate : { rates[0], rates[1] })
    {
        for (const auto& profile : profiles)
        {
            smallSignals.push_back(measureSmallSignal(rate, profile, false));
            smallSignals.push_back(measureSmallSignal(rate, profile, true));
        }
        for (const auto& profile : { adaptiveNoEc })
        {
            smallSignals.push_back(measureSmallSignal(rate, profile, false));
            smallSignals.push_back(measureSmallSignal(rate, profile, true));
        }
        for (const auto& profile : { profiles[1], profiles[2], profiles[4], profiles[5], profiles[6], profiles[7] })
        {
            for (const auto decisionMode : {
                echo::SdmDecisionMode::Extra,
                echo::SdmDecisionMode::Horizon,
            })
            {
                smallSignals.push_back(measureSmallSignal(rate, profile, false, decisionMode));
                smallSignals.push_back(measureSmallSignal(rate, profile, true, decisionMode));
            }
        }
    }

    std::vector<ImageMeasurement> images;
    for (const auto& rate : { rates[0], rates[1], rates[2] })
    {
        for (const auto& profile : profiles)
            images.push_back(measureImage(rate, profile));
    }
    // schema 9: DSD512 image rows. measureImage only exercises the
    // pre-modulator + last linear interpolation path and never runs the
    // modulator, so the profile field is a fixed lab label here instead of a
    // quality tier. heavyPreMod=false is the default pure x16 linear plan;
    // heavyPreMod=true mirrors the experimental one-stage halfband opt-in
    // (x2 FIR + x8 linear) so its rejection has a lab number before anyone
    // enables the flag on hardware.
    const ProfileSpec preModPath { "premod-path", echo::SdmQualityProfile::Reference };
    images.push_back(measureImage(rates[3], preModPath, false));
    images.push_back(measureImage(rates[3], preModPath, true));
    for (const auto& rate : rates)
    {
        images.push_back(measureUltimateImage(
            rate,
            echo::SdmPreModulatorProfile::UltimateReference));
        images.push_back(measureUltimateImage(
            rate,
            echo::SdmPreModulatorProfile::UltimateTransient));
    }
    for (const auto& rate : family48Rates)
    {
        images.push_back(measureImage(rate, preModPath, false));
        images.push_back(measureUltimateImage(rate, echo::SdmPreModulatorProfile::UltimateReference));
        images.push_back(measureUltimateImage(rate, echo::SdmPreModulatorProfile::UltimateTransient));
    }
    for (const auto& rate : rates)
    {
        smallSignals.push_back(measureSmallSignal(
            rate, profiles[2], false, echo::SdmDecisionMode::ReferenceX));
        smallSignals.push_back(measureSmallSignal(
            rate, profiles[2], true, echo::SdmDecisionMode::ReferenceX));
    }

    std::vector<PreModulatorResponseMeasurement> preModulatorResponses;
    for (const auto& rate : rates)
        preModulatorResponses.push_back(measurePreModulatorResponse(rate));
    preModulatorResponses.push_back(measurePreModulatorResponse(
        rates[3], echo::SdmPreModulatorProfile::Heavy));
    for (const auto& rate : rates)
    {
        preModulatorResponses.push_back(measurePreModulatorResponse(
            rate, echo::SdmPreModulatorProfile::UltimateReference));
        preModulatorResponses.push_back(measurePreModulatorResponse(
            rate, echo::SdmPreModulatorProfile::UltimateTransient));
    }
    for (const auto& rate : family48Rates)
    {
        preModulatorResponses.push_back(measurePreModulatorResponse(rate));
        preModulatorResponses.push_back(measurePreModulatorResponse(
            rate, echo::SdmPreModulatorProfile::UltimateReference));
        preModulatorResponses.push_back(measurePreModulatorResponse(
            rate, echo::SdmPreModulatorProfile::UltimateTransient));
    }

    std::vector<LevelSweepMeasurement> levelSweeps;
    for (const auto& rate : { rates[0], rates[1] })
    {
        for (const auto& profile : profiles)
            levelSweeps.push_back(measureLevelSweep(rate, profile));
        levelSweeps.push_back(measureLevelSweep(rate, adaptiveNoEc));
    }
    for (const auto& rate : { rates[2], rates[3] })
        levelSweeps.push_back(measureLevelSweep(rate, profiles[2]));
    for (const auto& rate : rates)
    {
        levelSweeps.push_back(measureLevelSweep(
            rate, profiles[2], echo::SdmDecisionMode::ReferenceX));
    }
    for (const auto& rate : family48Rates)
        levelSweeps.push_back(measureLevelSweep(rate, profiles[2]));

    // schema 7: the official full-scale suite (DC +/- steps, 0 dBFS dual
    // tone, uniform noise) across every rate class including DSD512, so the
    // 512+fs shaping keeps an explicit zero-recovery record in the baseline.
    std::vector<StressMeasurement> stresses;
    for (const auto& rate : rates)
    {
        for (const auto& profile : profiles)
        {
            for (const auto decisionMode : {
                echo::SdmDecisionMode::Greedy,
                echo::SdmDecisionMode::Extra,
                echo::SdmDecisionMode::Horizon,
            })
                stresses.push_back(measureFullScaleStress(rate, profile, decisionMode));
        }
        stresses.push_back(measureFullScaleStress(
            rate, profiles[2], echo::SdmDecisionMode::ReferenceX));
    }

    std::cout << std::fixed << std::setprecision(6)
              << "{\n"
              << "  \"schemaVersion\": 12,\n"
              << "  \"generator\": \"echo-sdm-quality-lab\",\n"
              << "  \"analysisBandHz\": 20000,\n"
              << "  \"stimulus\": { \"type\": \"sine\", \"frequencyHz\": 1000, \"amplitude\": 0.1, \"durationSeconds\": 0.125 },\n"
              << "  \"smallSignalStimulus\": { \"type\": \"sine-or-dc\", \"frequencyHz\": 1000, \"amplitude\": 0.001, \"durationSeconds\": 0.125 },\n"
              << "  \"levelSweepStimulus\": { \"type\": \"sine\", \"frequencyHz\": 1000, \"levelsDbfs\": [0, -20, -40, -60, -80, -100, -120], \"durationSeconds\": 0.125 },\n"
              << "  \"dither\": { \"family\": \"tpdf-lcg\", \"safe\": 0.0005, \"hifi\": 0.0002, \"reference\": 0.00015, \"insane\": 0.0001, \"adaptive\": 0.00015, \"adaptive-light\": 0.00015, \"adaptive-super\": 0.00015, \"reference-ec\": 0.00015 },\n"
              << "  \"decisionModes\": { \"default\": \"off\", \"extra\": { \"depth\": 1, \"headroomDb\": 1.5, \"stacksWithLowRateAddOn\": true }, \"horizon\": { \"depth\": 2, \"headroomDb\": 3.0, \"stacksWithLowRateAddOn\": false, \"insaneKeepsFullStack\": true }, \"referenceX\": { \"depth\": "
              << echo::sdmReferenceXBeamDepth << ", \"beamWidth\": "
              << echo::sdmReferenceXBeamWidth
              << ", \"headroomDb\": 0.0, \"futurePcmSamples\": 0, \"cpuOnly\": true, \"playbackAdmission\": \"pending-lab-gates\" } },\n"
              << "  \"decisionWeights\": { \"extraQuantErrorWeight\": " << echo::sdmExtraQuantErrorWeight
              << ", \"horizonFutureWeight\": " << echo::sdmHorizonFutureWeight << " },\n"
              << "  \"extendedCompensation\": { \"profiles\": [\"adaptive\", \"adaptive-super\", \"reference-ec\"], \"correlationAlpha\": "
              << echo::sdmEcCorrelationAlpha
              << ", \"biasWeight\": " << echo::sdmEcBiasWeight
              << ", \"maxDecisionBias\": " << echo::sdmEcMaxDecisionBias
              << ", \"superTier\": { \"correlationAlpha\": " << echo::sdmEcSuperTuning.correlationAlpha
              << ", \"biasWeight\": " << echo::sdmEcSuperTuning.biasWeight
              << ", \"activationLimit\": " << echo::sdmEcSuperTuning.activationLimit
              << ", \"maxDecisionBias\": " << echo::sdmEcSuperTuning.maxDecisionBias
              << ", \"envelopeGated\": " << (echo::sdmEcSuperTuning.envelopeGated ? "true" : "false") << " }"
              << ", \"referenceTier\": { \"correlationAlpha\": " << echo::sdmEcReferenceTuning.correlationAlpha
              << ", \"biasWeight\": " << echo::sdmEcReferenceTuning.biasWeight
              << ", \"activationLimit\": " << echo::sdmEcReferenceTuning.activationLimit
              << ", \"maxDecisionBias\": " << echo::sdmEcReferenceTuning.maxDecisionBias
              << ", \"envelopeGated\": " << (echo::sdmEcReferenceTuning.envelopeGated ? "true" : "false") << " }"
              << ", \"labOnlyProfiles\": [\"adaptive-noec\"] },\n"
              << "  \"preModulator\": { \"dsd64Stages\": 2, \"dsd128Stages\": 1, \"halfbandTaps\": "
              << echo::sdmPreModulatorHalfbandTapCount << ", \"kaiserBeta\": "
              << echo::sdmPreModulatorKaiserBeta << " },\n"
              << "  \"measurementOnly\": true,\n"
              << "  \"hardwareProof\": false,\n"
              << "  \"measurements\": [\n";
    for (std::size_t index = 0; index < measurements.size(); ++index)
        writeMeasurement(measurements[index], index + 1u == measurements.size());
    std::cout << "  ],\n"
              << "  \"smallSignalMeasurements\": [\n";
    for (std::size_t index = 0; index < smallSignals.size(); ++index)
        writeSmallSignalMeasurement(smallSignals[index], index + 1u == smallSignals.size());
    std::cout << "  ],\n"
              << "  \"preModulatorResponseMeasurements\": [\n";
    for (std::size_t index = 0; index < preModulatorResponses.size(); ++index)
        writePreModulatorResponseMeasurement(
            preModulatorResponses[index],
            index + 1u == preModulatorResponses.size());
    std::cout << "  ],\n"
              << "  \"imageMeasurements\": [\n";
    for (std::size_t index = 0; index < images.size(); ++index)
        writeImageMeasurement(images[index], index + 1u == images.size());
    std::cout << "  ],\n"
              << "  \"levelSweepMeasurements\": [\n";
    for (std::size_t index = 0; index < levelSweeps.size(); ++index)
        writeLevelSweepMeasurement(levelSweeps[index], index + 1u == levelSweeps.size());
    std::cout << "  ],\n"
              << "  \"stressMeasurements\": [\n";
    for (std::size_t index = 0; index < stresses.size(); ++index)
        writeStressMeasurement(stresses[index], index + 1u == stresses.size());
    std::cout << "  ]\n}\n";
    return 0;
}
