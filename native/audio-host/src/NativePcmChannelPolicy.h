#pragma once
inline constexpr int maxNativePcmChannels = 8;
inline bool validDaemonPcmChannels(int channels) noexcept
{
    return channels >= 2 && channels <= maxNativePcmChannels;
}
inline bool asioPcmOutputRangeFits(long available, long start, unsigned int channels) noexcept
{
    return channels > 0 && channels <= maxNativePcmChannels
        && start >= 0 && start < available
        && static_cast<long>(channels) <= available - start;
}

inline bool requiresPcmChannelReopen(int outputChannels, int sourceChannels) noexcept
{
    const int nextOutputChannels = sourceChannels <= 2 ? 2 : sourceChannels;
    return nextOutputChannels != outputChannels;
}
