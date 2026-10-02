#pragma once

#include <array>
#include <vector>

namespace echo {

constexpr int sdmNtfDefaultNativeSampleRate = 2'822'400;
constexpr double sdmNtfSignalBandHz = 20'000.0;
// 512+fs shaping (HQPlayer 512+fs direction): DSD512-class native rates
// (>= 22.5792 MHz, covering the 44.1k and 48k families) spread the opt=2
// zeros across a 100 kHz band instead of 20 kHz, trading extended-band
// flatness for extra ultrasonic headroom. DSD64/128/256 keep the 20 kHz
// band bit-identically. Must stay in sync with the mirror constants in
// src/main/audio/SdmFormatPlan.ts.
constexpr int sdmNtf512PlusMinimumNativeSampleRate = 22'579'200;
constexpr double sdmNtf512PlusSignalBandHz = 100'000.0;

constexpr double resolveSdmNtfSignalBandHz(int nativeSampleRate)
{
    return nativeSampleRate >= sdmNtf512PlusMinimumNativeSampleRate
        ? sdmNtf512PlusSignalBandHz
        : sdmNtfSignalBandHz;
}
// Peak gains are the highest values that stay recovery-free under the
// native full-scale DC / multitone / noise / step suite. 7th-order at 1.75
// failed that suite, so Reference stays at 1.60.
constexpr double sdmNtfSafePeakGain = 1.5;
constexpr double sdmNtfHifiPeakGain = 1.65;
constexpr double sdmNtfReferencePeakGain = 1.6;
constexpr double sdmNtfInsanePeakGain = 1.65;
// Adaptive (v5) relaxed pole set: the same order-7 opt=2 zeros as Reference
// with the poles bisected to this lower Nyquist gain. The adaptive tiers run
// it as a second error-feedback recursion and blend the two feedbacks from
// the signal envelope; because both designs share the zero polynomial, every
// blend keeps the in-band NTF zeros exactly and only the out-of-band peak
// moves. 2026-09-06 calibration (Windows x64, full-scale DC / dual-tone /
// noise / step suite, DSD64-512 x greedy/Extra/Horizon, forced mix 0.6 and
// 1.0): 1.25 and 1.35 are recovery-free with peak feedback <= 1.6; 1.45
// already recovers at DSD128/256 greedy and peaks at 3.9. 1.35 keeps a
// clear margin at the smallest in-band cost (full-scale -20 dBFS-probe
// residual -105 dB DSD64 / -120 dB DSD128 versus Reference -121 / -130).
// Must stay in sync with sdmNtfAdaptiveRelaxedPeakGain in
// src/main/audio/SdmFormatPlan.ts.
constexpr double sdmNtfAdaptiveRelaxedPeakGain = 1.35;
// v4 pole-spread (damping) factor per NTF order, indexed by order - 1.
// 1.0 is the pure maximally-flat Butterworth pattern; 0.0 collapses onto
// the v3 coincident real poles. Orders 3/6/7/8 (Safe/HiFi/Reference/Insane)
// are the largest spreads that stayed recovery-free with margin under the
// multi-seed full-scale stress suite and the 0 dBFS DSD64/128 level sweep
// at the unchanged peak-gain caps; other orders are unshipped and use
// conservative placeholders. Must stay bit-identical with
// sdmNtfPoleSpreadByOrder in src/main/audio/SdmFormatPlan.ts.
constexpr std::array<double, 8> sdmNtfPoleSpreadByOrder {
    1.0, 0.5, 0.8, 0.5, 0.5, 0.35, 0.35, 0.5,
};

struct SdmNtfDesign
{
    int order = 0;
    int nativeSampleRate = sdmNtfDefaultNativeSampleRate;
    double ntfPeakGain = 1.0;
    double poleRadius = 0.0;
    std::vector<double> feedbackNumeratorCoefficients;
    std::vector<double> feedbackDenominatorCoefficients;
    std::vector<double> zeroFrequenciesHz;
};

// v4: Schreier synthesizeNTF opt=2 zeros (keep H(1)=0, then spread the
// remaining unit-circle zeros across the resolved signal band: 0-20 kHz, or
// 0-100 kHz on DSD512-class native rates) with a damped maximally-flat
// (Butterworth-like) pole pattern, bisected so |H(-1)| equals the requested
// peak gain. poleRadius reports the largest pole magnitude of the cluster.
SdmNtfDesign designBoundedSdmNtf(
    int order,
    double peakGain,
    int nativeSampleRate = sdmNtfDefaultNativeSampleRate);

} // namespace echo
