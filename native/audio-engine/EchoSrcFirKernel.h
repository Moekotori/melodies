#pragma once

#include <cstddef>
#include <type_traits>

// Lab-only candidate. Product builds do not define this switch. Each lane
// keeps scalar tap order and separate FP64 multiply/add; never reduce taps
// horizontally or contract FMA under a precision-preserving experiment.
#if defined(ECHO_SRC_FIR_EXPERIMENTAL_AVX2) && ECHO_SRC_FIR_EXPERIMENTAL_AVX2 \
    && !defined(ECHO_SRC_FIR_FORCE_SCALAR) && (defined(_M_X64) || defined(__x86_64__))
#include "EchoSrcFirAvx2.h"
#define ECHO_SRC_FIR_HAS_AVX2_CANDIDATE 1
#else
#define ECHO_SRC_FIR_HAS_AVX2_CANDIDATE 0
#endif

#if !defined(ECHO_SRC_FIR_FORCE_SCALAR) && (defined(_M_X64) || defined(__SSE2__) \
    || (defined(_M_IX86_FP) && _M_IX86_FP >= 2))
#include <emmintrin.h>
#define ECHO_SRC_FIR_SSE2 1
#else
#define ECHO_SRC_FIR_SSE2 0
#endif

namespace echo::firKernel {

#if defined(_MSC_VER)
#pragma float_control(precise, on, push)
#pragma fp_contract(off)
#elif defined(__clang__)
#pragma clang fp contract(off)
#elif defined(__GNUC__)
#pragma GCC push_options
#pragma GCC optimize ("fp-contract=off")
#endif

// Lanes are independent output samples. Each keeps the scalar kernel's
// ascending tap order and FP64 multiply then add; no horizontal reduction,
// FP32 accumulation or fused multiply-add changes the rounding contract.
template <typename PhaseTaps, typename Sample>
void processPhase(const PhaseTaps& taps, const Sample* combined, size_t historyFrames,
    size_t inputFrames, size_t channels, size_t factor, size_t phase, Sample* output)
{
    const size_t samples = inputFrames * channels;
    const size_t historySamples = historyFrames * channels;
    size_t index = 0;
#if ECHO_SRC_FIR_HAS_AVX2_CANDIDATE
    if constexpr (std::is_same_v<Sample, double>)
        if (avx2Available()) index = processPhaseAvx2(taps, combined, historySamples,
            samples, channels, factor, phase, output);
#endif
#if ECHO_SRC_FIR_SSE2
    if constexpr (std::is_same_v<Sample, float>) for (; index + 4 <= samples; index += 4)
    {
        __m128d low = _mm_setzero_pd();
        __m128d high = _mm_setzero_pd();
        for (const auto& tap : taps)
        {
            const auto coefficient = _mm_set1_pd(static_cast<double>(tap.coefficient));
            const auto source = _mm_loadu_ps(combined + historySamples + index
                - tap.delayFrames * channels);
            low = _mm_add_pd(low, _mm_mul_pd(coefficient, _mm_cvtps_pd(source)));
            high = _mm_add_pd(high, _mm_mul_pd(coefficient,
                _mm_cvtps_pd(_mm_movehl_ps(source, source))));
        }
        const auto values = _mm_movelh_ps(_mm_cvtpd_ps(low), _mm_cvtpd_ps(high));
        if (channels == 2)
        {
            float* destination = output + (index / 2 * factor + phase) * 2;
            _mm_storel_pi(reinterpret_cast<__m64*>(destination), values);
            _mm_storeh_pi(reinterpret_cast<__m64*>(destination + factor * 2), values);
        }
        else
        {
            float lanes[4];
            _mm_storeu_ps(lanes, values);
            for (size_t lane = 0; lane < 4; ++lane)
            {
                const size_t sample = index + lane;
                output[(sample / channels * factor + phase) * channels
                    + sample % channels] = lanes[lane];
            }
        }
    }
    if constexpr (std::is_same_v<Sample, double>) for (; index + 2 <= samples; index += 2) {
        __m128d values = _mm_setzero_pd();
        for (const auto& tap : taps) values = _mm_add_pd(values, _mm_mul_pd(_mm_set1_pd(tap.coefficient),
            _mm_loadu_pd(combined + historySamples + index - tap.delayFrames * channels)));
        double lanes[2]; _mm_storeu_pd(lanes, values);
        for (size_t lane = 0; lane < 2; ++lane) {
            const size_t sample = index + lane;
            output[(sample / channels * factor + phase) * channels + sample % channels] = lanes[lane];
        }
    }
#endif
    for (; index < samples; ++index)
    {
        double value = 0.0;
        for (const auto& tap : taps)
            value += static_cast<double>(tap.coefficient)
                * static_cast<double>(combined[historySamples + index - tap.delayFrames * channels]);
        output[(index / channels * factor + phase) * channels + index % channels]
            = static_cast<Sample>(value);
    }
}
#if defined(_MSC_VER)
#pragma float_control(pop)
#elif defined(__GNUC__) && !defined(__clang__)
#pragma GCC pop_options
#endif
} // namespace echo::firKernel

#undef ECHO_SRC_FIR_SSE2
#undef ECHO_SRC_FIR_HAS_AVX2_CANDIDATE
