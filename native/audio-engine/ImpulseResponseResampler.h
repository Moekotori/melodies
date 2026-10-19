#pragma once
#include <vector>

namespace echo {
struct ImpulseResamplingPlan {
    int inputPadding = 0;
    int outputFrames = 0;
    int latencySamples = 0;
    bool valid = false;
};
struct ResampledImpulseResponse {
    std::vector<std::vector<float>> taps;
    int latencySamples = 0;
};
struct ResampledDoubleImpulseResponse {
    std::vector<std::vector<double>> taps;
    int latencySamples = 0;
};
// Zero padding retains the complete band-limited response; its delay is explicit.
ImpulseResamplingPlan planImpulseResampling(int frames, double sourceRate, double targetRate, int processingChannels);
ResampledImpulseResponse resampleImpulseResponse(const std::vector<std::vector<float>>& source,
    double sourceRate, double targetRate, int processingChannels);
ResampledDoubleImpulseResponse resampleImpulseResponse(const std::vector<std::vector<double>>& source,
    double sourceRate, double targetRate, int processingChannels);
}
