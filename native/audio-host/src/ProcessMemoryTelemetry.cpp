#include "ProcessMemoryTelemetry.h"

#if defined(_WIN32)
#include <windows.h>
#include <psapi.h>
#elif defined(__APPLE__)
#include <mach/mach.h>
#include <unistd.h>
#else
#include <fstream>
#include <unistd.h>
#endif

std::optional<ProcessMemoryTelemetry> readCurrentProcessMemoryTelemetry() noexcept
{
#if defined(_WIN32)
    PROCESS_MEMORY_COUNTERS_EX counters {};
    counters.cb = sizeof(counters);
    if (! GetProcessMemoryInfo(
            GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
            sizeof(counters)))
        return std::nullopt;
    return ProcessMemoryTelemetry {
        static_cast<uint64_t>(GetCurrentProcessId()),
        static_cast<uint64_t>(counters.WorkingSetSize),
        static_cast<uint64_t>(counters.PrivateUsage),
    };
#elif defined(__APPLE__)
    mach_task_basic_info_data_t info {};
    mach_msg_type_number_t count = MACH_TASK_BASIC_INFO_COUNT;
    if (task_info(mach_task_self(), MACH_TASK_BASIC_INFO,
            reinterpret_cast<task_info_t>(&info), &count) != KERN_SUCCESS)
        return std::nullopt;
    return ProcessMemoryTelemetry {
        static_cast<uint64_t>(getpid()),
        static_cast<uint64_t>(info.resident_size),
        std::nullopt,
    };
#else
    std::ifstream statm("/proc/self/statm");
    uint64_t totalPages = 0;
    uint64_t residentPages = 0;
    if (! (statm >> totalPages >> residentPages))
        return std::nullopt;
    (void) totalPages;
    const long pageSize = sysconf(_SC_PAGESIZE);
    if (pageSize <= 0)
        return std::nullopt;
    return ProcessMemoryTelemetry {
        static_cast<uint64_t>(getpid()),
        residentPages * static_cast<uint64_t>(pageSize),
        std::nullopt,
    };
#endif
}
