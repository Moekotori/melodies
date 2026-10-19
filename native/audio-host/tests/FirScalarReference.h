#pragma once
#include <cstddef>

// The reference is a separately compiled copy of the actual product sources,
// with SIMD disabled and an isolated C++ namespace. No copied FIR algorithm.
struct FirReferenceStage {
    int factor;
    const float* taps;
    size_t tapCount;
};
void* firReferenceCreate(int channels, const FirReferenceStage* stages, size_t count);
void firReferenceDestroy(void* reference);
size_t firReferenceProcess(void* reference, const float* input, int frames,
    float* output, size_t capacity);
size_t firReferenceProcessDouble(void* reference, const double* input, int frames,
    double* output, size_t capacity);
