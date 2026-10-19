#include "EqProcessor.h"

#include <cmath>
#include <iostream>
#include <stdexcept>

namespace
{
void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

void testSubFloatResidual()
{
    // These two FIR biquads model cancellation across adjacent stages. A float
    // handoff erases the residual; double must preserve it until PCM output.
    echo::BiquadState first, second;
    const double residual = std::ldexp(1.0, -30);
    const echo::BiquadCoefficients add { 1.0, residual, 0.0, 0.0, 0.0 };
    const echo::BiquadCoefficients subtract { 1.0, -1.0, 0.0, 0.0, 0.0 };
    second.processDouble(first.processDouble(1.0, add), subtract);
    require(second.processDouble(first.processDouble(1.0, add), subtract) == residual,
        "cascaded biquads lost a sub-float residual");

    echo::EqSvfState svf;
    const echo::EqSvfCoefficients identity;
    require(svf.processDouble(1.0 + residual, identity) == 1.0 + residual,
        "SVF handoff rounded to float");
    echo::EqBandFilter band;
    const auto bell = echo::makeEqBandCoefficients(48000, 1000, 6, 1, echo::EqFilterType::Peaking, true);
    const double firstImpulse = band.processDouble(1.0, bell);
    require(firstImpulse != static_cast<double>(static_cast<float>(firstImpulse)),
        "band dispatch rounded its double result to float");
}

void testReciprocalCascade()
{
    echo::EqProcessor eq;
    eq.prepare(48000, 512, 2);
    eq.setEnabled(true);
    for (int band = 0; band < 30; ++band) {
        eq.setBandFrequencyHz(band, 1000);
        eq.setBandFilterType(band, echo::EqFilterType::LowShelf);
        eq.setBandQ(band, 0.7f);
        eq.setBandGainDb(band, band % 2 == 0 ? 12.0f : -12.0f);
    }
    eq.reset();
    echo::FloatAudioBuffer buffer(2, 512);
    for (int block = 0; block < 16; ++block) {
        for (int ch = 0; ch < 2; ++ch)
            for (int frame = 0; frame < 512; ++frame) buffer.setSample(ch, frame, 0.125f);
        eq.processBlock(buffer, 0, 512);
    }
    for (int ch = 0; ch < 2; ++ch)
        for (int frame = 0; frame < 512; ++frame)
            require(buffer.getSample(ch, frame) == 0.125f,
                "reciprocal shelf cascade accumulated interstage rounding");
}

void testExtendedChannels(echo::EqBandScope scope, echo::EqDynamicMode mode)
{
    echo::EqProcessor eq;
    eq.prepare(48000, 256, 32);
    eq.setEnabled(true);
    eq.setPreampDb(-6);
    eq.setBandProcessing(0, scope, mode, 0, 2, 5, 80);
    eq.reset();
    echo::FloatAudioBuffer buffer(32, 256);
    const auto fill = [&] {
        for (int ch = 0; ch < 32; ++ch)
            for (int frame = 0; frame < 256; ++frame) buffer.setSample(ch, frame, 0.1f + ch * 0.001f);
    };
    fill();
    eq.processBlock(buffer, 4, 248);
    const double gain = std::pow(10.0, -6.0 / 20.0);
    // A threshold of 0 dB keeps the dynamic detector below threshold. Scoped
    // zero-gain filters also stay transparent, leaving only the known preamp.
    for (int ch = 0; ch < 32; ++ch) {
        const float dry = 0.1f + ch * 0.001f;
        require(buffer.getSample(ch, 0) == dry && buffer.getSample(ch, 255) == dry,
            "processing touched samples outside the requested range");
        require(std::abs(buffer.getSample(ch, 128) - dry * gain) < 1e-7,
            "extended EQ left a prepared channel without preamp processing");
    }
    eq.setEnabled(false);
    eq.reset();
    fill();
    eq.processBlock(buffer, 0, 256);
    for (int ch = 0; ch < 32; ++ch)
        require(buffer.getSample(ch, 255) == 0.1f + ch * 0.001f,
            "bypassed multichannel EQ changed the dry signal");
}

void testScopeIsolation(echo::EqBandScope scope)
{
    echo::EqProcessor eq;
    eq.prepare(48000, 256, 2);
    eq.setEnabled(true);
    eq.setBandFilterType(0, echo::EqFilterType::LowShelf);
    eq.setBandFrequencyHz(0, 1000);
    eq.setBandGainDb(0, 6);
    eq.setBandProcessing(0, scope, echo::EqDynamicMode::Off, -24, 2, 5, 80);
    eq.reset();
    echo::FloatAudioBuffer buffer(2, 256);
    for (int block = 0; block < 8; ++block) {
        for (int frame = 0; frame < 256; ++frame) {
            buffer.setSample(0, frame, 0.1f);
            buffer.setSample(1, frame, 0.05f);
        }
        eq.processBlock(buffer, 0, 256);
    }
    const double gain = std::pow(10.0, 6.0 / 20);
    double left = 0.1, right = 0.05;
    if (scope == echo::EqBandScope::Left) left *= gain;
    if (scope == echo::EqBandScope::Right) right *= gain;
    if (scope == echo::EqBandScope::Mid) { left = 0.075 * gain + 0.025; right = 0.075 * gain - 0.025; }
    if (scope == echo::EqBandScope::Side) { left = 0.075 + 0.025 * gain; right = 0.075 - 0.025 * gain; }
    require(std::abs(buffer.getSample(0, 255) - left) < 1e-6
            && std::abs(buffer.getSample(1, 255) - right) < 1e-6,
        "scoped filter changed the wrong channel component");
}
}

int main()
{
    try {
        testSubFloatResidual();
        testReciprocalCascade();
        testExtendedChannels(echo::EqBandScope::Left, echo::EqDynamicMode::Off);
        testExtendedChannels(echo::EqBandScope::Stereo, echo::EqDynamicMode::Down);
        for (auto scope : {echo::EqBandScope::Left, echo::EqBandScope::Right, echo::EqBandScope::Mid, echo::EqBandScope::Side})
            testScopeIsolation(scope);
        std::cout << "PEQ double cascade, 32-channel processing, scope isolation and bypass passed\n";
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
