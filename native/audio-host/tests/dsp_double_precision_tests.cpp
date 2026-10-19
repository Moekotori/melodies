#include "DspChain.h"
#include "NativeFormatProcessor.h"
#include <cmath>
#include <iostream>
#include <stdexcept>

void testPcmFifoSamples();

// Guards against narrowing to float between active DSP stages or SRC phases.
int main()
try
{
    testPcmFifoSamples();
    echo::EqProcessor eq;
    echo::ConvolutionProcessor convolution;
    echo::ChannelBalanceProcessor balance;
    echo::DspHeadroomProcessor headroom;
    echo::ReplayGainProcessor replayGain;
    echo::CompressorProcessor compressor;
    echo::SpatialDspProcessor spatial;
    echo::PlaybackRateProcessor rate;
    echo::LevelMeterProcessor meter;
    echo::DspChain rack(eq, convolution, balance, headroom, replayGain, compressor, spatial, rate, meter);
    rack.prepare(48000, 256, 2);
    eq.setEnabled(true);
    constexpr double signal = 0.125 + 1e-12;
    echo::DoubleAudioBuffer buffer(2, 256);
    for (int channel = 0; channel < 2; ++channel)
        for (int frame = 0; frame < 256; ++frame) buffer.setSample(channel, frame, signal);
    rack.processBlock(buffer, 0, 256);
    if (std::abs(buffer.getSample(0, 255) - signal) > 1e-15)
        throw std::runtime_error("DSP rack narrowed double PCM");
    echo::EchoSrcProcessor src;
    std::string error;
    if (!src.configure(2, {{2, {0.5f, 0.5f}}}, error)) throw std::runtime_error(error);
    const double input[] = {signal, -signal, signal, -signal};
    const auto output = src.processDouble(input, 2);
    if (output.size() != 8 || std::abs(output[0] - signal) > 1e-15)
        throw std::runtime_error("SRC narrowed double PCM");
    std::cout << "Active DSP rack and SRC preserve sub-float PCM detail\n";
}
catch (const std::exception& error)
{
    std::cerr << error.what() << '\n';
    return 1;
}
