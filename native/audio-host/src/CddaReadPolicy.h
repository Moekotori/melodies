#pragma once

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <functional>
#include <optional>
#include <stdexcept>
#include <utility>
#include "CddaReadErrors.h"

struct CddaReadDiagnostics
{
    std::uint64_t readCalls = 0;
    std::uint64_t sectorsRead = 0;
    std::uint64_t retries = 0;
    std::uint64_t batchReductions = 0;
    std::uint64_t overlapChecks = 0;
    std::uint64_t overlapMismatches = 0;
    std::uint64_t deemphasisSectors = 0;
    double totalReadMs = 0;
    double maxReadMs = 0;
    std::uint32_t failureLba = 0;
    std::uint32_t failureSectorCount = 0;
    CddaReadFault failureKind = CddaReadFault::None;
    std::uint32_t nativeError = 0;
    std::uint8_t senseKey = 0;
    std::uint8_t senseAsc = 0;
    std::uint8_t senseAscq = 0;
    bool senseValid = false;
    bool retryDeadlineExceeded = false;
};

class CddaReadRetryTimeout final : public CddaReadError
{
public:
    CddaReadRetryTimeout() : CddaReadError("audio_cd_read_retry_timeout", CddaReadFault::RetryTimeout) {}
};

/** Counts only device reads, excluding stdout backpressure and PCM conversion. */
class CddaReadPolicy final
{
public:
    using Clock = std::function<double()>;
    explicit CddaReadPolicy(Clock clock = {}, double retryBudgetMs = 4000)
        : now(clock ? std::move(clock) : Clock([] {
            return std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now().time_since_epoch()).count();
        })), retryBudget(retryBudgetMs) {}

    double beforeRead(std::uint32_t lba = 0, std::uint32_t sectors = 0)
    {
        requestLba = lba;
        requestSectors = sectors;
        const auto started = now();
        if (retryStarted && started - *retryStarted >= retryBudget)
        {
            const CddaReadRetryTimeout error;
            diagnostics.retryDeadlineExceeded = true;
            if (diagnostics.failureKind == CddaReadFault::None) recordError(error);
            throw error;
        }
        if (retryStarted) ++diagnostics.retries;
        ++diagnostics.readCalls;
        return started;
    }

    void afterRead(double started, std::uint32_t sectors, bool success)
    {
        const auto elapsed = std::max(0.0, now() - started);
        diagnostics.totalReadMs += elapsed;
        diagnostics.maxReadMs = std::max(diagnostics.maxReadMs, elapsed);
        if (success)
        {
            diagnostics.sectorsRead += sectors;
            retryStarted.reset();
        }
        else if (!retryStarted) retryStarted = started;
    }

    void reduceBatch() { ++diagnostics.batchReductions; }
    void checkOverlap(bool matches) { ++diagnostics.overlapChecks; if (!matches) ++diagnostics.overlapMismatches; }
    void setDeemphasisSectors(std::uint64_t sectors) { diagnostics.deemphasisSectors = sectors; }
    void recordError(const CddaReadError& error)
    {
        diagnostics.failureLba = requestLba;
        diagnostics.failureSectorCount = requestSectors;
        diagnostics.failureKind = error.fault;
        diagnostics.nativeError = error.nativeError;
        diagnostics.senseKey = error.sense.key;
        diagnostics.senseAsc = error.sense.asc;
        diagnostics.senseAscq = error.sense.ascq;
        diagnostics.senseValid = error.sense.valid;
    }
    const CddaReadDiagnostics& snapshot() const { return diagnostics; }

private:
    Clock now;
    double retryBudget;
    std::optional<double> retryStarted;
    CddaReadDiagnostics diagnostics;
    std::uint32_t requestLba = 0;
    std::uint32_t requestSectors = 0;
};

/** One constant-size summary on success/error; never put metadata or drive paths here. */
class CddaReadSummary final
{
public:
    explicit CddaReadSummary(const CddaReadPolicy& policyToUse) : policy(policyToUse) {}
    ~CddaReadSummary()
    {
        const auto& stats = policy.snapshot();
        std::fprintf(stderr,
            "ECHO_CDDA_READ {\"readCalls\":%llu,\"sectorsRead\":%llu,\"retries\":%llu,"
            "\"batchReductions\":%llu,\"totalReadMs\":%.3f,\"maxReadMs\":%.3f,"
            "\"overlapChecks\":%llu,\"overlapMismatches\":%llu,\"deemphasisSectors\":%llu,"
            "\"failureKind\":%u,\"failureLba\":%u,\"failureSectorCount\":%u,\"nativeError\":%u,"
            "\"senseKey\":%u,\"senseAsc\":%u,\"senseAscq\":%u,\"senseValid\":%u,\"retryDeadlineExceeded\":%u}\n",
            static_cast<unsigned long long>(stats.readCalls), static_cast<unsigned long long>(stats.sectorsRead),
            static_cast<unsigned long long>(stats.retries), static_cast<unsigned long long>(stats.batchReductions),
            stats.totalReadMs, stats.maxReadMs, static_cast<unsigned long long>(stats.overlapChecks),
            static_cast<unsigned long long>(stats.overlapMismatches), static_cast<unsigned long long>(stats.deemphasisSectors),
            static_cast<unsigned>(stats.failureKind), stats.failureLba, stats.failureSectorCount, stats.nativeError,
            static_cast<unsigned>(stats.senseKey), static_cast<unsigned>(stats.senseAsc), static_cast<unsigned>(stats.senseAscq),
            static_cast<unsigned>(stats.senseValid), static_cast<unsigned>(stats.retryDeadlineExceeded));
    }
private:
    const CddaReadPolicy& policy;
};
