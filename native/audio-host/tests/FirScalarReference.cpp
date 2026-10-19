#include "FirScalarReference.h"
#include "NativeFormatProcessor.h"
#include <algorithm>
#include <stdexcept>

void* firReferenceCreate(int channels, const FirReferenceStage* stages, size_t count)
{
    auto processor = std::make_unique<echo::EchoSrcProcessor>();
    std::vector<echo::EchoSrcStageConfig> config;
    for (size_t index = 0; index < count; ++index)
        config.push_back({stages[index].factor,
            std::vector<float>(stages[index].taps, stages[index].taps + stages[index].tapCount)});
    std::string error;
    if (!processor->configure(channels, config, error)) throw std::runtime_error(error);
    return processor.release();
}
void firReferenceDestroy(void* reference)
{
    delete static_cast<echo::EchoSrcProcessor*>(reference);
}
size_t firReferenceProcess(void* reference, const float* input, int frames,
    float* output, size_t capacity)
{
    const auto samples = static_cast<echo::EchoSrcProcessor*>(reference)->process(input, frames);
    if (samples.size() > capacity) throw std::runtime_error("fir_reference_capacity");
    std::copy(samples.begin(), samples.end(), output);
    return samples.size();
}
size_t firReferenceProcessDouble(void* reference, const double* input, int frames,
    double* output, size_t capacity)
{
    const auto samples = static_cast<echo::EchoSrcProcessor*>(reference)->processDouble(input, frames);
    if (samples.size() > capacity) throw std::runtime_error("fir_reference_capacity");
    std::copy(samples.begin(), samples.end(), output);
    return samples.size();
}
