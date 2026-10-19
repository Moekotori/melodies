#include "EqProcessor.h"
#include "HeadphoneCorrectionProtocol.h"
#include <cmath>
#include <iostream>
#include <stdexcept>

namespace
{
void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}
}

// Guards author gain/preamp/frequency/Q against personal-EQ limits in the
// actual native processor, and against the old 0.45*Fs coefficient clamp.
int main()
{
    echo::EqProcessor correction { true };
    echo::EqProcessor personal;
    correction.resetFlat();
    correction.setEnabled(true);
    correction.setPreampDb(-18.1f);
    correction.setBandFrequencyHz(0, 20000.0f);
    correction.setBandGainDb(0, 20.0f);
    correction.setBandQ(0, 1.0f);
    correction.setBandFrequencyHz(1, 8.5f);
    correction.setBandFilterType(1, echo::EqFilterType::LowShelf);
    correction.setBandQ(1, 0.048f);
    auto state = correction.getState();
    require(std::abs(state.preampDb + 18.1) < 0.00001, "published preamp changed");
    require(state.bandGainsDb[0] == 20, "published boost changed");
    require(state.bandFrequenciesHz[1] == 8.5, "sub-20 Hz center changed");
    require(std::abs(state.bandQ[1] - 0.048) < 0.00001, "shelf slope changed");
    personal.setBandGainDb(0, 20);
    require(personal.getState().bandGainsDb[0] == echo::eqMaxGainDb, "personal EQ domain changed");

    // A cookbook bell has exactly its published gain at its center. Use actual
    // native audio processing at 44.1 kHz, where 20 kHz exceeds the old clamp.
    constexpr double rate = 44100;
    constexpr int blockSize = 512;
    correction.prepare(rate, blockSize, 1);
    echo::PlanarAudioBuffer<double> buffer(1, blockSize);
    double inputEnergy = 0, outputEnergy = 0;
    for (int block = 0; block < 40; ++block)
    {
        for (int sample = 0; sample < blockSize; ++sample)
        {
            const auto value = 0.01 * std::sin(2 * 3.14159265358979323846 * 20000
                * (block * blockSize + sample) / rate);
            buffer.setSample(0, sample, value);
            if (block >= 20) inputEnergy += value * value;
        }
        correction.processBlock(buffer, 0, blockSize);
        if (block >= 20)
            for (int sample = 0; sample < blockSize; ++sample)
                outputEnergy += std::pow(buffer.getSample(0, sample), 2);
    }
    const double measuredDb = 10 * std::log10(outputEnergy / inputEnergy);
    require(std::abs(measuredDb - 1.9) < 0.001, "author response or preamp changed during processing");

    nlohmann::json profile = { { "preampDb", -18.1 }, { "bands", nlohmann::json::array() } };
    for (int band = 0; band < echo::eqBandCount; ++band)
        profile["bands"].push_back({ {"frequencyHz", 1000}, {"gainDb", 20}, {"q", 1}, {"filterType", "peaking"} });
    require(echo::validHeadphoneCorrectionProfile(profile), "supported correction rejected");
    profile["bands"][0]["q"] = 0;
    require(!echo::validHeadphoneCorrectionProfile(profile), "invalid correction accepted");
    std::cout << "Native author parameters and center response preserved: " << measuredDb << " dB\n";
}
