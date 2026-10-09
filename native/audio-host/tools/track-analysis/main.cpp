#include "TrackAnalysis.h"
#include "libav_decoder.h"

#include <algorithm>
#include <charconv>
#include <chrono>
#include <cmath>
#include <iostream>
#include <string>

namespace {

double readDuration(const char* text)
{
    if (text == nullptr || text[0] == '\0')
        return 0;
    double value = 0;
    const auto end = text + std::char_traits<char>::length(text);
    const auto parsed = std::from_chars(text, end, value);
    if (parsed.ec != std::errc {} || parsed.ptr != end)
        return 0;
    return value;
}

void appendNumber(std::string& out, double value)
{
    if (! std::isfinite(value))
        value = 0;
    char buffer[32];
    const auto result = std::to_chars(buffer, buffer + sizeof(buffer), value, std::chars_format::general, 6);
    if (result.ec != std::errc {})
        out += '0';
    else
        out.append(buffer, result.ptr);
}

void appendArray(std::string& out, const std::array<float, echo::trackAnalysisBins>& values)
{
    out += '[';
    for (int i = 0; i < echo::trackAnalysisBins; ++i) {
        if (i != 0)
            out += ',';
        appendNumber(out, values[static_cast<size_t>(i)]);
    }
    out += ']';
}

std::string toJson(double duration, const echo::TrackAnalysisResult& result)
{
    std::string out;
    out.reserve(48 * 1024);
    out += "{\"duration\":";
    appendNumber(out, duration);
    out += ",\"gainDb\":";
    appendNumber(out, result.gainDb);
    out += ",\"truePeak\":";
    appendNumber(out, result.truePeak);
    out += ",\"peaks\":";
    appendArray(out, result.peaks);
    out += ",\"energy\":";
    appendArray(out, result.energy);
    out += '}';
    return out;
}

bool isAbsolute(const std::string& path)
{
#ifdef _WIN32
    if (path.size() >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        return true;
    return path.size() >= 3 && path[0] == '\\' && path[1] == '\\';
#else
    return ! path.empty() && path[0] == '/';
#endif
}

int analyze(const std::string& path, double duration)
{
    if (! isAbsolute(path))
        throw std::runtime_error("分析只接受绝对路径");
    const int channels = std::clamp(echo::LibavDecoder::probe(path).channels, 1, 2);
    echo::LibavPcmStreamDecoder decoder;
    decoder.open(path, 48000, channels);
    echo::TrackAnalyzer analyzer(duration, 48000, decoder.channels());
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(45);
    int64_t frames = 0;
    while (! decoder.eof()) {
        if (std::chrono::steady_clock::now() > deadline)
            throw std::runtime_error("分析超过 45 秒");
        auto chunk = decoder.readFrames(4096);
        if (chunk.frames <= 0)
            break;
        analyzer.addFrames(chunk.samples.data(), chunk.frames);
        frames += chunk.frames;
    }
    if (decoder.cancelled() || frames <= 0)
        throw std::runtime_error("当前曲目无法分析");
    auto result = analyzer.finish();
    result.durationSeconds = duration;
    const auto json = toJson(duration, result);
    if (json.size() > 96 * 1024)
        throw std::runtime_error("分析结果超过上限");
    std::cout << json << '\n';
    return std::cout.good() ? 0 : 1;
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 3) {
        std::cerr << "用法: echo-track-analysis <绝对路径> <时长秒>\n";
        return 2;
    }
    try {
        const double duration = readDuration(argv[2]);
        if (! std::isfinite(duration) || duration <= 0 || duration > 4.0 * 3600.0)
            throw std::runtime_error("可视化仅支持不超过四小时的本地曲目");
        return analyze(argv[1], duration);
    }
    catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
