#pragma once
#include <cstdint>
#include <fstream>
#include <optional>

// Bounded trailer probes preserve the caller's stream position.
bool hasMp3TailTags(std::ifstream& stream);
std::optional<std::uint64_t> readOggTerminalGranule(std::ifstream& stream, std::uint32_t serial);
