#pragma once

#include "../../audio-engine/NativeFormatProcessor.h"

#include <memory>
#include <string>
#include <vector>

class CudaFirProcessor final
{
public:
    struct Timings {
        double h2dMs = 0, firMs = 0, d2hMs = 0;
        double synchronizeMs = 0, commitMs = 0;
        size_t deviceBytes = 0, pinnedBytes = 0;
    };
    Timings lastTimings() const noexcept;
    CudaFirProcessor();
    ~CudaFirProcessor();

    CudaFirProcessor(CudaFirProcessor&&) noexcept;
    CudaFirProcessor& operator=(CudaFirProcessor&&) noexcept;

    CudaFirProcessor(const CudaFirProcessor&) = delete;
    CudaFirProcessor& operator=(const CudaFirProcessor&) = delete;

    bool configure(
        int channels,
        const std::vector<echo::EchoSrcStageConfig>& stages,
        int maximumInputFrames,
        std::string& error);
    void release();
    void reset();
    bool reset(std::string& error);
    bool process(
        const float* interleaved,
        int frames,
        std::vector<float>& output,
        std::string& error);
    bool processDouble(const double* interleaved, int frames,
        std::vector<double>& output, std::string& error);
    bool copyLatestHistory(std::vector<std::vector<double>>& histories) const;
    bool restoreHistory(const std::vector<std::vector<double>>& histories,
        std::string& error);
    bool copyLatestHistory(std::vector<std::vector<float>>& histories) const;
    bool restoreHistory(
        const std::vector<std::vector<float>>& histories,
        std::string& error);

    bool active() const noexcept;
    std::string deviceName() const;
    double warmupMilliseconds() const noexcept;
    static bool builtWithCuda() noexcept;
    // Native regression harness: fail after real GPU work and D2H, before commit.
    void failNextBlockForTesting();

    // Keeps the CUDA runtime (primary context, hundreds of MB of host commit)
    // alive across a reconfiguration. Processors drop the context as soon as no
    // CUDA user remains, so without this an active CUDA processor would be torn
    // down and rebuilt on every per-track configure. When the hold ends and the
    // decision was "no CUDA", the context is released. No-op without CUDA, and
    // never touches the runtime unless a device was actually selected.
    class RuntimeHold final
    {
    public:
        RuntimeHold();
        ~RuntimeHold();
        RuntimeHold(const RuntimeHold&) = delete;
        RuntimeHold& operator=(const RuntimeHold&) = delete;
    };

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};
