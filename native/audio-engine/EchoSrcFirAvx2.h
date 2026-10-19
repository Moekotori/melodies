#pragma once
#include <immintrin.h>
#if defined(_MSC_VER)
#include <intrin.h>
#else
#include <cpuid.h>
#endif

namespace echo::firKernel {
#if defined(_MSC_VER)
#pragma float_control(precise, on, push)
#pragma fp_contract(off)
#endif
inline bool avx2Available()
{
    static const bool available = [] {
#if defined(_MSC_VER)
        int registers[4];
        __cpuid(registers, 1);
        if ((registers[2] & (1 << 27)) == 0 || (registers[2] & (1 << 28)) == 0) return false;
        if ((_xgetbv(0) & 6) != 6) return false;
        __cpuidex(registers, 7, 0);
        return (registers[1] & (1 << 5)) != 0;
#else
        __builtin_cpu_init();
        return __builtin_cpu_supports("avx2") != 0;
#endif
    }();
    return available;
}

template <typename PhaseTaps>
#if defined(__GNUC__) || defined(__clang__)
__attribute__((target("avx2,no-fma"), noinline))
#else
__declspec(noinline)
#endif
size_t processPhaseAvx2(const PhaseTaps& taps, const double* combined, size_t historySamples,
    size_t samples, size_t channels, size_t factor, size_t phase, double* output)
{
    size_t index = 0;
    for (; index + 4 <= samples; index += 4)
    {
        __m256d values = _mm256_setzero_pd();
        for (const auto& tap : taps)
            values = _mm256_add_pd(values, _mm256_mul_pd(_mm256_set1_pd(tap.coefficient),
                _mm256_loadu_pd(combined + historySamples + index - tap.delayFrames * channels)));
        double lanes[4];
        _mm256_storeu_pd(lanes, values);
        for (size_t lane = 0; lane < 4; ++lane)
        {
            const size_t sample = index + lane;
            output[(sample / channels * factor + phase) * channels + sample % channels] = lanes[lane];
        }
    }
    _mm256_zeroupper();
    return index;
}
#if defined(_MSC_VER)
#pragma float_control(pop)
#endif
}
