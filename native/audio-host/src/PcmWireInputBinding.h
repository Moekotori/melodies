#pragma once
struct Options;
class RawPcmInputReader;
class NativePlaybackPipeline;
void validatePcmWireOptions(const Options& options);
void bindPcmWireInput(RawPcmInputReader& reader, NativePlaybackPipeline& pipeline,
    const Options& options);
