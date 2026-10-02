#pragma once

namespace automix_handoff_tests {

inline void testSchedule()
{
    const AutomixHandoffSchedule early { 0.15, 0.20, 0.40, 0.50, 0.25, 0.50 };
    require(early.valid() && AutomixHandoffSchedule{}.valid(), "scheduled and legacy handoffs must validate");
    auto invalid = early;
    invalid.lowEnd = invalid.lowStart;
    require(!invalid.valid(), "zero-length swap must not reach the callback");
    invalid = early;
    invalid.holdStart = std::numeric_limits<double>::quiet_NaN();
    require(!invalid.valid(), "non-finite schedules must not reach the callback");
    double previous = -1.0;
    for (int i = 0; i <= 1000; ++i)
    {
        const double progress = i / 1000.0;
        const double warped = automixHeldProgress(progress, early.holdStart, early.holdEnd);
        require(warped >= previous && warped >= 0.0 && warped <= 1.0, "held envelope must be bounded and monotonic");
        const double out = std::cos(warped * 1.5707963267948966);
        const double in = std::sin(warped * 1.5707963267948966);
        requireNear(out * out + in * in, 1.0, 1e-12, "plateau must preserve equal-power gain law");
        requireNear(automixHeldProgress(progress, 0.0, 0.0), progress, 0.0, "legacy envelope must remain unchanged");
        previous = warped;
    }
    requireNear(automixHeldProgress(0.3, 0.25, 0.5), 0.5, 0.0, "plateau must hold both decks");

    echo::EqProcessor eq;
    echo::ChannelBalanceProcessor balance;
    PcmRingAudioSource source(2, 2048, 0, 0, 1.0f, eq, balance);
    source.prepareForNativeRender(1024, 48000.0);
    const std::vector<float> current(2048, 0.2f);
    const std::vector<float> next(2048, 0.0f);
    const auto render = [&](AutomixHandoffSchedule schedule) {
        source.beginSession();
        require(source.prepareAutomixFrames(64, 768, 0.0, 0.0, 0.0, 0.0, true, false,
                    false, 0.25, 0.6, 0.45, 0.55, 0.7, schedule), "scheduled handoff must arm");
        require(source.push(current.data(), 1024) && source.pushAutomixNext(next.data(), 1024), "scheduled decks must accept PCM");
        auto output = makeBuffer(2, 1024);
        require(source.renderPlanar(output, 0, 1024) == 1024, "scheduled handoff must render");
        return std::abs(output.getSample(0, 490));
    };
    const float earlyBass = render(early);
    const float legacyBass = render({});
    require(legacyBass > 0.15f && earlyBass < legacyBass * 0.1f,
        "native output must follow the planned bass swap, then restore legacy timing on a new plan");
    requireNear(render(invalid), legacyBass, 1e-4, "invalid schedules must use safe legacy timing");
}

} // namespace automix_handoff_tests
