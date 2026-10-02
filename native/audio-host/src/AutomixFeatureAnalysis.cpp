#include "AutomixFeatureAnalysis.h"

#include "third_party/nlohmann_json.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <numeric>
#include <vector>

#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif

namespace {
using Json = nlohmann::json;

constexpr double pi = 3.14159265358979323846;
constexpr double minimumPhaseConfidence = 0.55;
constexpr double minimumDownbeatConfidence = 0.75;
constexpr std::array<double, 12> majorProfile {6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88};
constexpr std::array<double, 12> minorProfile {6.33, 2.68, 3.52, 5.38, 2.6, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17};
constexpr std::array<const char*, 12> camelotMajor {"8B", "3B", "10B", "5B", "12B", "7B", "2B", "9B", "4B", "11B", "6B", "1B"};
constexpr std::array<const char*, 12> camelotMinor {"5A", "12A", "7A", "2A", "9A", "4A", "11A", "6A", "1A", "8A", "3A", "10A"};

double clamp(double value, double low, double high) { return std::max(low, std::min(high, value)); }
double roundMillis(double value) { return std::floor(value * 1000.0 + 0.5) / 1000.0; }

double rms(const std::vector<float>& samples, size_t start, size_t end) {
    end = std::min(samples.size(), end);
    double energy = 0;
    for (size_t index = start; index < end; ++index) energy += static_cast<double>(samples[index]) * samples[index];
    return end > start ? std::sqrt(energy / static_cast<double>(end - start)) : 0;
}

double lagCorrelation(const std::vector<float>& onsets, int lag) {
    double product = 0, leftEnergy = 1e-12, rightEnergy = 1e-12;
    for (size_t index = static_cast<size_t>(lag); index < onsets.size(); ++index) {
        const double left = onsets[index], right = onsets[index - static_cast<size_t>(lag)];
        product += left * right;
        leftEnergy += left * left;
        rightEnergy += right * right;
    }
    return product / std::sqrt(leftEnergy * rightEnergy);
}

double refinePeriod(const std::vector<float>& onsets, int lag) {
    for (int multiple = 4; multiple >= 1; --multiple) {
        const int centre = lag * multiple;
        if (centre + multiple + 1 >= static_cast<int>(onsets.size() / 2)) continue;
        int peak = centre;
        double score = -1;
        for (int candidate = centre - multiple; candidate <= centre + multiple; ++candidate) {
            const double candidateScore = lagCorrelation(onsets, candidate);
            if (candidateScore > score) { score = candidateScore; peak = candidate; }
        }
        const double before = lagCorrelation(onsets, peak - 1);
        const double after = lagCorrelation(onsets, peak + 1);
        const double curvature = before - 2 * score + after;
        if (curvature >= -1e-6) continue;
        const double refined = (peak + clamp((before - after) / (2 * curvature), -0.5, 0.5)) / multiple;
        if (std::abs(refined - lag) <= 0.5) return refined;
    }
    return lag;
}

struct Phase { double offset = 0; double confidence = 0; bool valid = false; };

Phase measurePhase(const std::vector<float>& onsets, double period) {
    const int bins = std::min(128, std::max(8, static_cast<int>(std::ceil(period))));
    std::vector<float> votes(static_cast<size_t>(bins), 0), support(static_cast<size_t>(bins), 0);
    double weight = 0;
    int peaks = 0;
    const auto isPeak = [&](size_t index) {
        return onsets[index] > 0 && onsets[index] >= (index ? onsets[index - 1] : 0)
            && onsets[index] > (index + 1 < onsets.size() ? onsets[index + 1] : 0);
    };
    for (size_t index = 0; index < onsets.size(); ++index) if (isPeak(index)) {
        weight += onsets[index];
        ++peaks;
    }
    if (peaks < 4 || weight <= 1e-9) return {};
    const double maximumVote = 2 * weight / peaks;
    weight = 0;
    for (size_t index = 0; index < onsets.size(); ++index) if (isPeak(index)) {
        const double strength = std::min(static_cast<double>(onsets[index]), maximumVote);
        const double position = (std::fmod(static_cast<double>(index), period) / period) * bins;
        const int bin = static_cast<int>(std::floor(position));
        const double fraction = position - bin;
        votes[bin] += static_cast<float>(strength * (1 - fraction));
        votes[(bin + 1) % bins] += static_cast<float>(strength * fraction);
        support[bin] += static_cast<float>(1 - fraction);
        support[(bin + 1) % bins] += static_cast<float>(fraction);
        weight += strength;
    }
    int winner = 0;
    double best = 0;
    for (int bin = 0; bin < bins; ++bin) {
        const double score = votes[(bin + bins - 1) % bins] + votes[bin] + votes[(bin + 1) % bins];
        if (score > best) { best = score; winner = bin; }
    }
    const double peakSupport = support[(winner + bins - 1) % bins] + support[winner] + support[(winner + 1) % bins];
    const double confidence = clamp(std::min(best / weight, peakSupport / peaks), 0, 1);
    if (peakSupport < 4) return {};
    if (confidence < minimumPhaseConfidence) return {0, confidence, false};
    const double shift = (votes[(winner + 1) % bins] - votes[(winner + bins - 1) % bins]) / best;
    const double phase = std::fmod(std::fmod(winner + shift, bins) + bins, bins);
    return {phase * period / bins, confidence, true};
}

void addBarEvidence(Json& result, const std::vector<float>& onsets, double period, double phase, double frameSeconds) {
    const int count = std::min(128, static_cast<int>(std::floor((onsets.size() - phase) / period)));
    if (count < 16) return;
    std::vector<float> strengths(static_cast<size_t>(count), 0);
    const double radius = std::min(3.0, period * 0.12);
    for (int beat = 0; beat < count; ++beat) {
        const double centre = phase + beat * period;
        for (int frame = std::max(0, static_cast<int>(std::ceil(centre - radius)));
             frame <= std::min(static_cast<int>(onsets.size()) - 1, static_cast<int>(std::floor(centre + radius))); ++frame)
            strengths[beat] = std::max(strengths[beat], onsets[frame]);
    }
    struct Candidate { int metre; int phase; double confidence; };
    std::vector<Candidate> candidates;
    for (int metre : {3, 4}) {
        const int bars = count / metre;
        std::vector<uint8_t> votes(metre, 0), first(metre, 0), second(metre, 0);
        int activeBars = 0;
        for (int bar = 0; bar < bars; ++bar) {
            double best = 0, runner = 0, floor = std::numeric_limits<double>::infinity();
            int winningPhase = 0;
            for (int beat = 0; beat < metre; ++beat) {
                const double strength = strengths[bar * metre + beat];
                floor = std::min(floor, strength);
                if (strength > best) { runner = best; best = strength; winningPhase = beat; }
                else runner = std::max(runner, strength);
            }
            if (best <= 1e-6) continue;
            ++activeBars;
            if (floor < best * 0.08 || (best - runner) / best < 0.28) continue;
            ++votes[winningPhase];
            ++(bar < bars / 2 ? first : second)[winningPhase];
        }
        for (int winningPhase = 0; winningPhase < metre; ++winningPhase) {
            const double confidence = votes[winningPhase] / static_cast<double>(std::max(1, activeBars));
            if (votes[winningPhase] < 4 || first[winningPhase] < 2 || second[winningPhase] < 2
                || confidence < minimumDownbeatConfidence) continue;
            candidates.push_back({metre, winningPhase, confidence});
        }
    }
    if (candidates.size() != 1) return;
    const auto& best = candidates.front();
    result["beatsPerBar"] = best.metre;
    result["downbeatOffsetMs"] = std::floor((phase + best.phase * period) * frameSeconds * 1000 + 0.5);
    result["downbeatConfidence"] = roundMillis(best.confidence);
}

Json analyzeTempo(const std::vector<float>& samples, int sampleRate) {
    Json result = {{"bpm", nullptr}, {"bpmConfidence", nullptr}, {"beatOffsetMs", nullptr}, {"beatPhaseConfidence", nullptr}};
    if (sampleRate < 8000 || samples.size() < static_cast<size_t>(sampleRate * 2)) return result;
    const size_t sampleCount = std::min(samples.size(), static_cast<size_t>(sampleRate * 36));
    const int hop = std::max(1, static_cast<int>(std::floor(sampleRate * 0.01 + 0.5)));
    std::vector<float> onsets((sampleCount + hop - 1) / hop, 0);
    double previous = 0, meanLevel = 0, onsetSum = 0, peakOnset = 0;
    for (size_t frame = 0; frame < onsets.size(); ++frame) {
        const size_t start = frame * static_cast<size_t>(hop);
        const size_t end = std::min(sampleCount, start + static_cast<size_t>(hop));
        double energy = 0;
        for (size_t index = start; index < end; ++index) energy += static_cast<double>(samples[index]) * samples[index];
        const double level = std::sqrt(energy / (end - start));
        const double onset = std::max(0.0, level - previous);
        onsets[frame] = static_cast<float>(onset);
        meanLevel += level;
        onsetSum += onset;
        peakOnset = std::max(peakOnset, onset);
        previous = level;
    }
    if (peakOnset < std::max(1e-6, meanLevel / onsets.size() * 0.1)) return result;
    const double floor = onsetSum / onsets.size() * 0.45;
    for (float& onset : onsets) onset = static_cast<float>(std::max(0.0, onset - floor));
    const int minLag = std::max(2, static_cast<int>(std::floor(60.0 / 200 * sampleRate / hop + 0.5)));
    const int maxLag = std::min(static_cast<int>(onsets.size()) - 1, static_cast<int>(std::floor(60.0 / 60 * sampleRate / hop + 0.5)));
    int bestLag = 0;
    double bestScore = 0;
    for (int lag = minLag; lag <= maxLag; ++lag) {
        const double score = lagCorrelation(onsets, lag);
        if (score > bestScore) { bestScore = score; bestLag = lag; }
    }
    if (!bestLag || bestScore < 0.08) return result;
    double period = refinePeriod(onsets, bestLag);
    double bpm = 60.0 * sampleRate / (period * hop);
    while (bpm < 80) { bpm *= 2; period /= 2; }
    while (bpm > 180) { bpm /= 2; period *= 2; }
    const auto phase = measurePhase(onsets, period);
    result["bpm"] = roundMillis(bpm);
    result["bpmConfidence"] = roundMillis(clamp(bestScore, 0, 1));
    result["beatPhaseConfidence"] = roundMillis(phase.confidence);
    if (phase.valid) {
        result["beatOffsetMs"] = std::floor(phase.offset * hop * 1000.0 / sampleRate + 0.5);
        if (bestScore >= 0.68) addBarEvidence(result, onsets, period, phase.offset, static_cast<double>(hop) / sampleRate);
    }
    return result;
}

double correlation(const std::array<double, 12>& left, const std::array<double, 12>& right) {
    const double leftMean = std::accumulate(left.begin(), left.end(), 0.0) / 12;
    const double rightMean = std::accumulate(right.begin(), right.end(), 0.0) / 12;
    double numerator = 0, leftEnergy = 0, rightEnergy = 0;
    for (int index = 0; index < 12; ++index) {
        const double l = left[index] - leftMean, r = right[index] - rightMean;
        numerator += l * r;
        leftEnergy += l * l;
        rightEnergy += r * r;
    }
    return numerator / std::max(1e-12, std::sqrt(leftEnergy * rightEnergy));
}

Json analyzeKey(const std::vector<float>& samples, int sampleRate, Json& segmentRms) {
    if (samples.size() < static_cast<size_t>(sampleRate)) return nullptr;
    const size_t start = std::min(samples.size(), static_cast<size_t>(std::floor(sampleRate * 0.5 + 0.5)));
    const size_t end = std::max(start, samples.size() - static_cast<size_t>(std::floor(sampleRate * 0.5 + 0.5)));
    std::array<double, 12> chroma {};
    // The 49 Goertzel filters have independent state. Advance all filters for
    // each sample so the PCM window is read once and the small state stays hot.
    std::array<double, 49> coefficients {}, previous {}, previousPrevious {};
    std::array<int, 49> pitchClasses {};
    size_t filterCount = 0;
    for (int pitchClass = 0; pitchClass < 12; ++pitchClass)
        for (int midi = 36 + pitchClass; midi <= 84; midi += 12) {
            const double frequency = 440 * std::pow(2, (midi - 69) / 12.0);
            coefficients[filterCount] = 2 * std::cos((2 * pi * frequency) / sampleRate);
            pitchClasses[filterCount++] = pitchClass;
        }
    for (size_t index = start; index < end; ++index) {
        const double sample = samples[index];
        for (size_t filter = 0; filter < filterCount; ++filter) {
            const double next = sample + coefficients[filter] * previous[filter] - previousPrevious[filter];
            previousPrevious[filter] = previous[filter];
            previous[filter] = next;
        }
    }
    for (size_t filter = 0; filter < filterCount; ++filter) {
        const double power = previousPrevious[filter] * previousPrevious[filter]
            + previous[filter] * previous[filter]
            - coefficients[filter] * previous[filter] * previousPrevious[filter];
        chroma[pitchClasses[filter]] += std::max(0.0, power);
    }
    const double total = std::accumulate(chroma.begin(), chroma.end(), 0.0);
    if (!std::isfinite(total) || total <= 1e-9) return nullptr;
    for (double& value : chroma) value /= total;
    struct Candidate { int tonic; bool major; double score; };
    std::vector<Candidate> candidates;
    for (int tonic = 0; tonic < 12; ++tonic) for (bool major : {true, false}) {
        const auto& profile = major ? majorProfile : minorProfile;
        std::array<double, 12> rotated {};
        for (int index = 0; index < 12; ++index) rotated[index] = profile[(index - tonic + 12) % 12];
        candidates.push_back({tonic, major, correlation(chroma, rotated)});
    }
    std::stable_sort(candidates.begin(), candidates.end(), [](const auto& left, const auto& right) { return left.score > right.score; });
    const auto& best = candidates[0];
    const double confidence = clamp((best.score - candidates[1].score) * 3.5, 0, 1);
    for (int bucket = 0; bucket < 18; ++bucket) {
        const size_t bucketStart = samples.size() * static_cast<size_t>(bucket) / 18;
        const size_t bucketEnd = samples.size() * static_cast<size_t>(bucket + 1) / 18;
        const double level = rms(samples, bucketStart, bucketEnd);
        segmentRms.push_back(level > 0 ? 20 * std::log10(level) : -120);
    }
    if (confidence < 0.08) return nullptr;
    Json roundedChroma = Json::array();
    for (double value : chroma) roundedChroma.push_back(roundMillis(value));
    return {{"tonic", best.tonic}, {"mode", best.major ? "major" : "minor"},
            {"camelot", best.major ? camelotMajor[best.tonic] : camelotMinor[best.tonic]},
            {"confidence", roundMillis(confidence)}, {"chroma", roundedChroma}};
}
} // namespace

int runAutomixFeatureAnalysis(int sampleRate) {
    if (sampleRate < 8000 || sampleRate > 48000) return 2;
#ifdef _WIN32
    if (_setmode(_fileno(stdin), _O_BINARY) == -1) return 2;
#endif
    const size_t maximumSamples = static_cast<size_t>(sampleRate) * 36;
    std::vector<float> samples(maximumSamples);
    std::cin.read(reinterpret_cast<char*>(samples.data()), static_cast<std::streamsize>(maximumSamples * sizeof(float)));
    const auto byteCount = std::cin.gcount();
    if (byteCount < 0 || byteCount % static_cast<std::streamsize>(sizeof(float)) != 0
        || (!std::cin.eof() && std::cin.peek() != EOF)) return 2;
    samples.resize(static_cast<size_t>(byteCount) / sizeof(float));
    for (float sample : samples) if (!std::isfinite(sample)) return 2;
    Json result = analyzeTempo(samples, sampleRate);
    Json segmentRms = Json::array();
    result["key"] = analyzeKey(samples, sampleRate, segmentRms);
    result["segmentRmsDb"] = std::move(segmentRms);
    std::cout << result.dump() << '\n';
    return 0;
}
