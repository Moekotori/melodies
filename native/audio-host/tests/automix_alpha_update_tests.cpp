#include "automix-alpha/AlphaRenderBlend.h"
#include <iostream>
#include <stdexcept>
using namespace echo::automix_alpha;
static void require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
static void near(double value, double expected, double tolerance, const char* message) {
    require(std::isfinite(value) && std::abs(value - expected) <= tolerance, message);
}
static void testUpdatedPlainBlend() {
    // Literal oracle from supplied AppleTransitionSpec.valueAt(style 1, p).
    const auto gains = plainBlendGains(.25);
    near(gains[0], .5f, 1e-7f, "outgoing gain must match the new supplied easing");
    near(gains[1], .133974596f, 1e-7f, "incoming gain must match the new supplied easing");
    Parameters parameters; parameters.enabled = true; parameters.style = Style::PlainBlend;
    MixDsp dsp; dsp.configure(parameters, 8000, 8000);
    float b[] { 0, 0 }, output[2] {};
    double power = 0, dryPower = 0;
    for (int frame = 0; frame < 8000; ++frame) {
        const float a[] { .2f * static_cast<float>(std::sin(frame * 6.283185307179586 * 70 / 8000)), 0 };
        dsp.mixFrame(a, b, output, 2, .8);
        if (frame >= 4000) { power += output[0] * output[0]; dryPower += a[0] * a[0]; }
        near(output[1], 0, 0, "handover must preserve silent channels");
    }
    const double dryGain = plainBlendGains(.8)[0];
    require(power < dryPower * dryGain * dryGain * .2, "new plain route must actually hand outgoing bass over");
    dsp.configure(parameters, 8000, 8000);
    const float a[] { .2f, -.3f };
    dsp.mixFrame(a, b, output, 2, 0);
    near(output[0], a[0], 0, "reconfiguration must reset the filter and preserve the next deck start");
}
static void testContinuityAndRecovery() {
    // The existing double-precision 32-channel state exceeds the old 8 KiB assertion.
    // This update adds a cutoff double, not a filter bank; bound the actual state.
    static_assert(sizeof(RenderBlend) < 10 * 1024, "updated Alpha state must remain bounded below 10 KiB");
    Parameters parameters; parameters.enabled = true;
    MixDsp dsp; dsp.configure(parameters, 48000, 2000);
    float a[] { .2f, -.3f }, b[] { -.4f, .1f }, output[2] {};
    dsp.mixFrame(a, b, output, 2, 0);
    for (int c = 0; c < 2; ++c) near(output[c], a[c], 1e-7, "new plain blend must start on the outgoing deck");
    for (int frame = 1; frame < 2000; ++frame) {
        dsp.mixFrame(a, b, output, 2, frame / 2000.0);
        for (float value : output) require(std::isfinite(value) && std::abs(value) < 1, "new plain blend must remain finite");
    }
    dsp.mixFrame(a, b, output, 2, 1);
    for (int c = 0; c < 2; ++c) near(output[c], b[c], 1e-7, "new plain blend must commit the unfiltered incoming deck");
    RenderBlend blend, reference; blend.prepare(7, parameters, 48000, 2000); reference.prepare(7, parameters, 48000, 2000);
    const float weighted[] { a[0] * .2f, a[1] * .2f };
    float expected[2] {};
    blend.mixFrame(weighted, b, output, 2, .8, .2, .9, 1, 1, false, 0);
    reference.mixFrame(weighted, b, expected, 2, .8, .2, .9, 1, 1, false, 0);
    reference.mixFrame(weighted, b, expected, 2, .8, .2, .9, 1, 1, false, 0);
    blend.mixFrame(weighted, b, output, 2, .8, .2, .9, 1, 1, true, 0);
    for (int c = 0; c < 2; ++c) near(output[c], expected[c], 1e-7, "cancellation must enter recovery without replacing the audible mix");
    blend.mixFrame(a, b, output, 2, .9, 1, 0, 1, 1, true, 1);
    for (int c = 0; c < 2; ++c) near(output[c], a[c], 1e-7, "cancellation must restore the outgoing deck");
}
int main() {
    try { testUpdatedPlainBlend(); testContinuityAndRecovery(); std::cout << "PASS updated Alpha plain gains, bass handover, endpoints, recovery and bounded state\n"; return 0; }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
