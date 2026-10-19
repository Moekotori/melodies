#include "NativeSdmProcessor.h"
#include <algorithm>
#include <cmath>
#include <iostream>
#include <stdexcept>

namespace {
void require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}

// A snapshot must resume the real recursion exactly, including double PCM
// and the last fractional FIR carrier. DC/float-only fixtures hid narrowing.
void exactHandoff(bool dop, echo::SdmPreModulatorProfile terminal)
{
    echo::SdmProcessor continuous, resumed;
    for (auto* processor : {&continuous, &resumed})
        processor->configure(2, echo::SdmQualityProfile::Reference, 384'000,
            echo::SdmInterpolationMode::Linear, echo::SdmDecisionMode::Greedy, false, terminal);
    std::vector<double> input(2 * 8192);
    for (size_t index = 0; index < input.size(); ++index)
        input[index] = 0.13 * std::sin(static_cast<double>(index) * 0.07123456789);
    continuous.processNativeDsd(input.data(), 4093);
    continuous.setTargetGain(0.73);
    const auto captured = continuous.state();
    require(resumed.restoreState(captured), "same-configuration state must restore");
    // A pause has no DSP input; the next call is the exact continuation.
    if (dop)
        require(continuous.processDop(input.data() + 8186, 513)
            == resumed.processDop(input.data() + 8186, 513), "DoP state handoff changed bits");
    else
        require(continuous.processNativeDsd(input.data() + 8186, 513)
            == resumed.processNativeDsd(input.data() + 8186, 513), "Native DSD state handoff changed bits");
    const auto next = continuous.state();
    const auto restored = resumed.state();
    require(next.previousSamples == restored.previousSamples
        && next.errorHistory == restored.errorHistory && next.feedbackHistory == restored.feedbackHistory,
        "handoff must preserve the complete recursive state");
}

void emptyCallPreservesTail()
{
    for (bool dop : {false, true})
    {
        NativeSdmProcessor processor;
        processor.setDoubleProcessing(true);
        std::string error;
        require(processor.configure(1, echo::SdmQualityProfile::Reference, 384'000, false, 512, error),
            "tail fixture must configure");
        const double impulse = 0.1;
        if (dop)
        {
            processor.processDop(&impulse, 1);
            processor.processDop(&impulse, 0);
            require(!processor.flushDop().empty(), "empty FP64 call must not discard the DoP FIR tail");
            require(processor.flushDop().empty(), "DoP FIR tail may flush only once");
        }
        else
        {
            processor.processNativeDsd(&impulse, 1);
            processor.processNativeDsd(&impulse, 0);
            require(!processor.flushNativeDsd().empty(), "empty FP64 call must not discard the native FIR tail");
            require(processor.flushNativeDsd().empty(), "native FIR tail may flush only once");
        }
    }
}

void transportAndFamilyBoundaries()
{
    echo::SdmProcessor reused;
    reused.configure(2, echo::SdmQualityProfile::Reference, 384'000);
    std::vector<double> input(2 * 1025);
    for (size_t index = 0; index < input.size(); ++index) input[index] = 0.1 * std::sin(index * 0.0712345);
    for (int rate : {384'000, 352'800, 768'000, 705'600})
    {
        reused.processNativeDsd(input.data(), 1025);
        reused.configure(2, echo::SdmQualityProfile::Reference, rate);
        echo::SdmProcessor fresh;
        fresh.configure(2, echo::SdmQualityProfile::Reference, rate);
        require(reused.processDop(input.data(), 1025) == fresh.processDop(input.data(), 1025),
            "family reconfiguration must discard the old FIR, recursive state and marker");
    }
    echo::SdmProcessor dop, native;
    dop.configure(2, echo::SdmQualityProfile::Reference, 384'000);
    native.configure(2, echo::SdmQualityProfile::Reference, 384'000);
    const auto words = dop.processDop(input.data(), 1025);
    const auto bytes = native.processNativeDsd(input.data(), 1025);
    for (int frame = 0; frame < 1025; ++frame)
        for (int channel = 0; channel < 2; ++channel)
        {
            const uint32_t word = words[frame * 2 + channel];
            require((word >> 16) == (frame % 2 ? 0xfau : 0x05u), "DoP marker must alternate over odd blocks");
            for (int bit = 0; bit < 16; ++bit)
                require(((word >> (15 - bit)) & 1u)
                    == ((bytes[frame * 4 + (bit / 8) * 2 + channel] >> (bit % 8)) & 1u),
                    "DoP and native packets must carry the same chronological DSD bits");
        }
}
}

int main()
{
    try
    {
        for (auto terminal : {echo::SdmPreModulatorProfile::Standard,
                echo::SdmPreModulatorProfile::UltimateReference, echo::SdmPreModulatorProfile::UltimateTransient})
            for (bool dop : {false, true}) exactHandoff(dop, terminal);
        emptyCallPreservesTail();
        transportAndFamilyBoundaries();
        std::cout << "PASS six real FP64 state handoffs, gain ramp and odd DoP boundary\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL " << error.what() << '\n';
        return 1;
    }
}
