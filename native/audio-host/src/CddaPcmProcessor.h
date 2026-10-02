#pragma once

#include "WindowsCddaReader.h"
#include "CddaDeemphasis.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <stdexcept>
#include <vector>

/** Convert CD PCM into one bounded output chunk, applying only explicit TOC flags. */
class CddaPcmProcessor final
{
public:
    explicit CddaPcmProcessor(const std::vector<CddaTrackInfo>& tracksToUse) : tracks(tracksToUse) {}

    std::vector<float> convert(std::span<const std::uint8_t> raw, std::uint32_t startLba)
    {
        if (raw.empty() || raw.size() % sectorBytes != 0)
            throw std::runtime_error("audio_cd_unaligned_pcm");
        std::vector<float> samples(raw.size() / 2);
        for (std::size_t sector = 0; sector < raw.size() / sectorBytes; ++sector)
        {
            const auto lba = startLba + static_cast<std::uint32_t>(sector);
            while (trackIndex < tracks.size() && lba >= tracks[trackIndex].endLba) ++trackIndex;
            if (trackIndex == tracks.size() || lba < tracks[trackIndex].startLba || !tracks[trackIndex].audio)
                throw std::runtime_error("audio_cd_track_timing_unavailable");
            const bool enabled = tracks[trackIndex].preEmphasis;
            if (enabled != emphasisEnabled) { deemphasis.reset(); emphasisEnabled = enabled; }
            if (enabled) ++processedEmphasisSectors;
            const auto firstSample = sector * sectorBytes / 2;
            for (std::size_t sample = firstSample; sample < firstSample + sectorBytes / 2; ++sample)
            {
                const auto byte = sample * 2;
                const auto value = static_cast<std::int16_t>(static_cast<std::uint16_t>(raw[byte])
                    | (static_cast<std::uint16_t>(raw[byte + 1]) << 8));
                const float input = static_cast<float>(value) / 32768.0f;
                samples[sample] = enabled ? deemphasis.process(input, sample % 2) : input;
            }
        }
        return samples;
    }

    std::uint64_t deemphasizedSectors() const { return processedEmphasisSectors; }

private:
    static constexpr std::size_t sectorBytes = 2352;
    const std::vector<CddaTrackInfo>& tracks;
    std::size_t trackIndex = 0;
    CddaDeemphasis deemphasis;
    bool emphasisEnabled = false;
    std::uint64_t processedEmphasisSectors = 0;
};
