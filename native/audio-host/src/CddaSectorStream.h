#pragma once

#include "WindowsCddaReader.h"
#include "CddaReadPolicy.h"

#include <algorithm>
#include <array>
#include <exception>
#include <optional>
#include <span>
#include <stdexcept>
#include <vector>

struct CddaSectorChunk
{
    std::uint32_t startLba = 0;
    std::vector<std::uint8_t> bytes;
};

class CddaOverlapMismatch final : public CddaReadError
{
public:
    CddaOverlapMismatch() : CddaReadError("audio_cd_read_overlap_mismatch", CddaReadFault::Overlap) {}
};

/** One held sector, at most sixteen sectors per device read, no speculative alignment. */
class CddaSectorStream final
{
public:
    CddaSectorStream(CddaDevice& deviceToUse, CddaReadPolicy& policyToUse,
        std::uint32_t startLba, std::uint32_t endLba)
        : device(deviceToUse), policy(policyToUse), cursor(startLba), end(endLba) {}

    std::optional<CddaSectorChunk> next()
    {
        while (cursor < end || hasPending)
        {
            // A single-sector-only drive and the final tail need a separate verification read.
            if (hasPending && (cursor == end || preferredBatch == 1))
            {
                auto verified = read(pendingLba, 1, pending);
                hasPending = false;
                return CddaSectorChunk { pendingLba, std::move(verified) };
            }

            const std::uint32_t overlap = hasPending ? 1 : 0;
            const auto start = cursor - overlap;
            const auto count = std::min(preferredBatch, end - start);
            std::vector<std::uint8_t> raw;
            try
            {
                raw = read(start, count, hasPending ? std::span<const std::uint8_t>(pending) : std::span<const std::uint8_t>());
            }
            catch (const CddaReadRetryTimeout&) { throw; }
            catch (const CddaOverlapMismatch&) { throw; }
            catch (const CddaFatalReadError&) { throw; }
            catch (...)
            {
                if (count == 1) throw;
                preferredBatch = std::max<std::uint32_t>(1, count / 2);
                policy.reduceBatch();
                continue;
            }

            // Hold the tail until the next read confirms it; the overlap is emitted exactly once.
            std::copy_n(raw.end() - sectorBytes, sectorBytes, pending.begin());
            pendingLba = start + count - 1;
            hasPending = true;
            cursor += count - overlap;
            raw.resize(raw.size() - sectorBytes);
            if (!raw.empty()) return CddaSectorChunk { start, std::move(raw) };
        }
        return std::nullopt;
    }

private:
    std::vector<std::uint8_t> read(std::uint32_t start, std::uint32_t count, std::span<const std::uint8_t> expected)
    {
        std::exception_ptr lastError;
        for (int attempt = 0; attempt < 2; ++attempt)
        {
            const auto started = policy.beforeRead(start, count);
            try
            {
                auto raw = device.readAudioSectors(start, count);
                if (raw.size() != static_cast<std::size_t>(count) * sectorBytes)
                    throw CddaReadError("audio_cd_short_raw_read", CddaReadFault::ShortRead);
                if (!expected.empty())
                {
                    const bool matches = std::equal(expected.begin(), expected.end(), raw.begin());
                    policy.checkOverlap(matches);
                    if (!matches) throw CddaOverlapMismatch();
                }
                policy.afterRead(started, count, true);
                return raw;
            }
            catch (const CddaFatalReadError& error)
            {
                policy.afterRead(started, count, false);
                policy.recordError(error);
                throw;
            }
            catch (const CddaReadError& error)
            {
                policy.afterRead(started, count, false);
                policy.recordError(error);
                lastError = std::current_exception();
            }
            catch (...)
            {
                policy.afterRead(started, count, false);
                policy.recordError(CddaReadError("audio_cd_raw_read_failed", CddaReadFault::Io));
                lastError = std::current_exception();
            }
        }
        std::rethrow_exception(lastError);
    }

    static constexpr std::size_t sectorBytes = 2352;
    CddaDevice& device;
    CddaReadPolicy& policy;
    std::uint32_t cursor;
    const std::uint32_t end;
    std::uint32_t preferredBatch = 16;
    std::array<std::uint8_t, sectorBytes> pending {};
    std::uint32_t pendingLba = 0;
    bool hasPending = false;
};
