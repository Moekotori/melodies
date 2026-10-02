#pragma once

#include "../../audio-engine/NativeFormatProcessor.h"

#include <memory>
#include <string>
#include <vector>

class CudaFirProcessor final
{
public:
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
    bool process(
        const float* interleaved,
        int frames,
        std::vector<float>& output,
        std::string& error);
    bool copyLatestHistory(std::vector<std::vector<float>>& histories) const;
    bool restoreHistory(
        const std::vector<std::vector<float>>& histories,
        std::string& error);

    bool active() const noexcept;
    std::string deviceName() const;
    double warmupMilliseconds() const noexcept;
    static bool builtWithCuda() noexcept;

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
