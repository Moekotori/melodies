#include "automix-alpha/AlphaRenderBlend.h"
#include "automix-alpha/AlphaSpectralFeatures.h"
#include "automix-alpha/AlphaProtocol.h"
#include <iostream>
#include <limits>
#include <stdexcept>

using namespace echo::automix_alpha;
static void require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
static void near(float value, float expected, float tolerance, const char* message) {
    require(std::isfinite(value) && std::abs(value - expected) <= tolerance, message);
}
static void testCurveShape() {
    EqCurve curve;
    const std::array<float, 12> controls { .2f, .6f, .9f, .4f, .8f, .3f, .7f, .95f, .9f, .5f, .4f, .8f };
    curve.configure(controls, 101);
    const double points[] { 0, .33, .66, 1 };
    const auto gain = [&](size_t band, size_t index) {
        const double minimum = band == 0 ? -30 : -18;
        return static_cast<float>(std::pow(10, (minimum - minimum * controls[band * 4 + index]) / 20));
    };
    for (size_t i = 0; i < 4; ++i) {
        const auto actual = curve.gainsAt(points[i]);
        for (size_t band = 0; band < 3; ++band) near(actual[band], gain(band, i), 2e-6f, "EQ must preserve its control-point gains");
    }
    // The supplied curve returned 0 dB here despite adjacent controls requesting
    // -12 and -3 dB. Check the intended bounds and monotonicity, not that defect.
    require(curve.gainsAt(.5)[0] < .708f, "EQ must not surge to unity between attenuated controls");
    for (size_t segment = 0; segment < 3; ++segment) {
        auto previous = curve.gainsAt(points[segment]);
        for (int step = 1; step <= 100; ++step) {
            const double p = points[segment] + (points[segment + 1] - points[segment]) * step / 100;
            const auto actual = curve.gainsAt(p);
            for (size_t band = 0; band < 3; ++band) {
                const float start = gain(band, segment), end = gain(band, segment + 1);
                require(actual[band] >= std::min(start, end) - 2e-6f
                    && actual[band] <= std::max(start, end) + 2e-6f, "EQ must remain inside adjacent control gains");
                require(end >= start ? actual[band] >= previous[band] - 2e-6f
                    : actual[band] <= previous[band] + 2e-6f, "EQ must not introduce extra peaks or holes");
            }
            previous = actual;
        }
    }
    std::array<float, 12> flat; flat.fill(.5f); curve.configure(flat, 80);
    near(curve.gainsAt(.5)[0], .17782794f, 2e-6f, "flat low controls must remain -15 dB");
    near(curve.gainsAt(.5)[1], .3548134f, 2e-6f, "flat mid controls must remain -9 dB");
}
static void testLowDuckChannelIndependence() {
    for (Style style : { Style::PlainBlend, Style::TailRide }) {
        Parameters parameters; parameters.enabled = true; parameters.style = style;
        MixDsp left, right; left.configure(parameters, 8000, 200); right.configure(parameters, 8000, 200);
        float outputLeft[2] {}, outputRight[2] {};
        for (int frame = 0; frame < 8000; ++frame) {
            const float pannedLeft[] { .2f, 0 }, pannedRight[] { 0, .2f };
            left.mixFrame(pannedLeft, pannedLeft, outputLeft, 2, .5);
            right.mixFrame(pannedRight, pannedRight, outputRight, 2, .5);
            near(outputRight[1], outputLeft[0], 1e-6f, "low ducking must be invariant when left/right channels swap");
            near(outputRight[0], outputLeft[1], 1e-6f, "low ducking must preserve the stereo image");
        }
        require(outputRight[1] < .19f, "right-panned competing bass must actually be attenuated");
    }
}
static void testMixEndpointsAndRecovery() {
    static_assert(sizeof(RenderBlend) < 8192, "Alpha render state must stay bounded below 8 KiB");
    for (Style style : { Style::PlainBlend, Style::BassSwap, Style::TailRide,
            Style::LearnedEq, Style::ShowMode, Style::BeatCut }) {
        Parameters parameters; parameters.enabled = true; parameters.style = style; parameters.eqControls.fill(.3f);
        MixDsp dsp; dsp.configure(parameters, 48000, 200);
        float a[] { .2f, -.3f }, b[] { -.4f, .1f }, output[2] {};
        dsp.mixFrame(a, b, output, 2, 0);
        for (int c = 0; c < 2; ++c) near(output[c], a[c], 1e-6f, "route must start continuously on Deck A");
        for (int frame = 1; frame < 2000; ++frame) {
            const double p = frame / 2000.0;
            a[0] = .2f * static_cast<float>(std::sin(frame * .02));
            b[0] = .4f * static_cast<float>(std::sin(frame * .08));
            dsp.mixFrame(a, b, output, 2, p);
            for (float value : output) require(std::isfinite(value) && std::abs(value) < 2, "route must remain finite and bounded");
        }
        dsp.mixFrame(a, b, output, 2, 1);
        for (int c = 0; c < 2; ++c) near(output[c], b[c], 1e-6f, "route must finish continuously on unfiltered Deck B");

        RenderBlend blend, uninterrupted;
        require(blend.needsPrepare(7), "new deck leases must initialize DSP state");
        blend.prepare(7, parameters, 48000, 200);
        require(!blend.needsPrepare(7) && blend.needsPrepare(8), "parameter snapshots must be needed only for a new lease");
        uninterrupted.prepare(7, parameters, 48000, 200);
        const float envelopeA = .70710678f, envelopeB = .70710678f;
        const float weighted[] { a[0] * envelopeA, a[1] * envelopeA };
        float before[2] {}, after[2] {};
        MixDsp direct; direct.configure(parameters, 48000, 200);
        const float recovered[] { weighted[0] / envelopeA, weighted[1] / envelopeA };
        direct.mixFrame(recovered, b, after, 2, .5);
        blend.mixFrame(weighted, b, before, 2, .5, envelopeA, envelopeB, 1, 1, false, 0);
        for (int c = 0; c < 2; ++c) near(before[c], after[c], 0, "normal mixing must preserve the DSP result without subtract/add rounding");
        uninterrupted.mixFrame(weighted, b, before, 2, .5, envelopeA, envelopeB, 1, 1, false, 0);
        uninterrupted.mixFrame(weighted, b, before, 2, .5, envelopeA, envelopeB, 1, 1, false, 0);
        blend.mixFrame(weighted, b, after, 2, .5, envelopeA, envelopeB, 1, 1, true, 0);
        for (int c = 0; c < 2; ++c) near(after[c], before[c], 1e-6f, "failure must not abruptly replace the Alpha route");
        blend.mixFrame(a, b, after, 2, .8, 1, 0, 1, 1, true, 1);
        for (int c = 0; c < 2; ++c) near(after[c], a[c], 1e-6f, "failure recovery must restore Deck A");
    }
}
static void testProtocolAndSpectralBoundaries() {
    Parameters p;
    require(parseParameters(nlohmann::json::object(), p) && !p.enabled, "Original plans must bypass Alpha");
    require(parseParameters({ { "alphaDsp", { { "style", "bass_swap" } } } }, p) && p.enabled, "Alpha route must parse");
    require(!parseParameters({ { "alphaDsp", { { "style", "learned_eq" } } } }, p), "learned EQ requires real controls");
    require(!parseParameters({ { "alphaDsp", { { "style", "unknown" } } } }, p), "unknown routes must be rejected");
    p.eqControls[0] = std::numeric_limits<float>::quiet_NaN();
    require(!p.valid(), "nonfinite controls must be rejected before audio rendering");
    std::vector<float> samples(8000 * 38, 0);
    const auto features = spectralFeatures(samples, 8000);
    require(features.size() == 1800 * 5, "spectral features must cap the retained analysis window");
    for (float value : features) near(value, 0, 0, "silence must not invent spectral energy");
}
int main() {
    try {
        testCurveShape(); testLowDuckChannelIndependence(); testMixEndpointsAndRecovery(); testProtocolAndSpectralBoundaries();
        std::cout << "PASS Alpha bounded EQ curve, channel-independent bass ducking, six routes, recovery and memory bounds\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
