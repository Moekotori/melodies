#pragma once

#ifdef __CUDACC__
#define ECHO_SDM_HD __host__ __device__
#else
#define ECHO_SDM_HD
#endif

namespace echo {

enum class SdmDecisionMode : int
{
    Greedy = 0,
    Extra = 1,
    Horizon = 2,
    ReferenceX = 3,
};

// Reference-X is deliberately bounded and deterministic. It evaluates four
// committed-bit candidates ahead while retaining only the best eight nodes.
// The speculative steps reuse the current PCM input; no future PCM or dither
// is read and no FIR delay is added.
constexpr int sdmReferenceXBeamDepth = 4;
constexpr int sdmReferenceXBeamWidth = 8;
constexpr int sdmReferenceXMaximumOrder = 8;

// Extra is a same-sample 1-step look-ahead on the existing NTF. It does not
// add group delay. The extra headroom is the smallest add-on that kept the
// native full-scale suite recovery-free with Extra on; retune after any
// Extra cost-function change.
constexpr double sdmExtraLoopHeadroomDb = 1.5;
// Horizon evaluates both current output signs and the best following sign.
// It is an ECHO-owned bounded predictive error-feedback strategy: no future
// PCM is consumed, so it adds no group delay. Keep the larger headroom until
// the full quality and hardware matrix proves a smaller margin safe. On
// DSD64-class carriers this margin absorbs the low-rate high-order add-on
// for HiFi/Reference instead of stacking on top of it; the combination rule
// lives in SdmProcessor::configure and must stay in sync with
// resolveSdmStackedHeadroomDb in src/main/audio/SdmFormatPlan.ts.
constexpr double sdmHorizonHeadroomDb = 3.0;
// Decision-cost weights. The defaults are the release values shared by the
// CPU and CUDA paths; the quality-lab weight sweep rebuilds its own binary
// with override macros to grid-search candidates without touching playback
// builds. Retune only from lab evidence (scripts/run-sdm-horizon-weight-sweep.mjs).
// 2026-08-24 sweep: the quant weight stays shared with Extra at 0.25 because
// raising it regressed Extra's own DC small-signal cells; the Horizon future
// weight moved 0.5 -> 0.7, which brought DSD64 DC spurs to Extra parity and
// cut the worst DSD128 DC deficit from 9.4 dB to 4.7 dB while keeping the
// -60 dBFS sine spur advantage and a recovery-free full-scale suite.
#ifndef ECHO_SDM_EXTRA_QUANT_ERROR_WEIGHT
#define ECHO_SDM_EXTRA_QUANT_ERROR_WEIGHT 0.25
#endif
#ifndef ECHO_SDM_HORIZON_FUTURE_WEIGHT
#define ECHO_SDM_HORIZON_FUTURE_WEIGHT 0.7
#endif
constexpr double sdmExtraQuantErrorWeight = ECHO_SDM_EXTRA_QUANT_ERROR_WEIGHT;
constexpr double sdmHorizonFutureWeight = ECHO_SDM_HORIZON_FUTURE_WEIGHT;

// Extended compensation (EC, experimental): an ECHO-owned correlated-error
// compensator. Each channel keeps a leaky EMA of the quantization error
// (the correlated, sub-cutoff part of the error; the white part averages
// out) and the quantizer selection is penalized by `ecBias * q_candidate`
// with ecBias = weight * EMA, clamped. This only re-ranks the two 1-bit
// candidates; the NTF error-feedback recursion never sees the bias, so the
// loop transfer function is untouched and profiles with EC disabled stay
// bit-identical. The override macros exist for the offline lab sweep
// (scripts/run-sdm-ec-sweep.mjs); playback builds always use the defaults.
// 2026-08-24 sweep (Linux x64 software matrix, grids over alpha 0.001-0.016
// and weight 2-12): alpha 0.004 / weight 8 lifted the worst adaptive DC
// small-signal cell from -89.9 dB to -93.1 dB (within 0.2 dB of Reference's
// worst cell) with five cells gaining 4.4-6.9 dB, no sine-cell regressions,
// and zero adaptive stress recoveries. Two DC cells trade down (-2.8 dB /
// -0.9 dB) but stay below the old worst case; every neighboring grid point
// either regressed sine cells or deepened the DC trades.
#ifndef ECHO_SDM_EC_ALPHA
#define ECHO_SDM_EC_ALPHA 0.004
#endif
#ifndef ECHO_SDM_EC_BIAS_WEIGHT
#define ECHO_SDM_EC_BIAS_WEIGHT 8.0
#endif
constexpr double sdmEcCorrelationAlpha = ECHO_SDM_EC_ALPHA;
constexpr double sdmEcBiasWeight = ECHO_SDM_EC_BIAS_WEIGHT;
// Hard safety clamp on the decision bias. Not swept: it bounds how far EC
// can ever push a selection regardless of the calibrated constants above.
constexpr double sdmEcMaxDecisionBias = 0.25;
// EC engages only while |bitSample| stays at or below this activation
// limit. Correlated quantization noise is a small-signal phenomenon; near
// full scale the adaptive envelope brake is the load-bearing stability
// mechanism and an extra selection bias erodes its margin (the unbraked
// DSD256/512-class sustained-DC probes recovered with an ungated bias).
// Above the limit the bias is zero and the EMA freezes, so the loud regime
// stays bit-identical to the compensator-free stream. The value matches the
// adaptive envelope knee threshold on purpose: below it the full
// Reference-grade shaping is engaged and EC only has to fix correlation.
constexpr double sdmEcActivationLimit = 0.5;
// Release of the envelope-gated EC tiers (the brake keeps its own musical
// 0.2 s release). The gate only has to hold through the zero crossings and
// inter-peak dips of loud in-band material: 0.05 s keeps a full-scale 20 Hz
// half cycle (25 ms) above the 0.5 activation limit (decay e^-0.5 ~= 0.61)
// while re-engaging ~35 ms into a genuine quiet passage, so the quality
// lab's 125 ms small-signal windows still measure the compensator.
constexpr double sdmEcEnvelopeReleaseSeconds = 0.05;

// Per-profile EC tuning. The adaptive tier keeps the EC v1 constants above
// bit-identically (including the shared hard clamp). The super tier
// (ASDM+EC-super direction) and the reference tier (Reference NTF + EC at
// full Reference headroom, no envelope brake) carry their own lab-swept
// constants so retuning one tier can never shift another tier's bit
// stream. The override macros exist for the offline lab sweep
// (scripts/run-sdm-ec-sweep.mjs --tier super|reference); playback builds
// always use the defaults.
struct SdmEcTuning
{
    double correlationAlpha;
    double biasWeight;
    double activationLimit;
    double maxDecisionBias;
    // Gate mode. false: the bias engages whenever the instantaneous
    // |bitSample| sits at or below the activation limit (EC v1 behavior;
    // re-engages near the zero crossings of loud material, which the
    // adaptive envelope brake absorbs). true: the same instant-attack /
    // slow-release peak envelope as the adaptive brake must fall below the
    // activation limit first, so EC stays frozen through entire loud
    // passages. The envelope gate is what lets the brakeless reference tier
    // keep its full-scale stress behavior bit-identical to plain Reference.
    bool envelopeGated;
};

constexpr SdmEcTuning sdmEcAdaptiveTuning {
    sdmEcCorrelationAlpha,
    sdmEcBiasWeight,
    sdmEcActivationLimit,
    sdmEcMaxDecisionBias,
    false,
};

// Super tier: EC v2 = the adaptive EC mechanism with a stronger bias drive.
// 2026-08-24 sweep (alpha 0.002-0.008 x weight 8-24 x clamp 0.25-0.5, plus a
// weight 10/14 x clamp 0.3/0.45 neighbor grid): weight 12 with clamp 0.375
// at alpha 0.004 is a sharp optimum -- worst adaptive-super DC cell -94.0 dB
// vs adaptive's -93.1, worst sine cell -89.3 dB (identical to adaptive's own
// worst sine), zero stress recoveries DSD64-512. Neighbors fall off fast:
// weight 10 collapses to -88.2, weight 14 regresses sines to -88.9, clamp
// 0.5 gives the extra weight nowhere to go (-93.1), and widening the
// activation band (0.55-0.65) changed nothing at all, so the tier keeps the
// shared 0.5 activation gate.
#ifndef ECHO_SDM_EC_SUPER_ALPHA
#define ECHO_SDM_EC_SUPER_ALPHA 0.004
#endif
#ifndef ECHO_SDM_EC_SUPER_BIAS_WEIGHT
#define ECHO_SDM_EC_SUPER_BIAS_WEIGHT 12.0
#endif
#ifndef ECHO_SDM_EC_SUPER_ACTIVATION_LIMIT
#define ECHO_SDM_EC_SUPER_ACTIVATION_LIMIT 0.5
#endif
#ifndef ECHO_SDM_EC_SUPER_MAX_BIAS
#define ECHO_SDM_EC_SUPER_MAX_BIAS 0.375
#endif
constexpr SdmEcTuning sdmEcSuperTuning {
    ECHO_SDM_EC_SUPER_ALPHA,
    ECHO_SDM_EC_SUPER_BIAS_WEIGHT,
    ECHO_SDM_EC_SUPER_ACTIVATION_LIMIT,
    ECHO_SDM_EC_SUPER_MAX_BIAS,
    false,
};

// Reference tier: retuned for the plain Reference loop (no envelope brake,
// 4.5 dB static headroom). 2026-08-24 sweep (alpha 0.002/0.004 x weight
// 2-8, envelope-gated): weight 2.0 at alpha 0.004 was the only point with
// zero DC and zero sine regressions against plain Reference; it lifts the
// worst reference DC cell -93.3 -> -93.8 dB and improves five DC cells by
// up to 2.2 dB. Reference's deeper static shaping leaves far less
// correlated error than the adaptive loop, so the adaptive-strength bias
// (weight 8) overshoots and regresses cells here. See the ec-sweep notes in
// docs/ECHO_SDM_QUALITY_LAB.md before changing any value.
#ifndef ECHO_SDM_EC_REFERENCE_ALPHA
#define ECHO_SDM_EC_REFERENCE_ALPHA 0.004
#endif
#ifndef ECHO_SDM_EC_REFERENCE_BIAS_WEIGHT
#define ECHO_SDM_EC_REFERENCE_BIAS_WEIGHT 2.0
#endif
// The activation limit compares against the post-headroom bit sample. The
// deepest Reference stack (DSD64 low-rate add-on plus a decision margin,
// 7.5 dB) scales full-scale material down to ~0.42, so the gate must sit
// below that with margin or the envelope settles onto the activation
// boundary and EC flickers on at full scale (observed as a DSD64 greedy
// full-scale recovery at the adaptive tier's 0.5 limit). 0.35 keeps ~17%
// margin below the worst-case full-scale floor; small-signal cells sit
// orders of magnitude lower and are unaffected.
#ifndef ECHO_SDM_EC_REFERENCE_ACTIVATION_LIMIT
#define ECHO_SDM_EC_REFERENCE_ACTIVATION_LIMIT 0.35
#endif
#ifndef ECHO_SDM_EC_REFERENCE_MAX_BIAS
#define ECHO_SDM_EC_REFERENCE_MAX_BIAS 0.25
#endif
constexpr SdmEcTuning sdmEcReferenceTuning {
    ECHO_SDM_EC_REFERENCE_ALPHA,
    ECHO_SDM_EC_REFERENCE_BIAS_WEIGHT,
    ECHO_SDM_EC_REFERENCE_ACTIVATION_LIMIT,
    ECHO_SDM_EC_REFERENCE_MAX_BIAS,
    // Envelope-gated: without the adaptive brake, an instantaneously-gated
    // bias re-engages near the zero crossings of loud material and erodes
    // the Reference loop's margin (the harsh DSD128/256 greedy full-scale
    // cells recovered 5/13 times vs plain Reference's own 3/11). Freezing
    // EC below a peak envelope keeps every loud passage bit-identical to
    // plain Reference, so this tier can never be less stable than the loop
    // it compensates.
    true,
};

ECHO_SDM_HD inline double sdmExtraAbs(double value)
{
    return value < 0.0 ? -value : value;
}

ECHO_SDM_HD inline double sdmExtraClamp(double value, double minimum, double maximum)
{
    return value < minimum ? minimum : (value > maximum ? maximum : value);
}

ECHO_SDM_HD inline double predictNextSdmFeedback(
    const double* numerator,
    const double* denominator,
    const double* errorHistory,
    const double* feedbackHistory,
    int order,
    double quantizationError,
    double currentFeedback)
{
    double next = 0.0;
    if (order > 0)
        next = numerator[0] * quantizationError - denominator[0] * currentFeedback;
    for (int tap = 1; tap < order; ++tap)
        next += numerator[tap] * errorHistory[tap - 1] - denominator[tap] * feedbackHistory[tap - 1];
    return next;
}

ECHO_SDM_HD inline bool chooseSdmExtraBit(
    double decision,
    double stabilityLimit,
    const double* numerator,
    const double* denominator,
    const double* errorHistory,
    const double* feedbackHistory,
    int order,
    double currentFeedback,
    double& quantizationError,
    double ecBias = 0.0)
{
    const bool greedy = decision >= 0.0;
    const double greedyY = greedy ? 1.0 : -1.0;
    const double greedyQ = sdmExtraClamp(decision - greedyY, -stabilityLimit, stabilityLimit);
    const bool other = !greedy;
    const double otherY = other ? 1.0 : -1.0;
    const double otherQ = sdmExtraClamp(decision - otherY, -stabilityLimit, stabilityLimit);

    // Both candidates share every look-ahead term except the new
    // quantization error. Accumulate them together so the recursive hot path
    // evaluates each history product once while preserving the operation order
    // of the original two independent predictions.
    double greedyNext = 0.0;
    double otherNext = 0.0;
    if (order > 0)
    {
        greedyNext = numerator[0] * greedyQ - denominator[0] * currentFeedback;
        otherNext = numerator[0] * otherQ - denominator[0] * currentFeedback;
    }
    for (int tap = 1; tap < order; ++tap)
    {
        const double sharedTail =
            numerator[tap] * errorHistory[tap - 1]
            - denominator[tap] * feedbackHistory[tap - 1];
        greedyNext += sharedTail;
        otherNext += sharedTail;
    }
    // The EC term is linear in the candidate error, so a positive correlated
    // error state favors the candidate whose error opposes it. ecBias is
    // exactly 0.0 whenever EC is disabled, which keeps both costs and the
    // selection bit-identical to the historical stream.
    const double greedyCost = sdmExtraAbs(greedyNext) + sdmExtraQuantErrorWeight * sdmExtraAbs(greedyQ)
        + ecBias * greedyQ;
    const double otherCost = sdmExtraAbs(otherNext) + sdmExtraQuantErrorWeight * sdmExtraAbs(otherQ)
        + ecBias * otherQ;

    if (otherCost < greedyCost)
    {
        quantizationError = otherQ;
        return other;
    }
    quantizationError = greedyQ;
    return greedy;
}

ECHO_SDM_HD inline double sdmHorizonEnergy(double value)
{
    return value * value;
}

ECHO_SDM_HD inline double evaluateSdmHorizonFutureCost(
    double firstQuantizationError,
    double firstFeedback,
    double predictionInput,
    double stabilityLimit,
    double numerator0,
    double denominator0,
    double numerator1,
    double secondFeedbackTail)
{
    const double futureDecision = sdmExtraClamp(
        predictionInput + firstFeedback,
        -stabilityLimit,
        stabilityLimit);
    double bestFutureCost = 1.0e300;
    for (int candidate = 0; candidate < 2; ++candidate)
    {
        const bool futureBit = candidate == 0
            ? futureDecision >= 0.0
            : futureDecision < 0.0;
        const double futureValue = futureBit ? 1.0 : -1.0;
        const double futureQuantizationError = sdmExtraClamp(
            futureDecision - futureValue,
            -stabilityLimit,
            stabilityLimit);
        const double secondFeedback =
            numerator0 * futureQuantizationError
            - denominator0 * firstFeedback
            + numerator1 * firstQuantizationError
            + secondFeedbackTail;
        const double futureCost =
            sdmHorizonEnergy(secondFeedback)
            + sdmExtraQuantErrorWeight * sdmHorizonEnergy(futureQuantizationError);
        if (futureCost < bestFutureCost)
            bestFutureCost = futureCost;
    }
    return bestFutureCost;
}

ECHO_SDM_HD inline bool chooseSdmHorizonBitFromTails(
    double decision,
    double predictionInput,
    double stabilityLimit,
    double numerator0,
    double denominator0,
    double numerator1,
    double firstFeedbackTail,
    double secondFeedbackTail,
    double& quantizationError,
    double ecBias = 0.0)
{
    const bool greedy = decision >= 0.0;
    bool selected = greedy;
    double selectedError = 0.0;
    double selectedCost = 1.0e300;
    for (int candidate = 0; candidate < 2; ++candidate)
    {
        const bool firstBit = candidate == 0 ? greedy : !greedy;
        const double firstValue = firstBit ? 1.0 : -1.0;
        const double firstError = sdmExtraClamp(
            decision - firstValue,
            -stabilityLimit,
            stabilityLimit);
        const double firstFeedback = firstFeedbackTail + numerator0 * firstError;
        // EC penalizes only the committed first step (linear in the candidate
        // error, see chooseSdmExtraBit); the speculative future step keeps the
        // original cost. ecBias is exactly 0.0 with EC disabled.
        const double currentCost =
            sdmHorizonEnergy(firstFeedback)
            + sdmExtraQuantErrorWeight * sdmHorizonEnergy(firstError)
            + ecBias * firstError;
        const double totalCost = currentCost + sdmHorizonFutureWeight
            * evaluateSdmHorizonFutureCost(
                firstError,
                firstFeedback,
                predictionInput,
                stabilityLimit,
                numerator0,
                denominator0,
                numerator1,
                secondFeedbackTail);
        if (totalCost < selectedCost)
        {
            selected = firstBit;
            selectedError = firstError;
            selectedCost = totalCost;
        }
    }
    quantizationError = selectedError;
    return selected;
}

ECHO_SDM_HD inline bool chooseSdmHorizonBit(
    double decision,
    double predictionInput,
    double stabilityLimit,
    const double* numerator,
    const double* denominator,
    const double* errorHistory,
    const double* feedbackHistory,
    int order,
    double currentFeedback,
    double& quantizationError,
    double ecBias = 0.0)
{
    double firstFeedbackTail = 0.0;
    if (order > 0)
        firstFeedbackTail = -denominator[0] * currentFeedback;
    for (int tap = 1; tap < order; ++tap)
    {
        firstFeedbackTail +=
            numerator[tap] * errorHistory[tap - 1]
            - denominator[tap] * feedbackHistory[tap - 1];
    }

    double secondFeedbackTail = order > 1
        ? -denominator[1] * currentFeedback
        : 0.0;
    for (int tap = 2; tap < order; ++tap)
    {
        secondFeedbackTail +=
            numerator[tap] * errorHistory[tap - 2]
            - denominator[tap] * feedbackHistory[tap - 2];
    }
    return chooseSdmHorizonBitFromTails(
        decision,
        predictionInput,
        stabilityLimit,
        order > 0 ? numerator[0] : 0.0,
        order > 0 ? denominator[0] : 0.0,
        order > 1 ? numerator[1] : 0.0,
        firstFeedbackTail,
        secondFeedbackTail,
        quantizationError,
        ecBias);
}

struct SdmReferenceXNode
{
    double errorHistory[sdmReferenceXMaximumOrder] {};
    double feedbackHistory[sdmReferenceXMaximumOrder] {};
    double currentFeedback = 0.0;
    double correlatedError = 0.0;
    double cost = 0.0;
    bool firstBit = false;
    bool lastBit = false;
    int runLength = 0;
};

ECHO_SDM_HD inline bool chooseSdmReferenceXBit(
    double decision,
    double predictionInput,
    double stabilityLimit,
    const double* numerator,
    const double* denominator,
    const double* errorHistory,
    const double* feedbackHistory,
    int order,
    double currentFeedback,
    double& quantizationError)
{
    const int boundedOrder = order < 0
        ? 0
        : (order > sdmReferenceXMaximumOrder ? sdmReferenceXMaximumOrder : order);
    SdmReferenceXNode beam[sdmReferenceXBeamWidth] {};
    int beamCount = 1;
    beam[0].currentFeedback = currentFeedback;
    for (int tap = 0; tap < boundedOrder; ++tap)
    {
        beam[0].errorHistory[tap] = errorHistory[tap];
        beam[0].feedbackHistory[tap] = feedbackHistory[tap];
    }

    for (int depth = 0; depth < sdmReferenceXBeamDepth; ++depth)
    {
        SdmReferenceXNode next[sdmReferenceXBeamWidth * 2] {};
        int nextCount = 0;
        for (int nodeIndex = 0; nodeIndex < beamCount; ++nodeIndex)
        {
            const SdmReferenceXNode& parent = beam[nodeIndex];
            const double stepDecision = depth == 0
                ? decision
                : sdmExtraClamp(
                    predictionInput + parent.currentFeedback,
                    -stabilityLimit,
                    stabilityLimit);
            for (int candidate = 0; candidate < 2; ++candidate)
            {
                SdmReferenceXNode child = parent;
                const bool bit = candidate == 0;
                const double error = sdmExtraClamp(
                    stepDecision - (bit ? 1.0 : -1.0),
                    -stabilityLimit,
                    stabilityLimit);
                const double nextFeedback = predictNextSdmFeedback(
                    numerator,
                    denominator,
                    parent.errorHistory,
                    parent.feedbackHistory,
                    boundedOrder,
                    error,
                    parent.currentFeedback);
                for (int tap = boundedOrder; tap-- > 1;)
                {
                    child.errorHistory[tap] = parent.errorHistory[tap - 1];
                    child.feedbackHistory[tap] = parent.feedbackHistory[tap - 1];
                }
                if (boundedOrder > 0)
                {
                    child.errorHistory[0] = error;
                    child.feedbackHistory[0] = parent.currentFeedback;
                }
                child.currentFeedback = nextFeedback;
                child.correlatedError += 0.02 * (error - child.correlatedError);
                child.runLength = depth > 0 && bit == parent.lastBit
                    ? parent.runLength + 1
                    : 1;
                child.lastBit = bit;
                if (depth == 0)
                    child.firstBit = bit;

                const double feedbackEnergy = nextFeedback * nextFeedback;
                const double errorEnergy = error * error;
                const double correlatedEnergy = child.correlatedError * child.correlatedError;
                const double idleRisk = child.runLength > 2
                    ? static_cast<double>((child.runLength - 2) * (child.runLength - 2))
                    : 0.0;
                child.cost = parent.cost
                    + feedbackEnergy
                    + 0.25 * errorEnergy
                    + 0.5 * correlatedEnergy
                    + 0.002 * idleRisk;
                next[nextCount++] = child;
            }
        }

        // Stable insertion sort supplies a fully deterministic tie break:
        // positive candidates are emitted first, then retain parent order.
        for (int index = 1; index < nextCount; ++index)
        {
            const SdmReferenceXNode value = next[index];
            int position = index;
            while (position > 0 && value.cost < next[position - 1].cost)
            {
                next[position] = next[position - 1];
                --position;
            }
            next[position] = value;
        }
        beamCount = nextCount < sdmReferenceXBeamWidth
            ? nextCount
            : sdmReferenceXBeamWidth;
        for (int index = 0; index < beamCount; ++index)
            beam[index] = next[index];
    }

    const bool selected = beam[0].firstBit;
    quantizationError = sdmExtraClamp(
        decision - (selected ? 1.0 : -1.0),
        -stabilityLimit,
        stabilityLimit);
    return selected;
}

class SdmQuantizerStrategy final
{
public:
    void configure(SdmDecisionMode mode) noexcept { mode_ = mode; }
    SdmDecisionMode mode() const noexcept { return mode_; }
    bool predictive() const noexcept { return mode_ != SdmDecisionMode::Greedy; }

