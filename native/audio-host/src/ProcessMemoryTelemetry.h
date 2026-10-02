#pragma once

#include <cstdint>
#include <optional>

struct ProcessMemoryTelemetry {
    uint64_t processId = 0;
    uint64_t workingSetBytes = 0;
    std::optional<uint64_t> privateBytes;
};

std::optional<ProcessMemoryTelemetry> readCurrentProcessMemoryTelemetry() noexcept;
