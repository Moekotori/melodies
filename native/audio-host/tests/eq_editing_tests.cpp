#include "EqProcessor.h"
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <thread>

void require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
float render(echo::EqProcessor& eq, float frequency, int frames = 8192)
{
    echo::FloatAudioBuffer buffer(2, frames);
    for (int ch = 0; ch < 2; ++ch)
        for (int i = 0; i < frames; ++i) buffer.getWritePointer(ch)[i] = 0.1f * std::sin(6.283185307179586 * frequency * i / 48000.0);
    eq.processBlock(buffer, 0, frames);
    double energy = 0;
    for (int i = frames / 2; i < frames; ++i) {
        const float sample = buffer.getReadPointer(0)[i];
        require(std::isfinite(sample) && std::abs(sample) < 1.0f, "non-finite or excessive output");
        require(std::abs(sample - buffer.getReadPointer(1)[i]) < 1e-6f, "stereo monitor mismatch");
        energy += sample * sample;
    }
    return std::sqrt(energy / (frames / 2));
}
int main()
{
    try {
        echo::EqProcessor eq;
        eq.prepare(48000, 8192, 2);
        eq.setEnabled(true); eq.setBandFrequencyHz(17, 1000); eq.setBandGainDb(17, 6);
        eq.reset(); const float full = render(eq, 1000);
        eq.setStrengthPercent(0); eq.reset(); const float flat = render(eq, 1000);
        require(full > flat * 1.9f, "strength did not scale response");
        require(eq.getState().bandGainsDb[17] == 6, "strength destroyed original parameters");
        eq.setStrengthPercent(100); eq.reset(); require(std::abs(render(eq, 1000) - full) < 0.001f, "strength restore differs");
        eq.setEnabled(false); eq.reset();
        require(eq.setAudition(true, 1000, 2), "audition rejected");
        const float center = render(eq, 1000), outside = render(eq, 100);
        require(center > outside * 8, "audition is not band limited");
        eq.setAudition(true, 3000, 3); render(eq, 3000);
        require(!eq.getState().enabled && eq.getState().bandGainsDb[17] == 6, "monitor mutated EQ state");
        eq.setAudition(false, 0, 0); const float restored = render(eq, 1000);
        require(std::abs(restored - flat) < 0.001f && !eq.isAuditionProcessing(), "release did not restore normal output");
        require(!eq.setAudition(true, NAN, 1), "accepted non-finite audition");
        eq.setAudition(true, 1000, 1); render(eq, 1000);
        std::this_thread::sleep_for(std::chrono::milliseconds(780));
        require(!eq.isAuditionActive(), "host lease did not expire");
        render(eq, 1000); require(!eq.isAuditionProcessing(), "lease fade did not finish");
        std::cout << "EQ strength, monitor, sweep, release and lease tests passed\n";
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
