// Included in the quality lab's measurement namespace to reuse its actual
// stress, source-error and small-signal algorithms without a second DSP model.
// This bounded audit is deliberately separate from the full comparison grid.
int runSdmP0Audit()
{
    const RateSpec rates[] {{"DSD128", 352'800}, {"DSD128-48k", 384'000},
        {"DSD256", 705'600}, {"DSD256-48k", 768'000}};
    const ProfileSpec profiles[] {{"hifi", echo::SdmQualityProfile::Hifi},
        {"reference", echo::SdmQualityProfile::Reference}, {"insane", echo::SdmQualityProfile::Insane}};
    const InterpolationSpec linear {"linear", echo::SdmInterpolationMode::Linear};
    bool clear = true;
    std::cout << std::setprecision(12)
        << "{\"schemaVersion\":1,\"generator\":\"echo-sdm-p0-quality-audit\","
        << "\"hardwareProof\":false,\"decision\":\"greedy\",\"measurements\":[";
    int row = 0;
    for (const auto& rate : rates)
        for (const auto& profile : profiles)
        {
            const auto nominal = measure(rate, profile, linear);
            const auto weak = measureSmallSignal(rate, profile, false, echo::SdmDecisionMode::Greedy, 1.0e-6);
            std::uint64_t recoveries = 0;
            echo::SdmClampCounts clamps;
            double peak = 0.0;
            for (std::uint32_t seed : {0x12345678u, 0xdeadbeefu, 0x9e3779b9u})
            {
                const auto stress = measureFullScaleStress(rate, profile, echo::SdmDecisionMode::Greedy, seed);
                recoveries += stress.stabilityRecoveries;
                clamps = echo::addSdmClampCounts(clamps, stress.clampCounts);
                peak = std::max(peak, stress.peakFeedbackState);
            }
            // Analyze the real packed native bits after the transition ramp.
            // Bounded 2^17-sample window; no PCM/DSD capture stays resident.
            echo::SdmProcessor processor;
            processor.configure(1, profile.profile, rate.transportSampleRate);
            const auto config = processor.configuration();
            const auto input = createSine(rate.transportSampleRate, rate.transportSampleRate / 8, 1000.0, 0.1);
            const auto bytes = processor.processNativeDsd(input.data(), static_cast<int>(input.size()));
            const size_t firstByte = static_cast<size_t>(rate.transportSampleRate / 20) * 2;
            std::vector<double> bits(1u << 17u);
            for (size_t bit = 0; bit < bits.size(); ++bit)
                bits[bit] = (bytes[firstByte + bit / 8] & (1u << (bit % 8))) ? 1.0 : -1.0;
            const std::array<echo::sdmLab::FrequencyBand, 4> bands {{
                {20, 20'000}, {20'000, 100'000}, {100'000, 1'000'000},
                {1'000'000, rate.transportSampleRate * 8.0}}};
            const auto spectrum = echo::sdmLab::analyzeSpectrum(bits, rate.transportSampleRate * 16.0, bands);
            clear = clear && recoveries == 0 && clamps.input == 0 && clamps.loopInput == 0
                && clamps.quantizationError == 0 && nominal.deterministic && nominal.chunkInvariant;
            std::cout << (row++ ? "," : "") << "{\"rate\":\"" << rate.name
                << "\",\"profile\":\"" << profile.name << "\",\"headroomDb\":" << nominal.profileHeadroomDb
                << ",\"ntfPeakGain\":" << nominal.ntfPeakGain
                << ",\"ecEnabled\":" << (config.extendedCompensationEnabled ? "true" : "false")
                << ",\"stressSeeds\":3,\"stressRecoveries\":" << recoveries
                << ",\"stressPeakFeedback\":" << peak << ",\"inputClamps\":" << clamps.input
                << ",\"loopClamps\":" << clamps.loopInput << ",\"errorClamps\":" << clamps.quantizationError
                << ",\"inBandSourceErrorDb\":" << nominal.inBandResidualDb
                << ",\"weak120DbfsSourceErrorDb\":" << weak.inBandResidualDb
                << ",\"weakRecoveries\":" << weak.stabilityRecoveries
                << ",\"deterministic\":" << (nominal.deterministic ? "true" : "false")
                << ",\"chunkInvariant\":" << (nominal.chunkInvariant ? "true" : "false")
                << ",\"checksum\":" << nominal.checksum
                << ",\"ultrasonicBandPowerDb\":[" << spectrum.bandPowerDb[1]
                << ',' << spectrum.bandPowerDb[2] << ',' << spectrum.bandPowerDb[3] << "]}";
        }
    std::cout << "],\"stressGuardrailsClear\":" << (clear ? "true" : "false") << "}\n";
    return clear ? 0 : 1;
}