    bool choose(
        double decision,
        double predictionInput,
        double stabilityLimit,
        const double* numerator,
        const double* denominator,
        const double* errorHistory,
        const double* feedbackHistory,
        int order,
        double currentFeedback,
        double& quantizationError,
        double ecBias = 0.0) const
    {
        if (mode_ == SdmDecisionMode::ReferenceX)
        {
            return chooseSdmReferenceXBit(
                decision,
                predictionInput,
                stabilityLimit,
                numerator,
                denominator,
                errorHistory,
                feedbackHistory,
                order,
                currentFeedback,
                quantizationError);
        }
        if (mode_ == SdmDecisionMode::Horizon)
        {
            return chooseSdmHorizonBit(
                decision,
                predictionInput,
                stabilityLimit,
                numerator,
                denominator,
                errorHistory,
                feedbackHistory,
                order,
                currentFeedback,
                quantizationError,
                ecBias);
        }
        if (mode_ == SdmDecisionMode::Extra)
        {
            return chooseSdmExtraBit(
                decision,
                stabilityLimit,
                numerator,
                denominator,
                errorHistory,
                feedbackHistory,
                order,
                currentFeedback,
                quantizationError,
                ecBias);
        }
        return decision >= 0.0;
    }

private:
    SdmDecisionMode mode_ = SdmDecisionMode::Greedy;
};

} // namespace echo
