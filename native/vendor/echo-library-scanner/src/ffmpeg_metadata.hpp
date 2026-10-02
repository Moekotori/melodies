#pragma once
#include "native_metadata_result.hpp"
// Metadata probing only: no PCM decode loop, cover extraction or hashing.
bool fillFfmpegAudioMetadata(const std::string& path, NativeMetadataResult& result, std::string& error);
const char* ffmpegMetadataExtensionsJson();
