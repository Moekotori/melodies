#include "MultibandCompressorProcessor.h"

#include <cmath>
#include <iostream>
#include <stdexcept>

namespace
{
void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

template <typename Sample>
void testLiveActivation()
{
    constexpr int blockSize = 256;
    constexpr double sampleRate = 48000.0;
    echo::MultibandCompressorProcessor processor;
    processor.prepare(sampleRate, blockSize, 2);
    require(!processor.isProcessingActive(), "initial bypass must leave the DSP chain inactive");

    auto state = processor.getState();
    state.enabled = true;
    processor.setState(state);
    // DspChain and PcmRingAudioSource consult this before dispatching any PCM.
    // Enabling during playback must work without another DSP stage or reset.
    require(processor.isProcessingActive(), "live enable must wake the DSP chain before the first block");

    echo::PlanarAudioBuffer<Sample> buffer(2, blockSize);
    double outputEnergy = 0.0;
    double inputEnergy = 0.0;
    for (int block = 0; block < 188; ++block)
    {
        for (int sample = 0; sample < blockSize; ++sample)
        {
            const auto input = static_cast<Sample>(0.4 * std::sin(
                2.0 * 3.141592653589793 * 1000.0 * (block * blockSize + sample) / sampleRate));
            for (int channel = 0; channel < 2; ++channel) buffer.setSample(channel, sample, input);
            if (block >= 94) inputEnergy += input * input;
        }
        processor.processBlock(buffer, 0, blockSize);
        if (block >= 94)
            for (int sample = 0; sample < blockSize; ++sample)
            {
                const double output = buffer.getSample(0, sample);
                require(std::isfinite(output), "live enable must keep PCM finite");
                outputEnergy += output * output;
            }
    }
    require(outputEnergy < inputEnergy * 0.8, "enabled defaults must attenuate an above-threshold tone");
    require(processor.gainReductionDb(1) > 1.0f, "host must report actual mid-band reduction");

    state.enabled = false;
    processor.setState(state);
    require(processor.isProcessingActive(), "disable must retain processing until the bypass ramp drains");
    for (int block = 0; block < 100; ++block)
    {
        for (int channel = 0; channel < 2; ++channel)
            for (int sample = 0; sample < blockSize; ++sample) buffer.setSample(channel, sample, Sample(0.1));
        processor.processBlock(buffer, 0, blockSize);
    }
    require(!processor.isProcessingActive(), "bypass must release the DSP chain after its ramp");
    require(processor.gainReductionDb(1) == 0.0f, "bypass must clear reduction telemetry");
    const auto dry = buffer;
    processor.processBlock(buffer, 0, blockSize);
    for (int sample = 0; sample < blockSize; ++sample)
        require(buffer.getSample(0, sample) == dry.getSample(0, sample), "settled bypass must preserve PCM");

    state.enabled = true;
    processor.setState(state);
    require(processor.isProcessingActive(), "reenable must wake a fully bypassed chain");
}
} // namespace

int main()
{
    try
    {
        testLiveActivation<float>();
        testLiveActivation<double>();
        std::cout << "[PASS] multiband live activation, reduction, bypass and reenable (float/double)\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "[FAIL] " << error.what() << '\n';
        return 1;
    }
}
