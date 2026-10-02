#include "SdmNtfDesign.h"

#include <algorithm>
#include <cmath>
#include <cstddef>

namespace echo {
namespace {

constexpr double pi = 3.14159265358979323846;

int clampOrder(int order)
{
    return std::max(1, std::min(8, order));
}

void multiplyLinear(std::vector<double>& polynomial, double zero)
{
    std::vector<double> next(polynomial.size() + 1, 0.0);
    for (std::size_t index = 0; index < polynomial.size(); ++index)
    {
        next[index] += polynomial[index];
        next[index + 1] += polynomial[index] * -zero;
    }
    polynomial = std::move(next);
}

void multiplyQuadratic(std::vector<double>& polynomial, double twoCosine)
{
    std::vector<double> next(polynomial.size() + 2, 0.0);
    for (std::size_t index = 0; index < polynomial.size(); ++index)
    {
        next[index] += polynomial[index];
        next[index + 1] += polynomial[index] * -twoCosine;
        next[index + 2] += polynomial[index];
    }
    polynomial = std::move(next);
}

// Multiplies by (1 - sumRe z^-1 + magSquared z^-2), the real quadratic of a
// conjugate pole pair with Re(p)+Re(conj(p)) = sumRe and |p|^2 = magSquared.
void multiplyConjugatePair(std::vector<double>& polynomial, double sumRe, double magSquared)
{
    std::vector<double> next(polynomial.size() + 2, 0.0);
    for (std::size_t index = 0; index < polynomial.size(); ++index)
    {
        next[index] += polynomial[index];
        next[index + 1] += polynomial[index] * -sumRe;
        next[index + 2] += polynomial[index] * magSquared;
    }
    polynomial = std::move(next);
}

// Fixed-schedule Taylor series for the 512+fs design path. Platform libm
// (glibc, MSVC) and V8 round cos/sin differently by 1 ULP for some
// arguments (observed on the order-6 pole angle), which would break the
// bit-exact CPU/TypeScript mirror contract. Every |x| used here stays below
// pi, where these fixed Horner series are accurate to well under 1 ULP and
// only use IEEE add/mul/div shared by both runtimes, so the 512+fs design
// is also identical across operating systems. Sub-DSD512 rates keep the
// libm calls so their designs stay bit-identical with every previous
// release. Mirrors cosineSeries / sineSeries in
// src/main/audio/SdmFormatPlan.ts bit for bit.
double cosineSeries(double x)
{
    const double t = x * x;
    return 1.0 + t * (-1.0 / 2.0
        + t * (1.0 / 24.0
            + t * (-1.0 / 720.0
                + t * (1.0 / 40320.0
                    + t * (-1.0 / 3628800.0
                        + t * (1.0 / 479001600.0
                            + t * (-1.0 / 87178291200.0
                                + t * (1.0 / 20922789888000.0
                                    + t * (-1.0 / 6402373705728000.0
                                        + t * (1.0 / 2432902008176640000.0
                                            + t * (-1.0 / 1124000727777607680000.0
                                                + t * (1.0 / 620448401733239439360000.0
                                                    + t * (-1.0 / 403291461126605635584000000.0
                                                        + t * (1.0 / 304888344611713860501504000000.0
                                                            + t * (-1.0 / 265252859812191058636308480000000.0)))))))))))))));
}

double sineSeries(double x)
{
    const double t = x * x;
    return x * (1.0 + t * (-1.0 / 6.0
        + t * (1.0 / 120.0
            + t * (-1.0 / 5040.0
                + t * (1.0 / 362880.0
                    + t * (-1.0 / 39916800.0
                        + t * (1.0 / 6227020800.0
                            + t * (-1.0 / 1307674368000.0
                                + t * (1.0 / 355687428096000.0
                                    + t * (-1.0 / 121645100408832000.0
                                        + t * (1.0 / 51090942171709440000.0
                                            + t * (-1.0 / 25852016738884976640000.0
                                                + t * (1.0 / 15511210043330985984000000.0
                                                    + t * (-1.0 / 10888869450418352160768000000.0
                                                        + t * (1.0 / 8841761993739701954543616000000.0
                                                            + t * (-1.0 / 8222838654177922817725562880000000.0))))))))))))))));
}

double evaluateAtNyquist(const std::vector<double>& polynomial)
{
    double value = 0.0;
    for (std::size_t index = 0; index < polynomial.size(); ++index)
        value += index % 2 == 0 ? polynomial[index] : -polynomial[index];
    return value;
}

struct MaximallyFlatPoles
{
    std::vector<double> denominator;
    double maxPoleMagnitude = 0.0;
};

// v4 pole placement: damped maximally-flat (Butterworth-like) pattern. For
// a cluster radius me2Mag > 0, pure Schreier synthesizeNTF poles form a
// Butterworth pattern around z = 1 in the m = (z + 1/z) / 2 domain,
// mb2_k = 1 + me2Mag * exp(i * ((2k-1) * pi / n - pi)). The per-order
// spread factor compresses those angles toward the deep real pole:
// spread = 1 is the pure maximally-flat pattern (lowest in-band noise,
// lowest 1-bit maximum stable amplitude) and spread = 0 collapses onto the
// v3 coincident real poles (most robust). Each m maps back to the
// in-circle root of p + 1/p = 2 * mb2. A larger me2Mag pushes the cluster
// away from DC, raising the Nyquist gain; the caller bisects me2Mag until
// |H(-1)| matches the peak-gain cap. Bisecting the radius directly keeps
// the iteration free of pow/exp so the fixed schedule stays bit-exact with
// the TypeScript mirror in src/main/audio/SdmFormatPlan.ts; the
// multiplication order (real middle pole first, then conjugate pairs by
// ascending k) is part of the same contract.
MaximallyFlatPoles maximallyFlatPoles(int order, double me2Mag, double spread, bool deterministicTrig)
{
    MaximallyFlatPoles result;
    result.denominator = { 1.0 };
    if (order % 2 == 1)
    {
        // Middle pole at angle 0: mb2 = 1 + me2Mag is real and > 1, so the
        // in-circle root is real in (0, 1). Independent of the spread.
        const double mb2 = 1.0 + me2Mag;
        const double pole = mb2 - std::sqrt(mb2 * mb2 - 1.0);
        multiplyLinear(result.denominator, pole);
        result.maxPoleMagnitude = std::max(result.maxPoleMagnitude, std::abs(pole));
    }
    for (int pair = 1; pair <= order / 2; ++pair)
    {
        const double w = static_cast<double>(2 * pair - 1) * pi / static_cast<double>(order);
        const double angle = spread * (w - pi);
        const double cosAngle = deterministicTrig ? cosineSeries(angle) : std::cos(angle);
        const double sinAngle = deterministicTrig ? sineSeries(angle) : std::sin(angle);
        const double mb2Re = 1.0 + me2Mag * cosAngle;
        const double mb2Im = me2Mag * sinAngle;
        const double cRe = mb2Re * mb2Re - mb2Im * mb2Im - 1.0;
        const double cIm = 2.0 * mb2Re * mb2Im;
        const double cMag = std::sqrt(cRe * cRe + cIm * cIm);
        const double sRe = std::sqrt((cMag + cRe) / 2.0);
        const double sIm = (cIm >= 0.0 ? 1.0 : -1.0) * std::sqrt((cMag - cRe) / 2.0);
        double pRe = mb2Re - sRe;
        double pIm = mb2Im - sIm;
        double magSquared = pRe * pRe + pIm * pIm;
        if (magSquared > 1.0)
        {
            pRe /= magSquared;
            pIm = -pIm / magSquared;
            magSquared = pRe * pRe + pIm * pIm;
        }
        multiplyConjugatePair(result.denominator, 2.0 * pRe, magSquared);
        result.maxPoleMagnitude = std::max(result.maxPoleMagnitude, std::sqrt(magSquared));
    }
    return result;
}

struct ZeroPlan
{
    int dcCount = 1;
    std::vector<double> pairFractions;
};

// Distinct non-negative frequencies as a fraction of the signal band.
// Values match Schreier / python-deltasigma ds_optzeros(..., opt=2):
// odd orders already contain a DC zero; even orders keep a DC pair and
// push the remaining pairs toward the top of the audio band.
ZeroPlan optimizedAudioZeros(int order)
{
    switch (order)
    {
        case 1:
            return { 1, {} };
        case 2:
            return { 2, {} };
        case 3:
            return { 1, { std::sqrt(3.0 / 5.0) } };
        case 4:
            return { 2, { std::sqrt(5.0 / 7.0) } };
        case 5:
        {
            const double spread = std::sqrt(25.0 / 81.0 - 5.0 / 21.0);
            return { 1, { std::sqrt(5.0 / 9.0 - spread), std::sqrt(5.0 / 9.0 + spread) } };
        }
        case 6:
        {
            const double spread = std::sqrt(56.0) / 33.0;
            return { 2, { std::sqrt(7.0 / 11.0 - spread), std::sqrt(7.0 / 11.0 + spread) } };
        }
        case 7:
            return { 1, { 0.40584371, 0.74153078, 0.94910785 } };
        case 8:
        default:
            return { 2, { 0.50563161, 0.79017286, 0.95914731 } };
    }
}

SdmNtfDesign designAllDcNtf(int order, double peakGain, int nativeSampleRate)
{
    SdmNtfDesign design;
    design.order = order;
    design.nativeSampleRate = nativeSampleRate;
    design.ntfPeakGain = peakGain;
    design.poleRadius = 2.0 / std::pow(peakGain, 1.0 / static_cast<double>(order)) - 1.0;
    design.poleRadius = std::clamp(design.poleRadius, 0.0, 0.999999);
    design.feedbackNumeratorCoefficients.assign(static_cast<std::size_t>(order), 0.0);
    design.feedbackDenominatorCoefficients.assign(static_cast<std::size_t>(order), 0.0);
    design.zeroFrequenciesHz.assign(static_cast<std::size_t>(order), 0.0);
    if (order == 1 && std::abs(peakGain - 2.0) <= 1.0e-15)
    {
        design.feedbackNumeratorCoefficients[0] = 1.0;
        design.poleRadius = 0.0;
        return design;
    }

    double binomial = 1.0;
    for (int tap = 1; tap <= order; ++tap)
    {
        binomial *= static_cast<double>(order - tap + 1) / static_cast<double>(tap);
        const double denominator = binomial * std::pow(-design.poleRadius, tap);
        design.feedbackDenominatorCoefficients[static_cast<std::size_t>(tap - 1)] = denominator;
        design.feedbackNumeratorCoefficients[static_cast<std::size_t>(tap - 1)] =
            denominator - binomial * std::pow(-1.0, tap);
    }
    return design;
}

} // namespace

SdmNtfDesign designBoundedSdmNtf(int order, double peakGain, int nativeSampleRate)
{
    const int safeOrder = clampOrder(order);
    const double safePeakGain = std::max(1.0, peakGain);
    const int safeNativeRate = std::max(1, nativeSampleRate);
    if (safeNativeRate < static_cast<int>(4.0 * sdmNtfSignalBandHz))
        return designAllDcNtf(safeOrder, safePeakGain, safeNativeRate);

    // 512+fs: DSD512-class native rates spread the same zero-fraction table
    // across a 100 kHz band; lower rates keep the 20 kHz band bit-identically.
    const bool extendedBand = safeNativeRate >= sdmNtf512PlusMinimumNativeSampleRate;
    const double signalBandHz = extendedBand ? sdmNtf512PlusSignalBandHz : sdmNtfSignalBandHz;
    const ZeroPlan zeros = optimizedAudioZeros(safeOrder);
    std::vector<double> numerator { 1.0 };
    std::vector<double> zeroFrequenciesHz;
    zeroFrequenciesHz.reserve(static_cast<std::size_t>(safeOrder));
    double numeratorAtNyquist = 1.0;

    for (int dc = 0; dc < zeros.dcCount; ++dc)
    {
        multiplyLinear(numerator, 1.0);
        zeroFrequenciesHz.push_back(0.0);
        numeratorAtNyquist *= 2.0;
    }

    for (const double fraction : zeros.pairFractions)
    {
        const double boundedFraction = std::clamp(fraction, 0.0, 0.999);
        const double frequencyHz = boundedFraction * signalBandHz;
        const double omega = 2.0 * pi * frequencyHz / static_cast<double>(safeNativeRate);
        const double cosine = extendedBand ? cosineSeries(omega) : std::cos(omega);
        multiplyQuadratic(numerator, 2.0 * cosine);
        zeroFrequenciesHz.push_back(frequencyHz);
        zeroFrequenciesHz.push_back(-frequencyHz);
        numeratorAtNyquist *= 2.0 + 2.0 * cosine;
    }

    // Bisect the m-domain cluster radius until the Nyquist gain hits the
    // cap. |H(-1)| grows monotonically with the radius on this bracket:
    // radius -> 0 collapses the poles onto z = 1 (gain slightly below 1),
    // a larger radius pulls them inward and lets the gain approach
    // 2^order. A fixed deterministic iteration schedule keeps CPU and the
    // TypeScript mirror bit-identical.
    const double poleSpread = sdmNtfPoleSpreadByOrder[static_cast<std::size_t>(safeOrder - 1)];
    const auto nyquistGainFor = [&numeratorAtNyquist, safeOrder, poleSpread, extendedBand](double radius)
    {
        const MaximallyFlatPoles candidate = maximallyFlatPoles(safeOrder, radius, poleSpread, extendedBand);
        return std::abs(numeratorAtNyquist) / std::abs(evaluateAtNyquist(candidate.denominator));
    };
    double lowRadius = 0.0;
    double highRadius = 0.5;
    for (int expand = 0; expand < 64 && nyquistGainFor(highRadius) < safePeakGain; ++expand)
    {
        lowRadius = highRadius;
        highRadius *= 2.0;
    }
    for (int iteration = 0; iteration < 200; ++iteration)
    {
        const double midRadius = 0.5 * (lowRadius + highRadius);
        if (midRadius == lowRadius || midRadius == highRadius)
            break;
        if (nyquistGainFor(midRadius) < safePeakGain)
            lowRadius = midRadius;
        else
            highRadius = midRadius;
    }
    const MaximallyFlatPoles poles = maximallyFlatPoles(safeOrder, 0.5 * (lowRadius + highRadius), poleSpread, extendedBand);
    const std::vector<double>& denominator = poles.denominator;
    const double poleRadius = std::clamp(poles.maxPoleMagnitude, 0.0, 0.999999);

    SdmNtfDesign design;
    design.order = safeOrder;
    design.nativeSampleRate = safeNativeRate;
    design.ntfPeakGain = safePeakGain;
    design.poleRadius = poleRadius;
    design.zeroFrequenciesHz = std::move(zeroFrequenciesHz);
    design.feedbackNumeratorCoefficients.resize(static_cast<std::size_t>(safeOrder), 0.0);
    design.feedbackDenominatorCoefficients.resize(static_cast<std::size_t>(safeOrder), 0.0);
    const std::size_t tapCount = static_cast<std::size_t>(safeOrder);
    for (std::size_t tap = 0; tap < tapCount; ++tap)
    {
        const double denominatorTap = tap + 1 < denominator.size() ? denominator[tap + 1] : 0.0;
        const double numeratorTap = tap + 1 < numerator.size() ? numerator[tap + 1] : 0.0;
        design.feedbackDenominatorCoefficients[tap] = denominatorTap;
        design.feedbackNumeratorCoefficients[tap] = denominatorTap - numeratorTap;
    }
    return design;
}

} // namespace echo
