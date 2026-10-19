#pragma once

#include <optional>
#include <string>
#include <vector>

// A control-only command. It never opens an audio stream or changes PCM gain.
std::optional<int> handleDeviceVolumeCommand(const std::vector<std::string>& arguments);
