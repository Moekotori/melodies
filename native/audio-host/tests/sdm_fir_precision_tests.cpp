#include "FirScalarReference.h"
#include "RoutePlan.h"
#include <cmath>
#include <cstring>
#include <iostream>
#include <memory>
#include <stdexcept>

int main(int argc, char** argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: echo-sdm-fir-precision-tests <fir-plan>");
        for (const auto& route : echo::sdmRealtimeLab::readRoutes(argv[1]))
        {
            std::vector<FirReferenceStage> stages;
            for (const auto& stage : route.stages)
                stages.push_back({stage.upsampleFactor, stage.taps.data(), stage.taps.size()});
            std::unique_ptr<void, decltype(&firReferenceDestroy)> scalar(
                firReferenceCreate(2, stages.data(), stages.size()), firReferenceDestroy);
            echo::EchoSrcProcessor actual;
            std::string error;
            if (!actual.configure(2, route.stages, error)) throw std::runtime_error(error);
            // Independent channel lanes, fractional FP64 inputs, cancellation,
            // weak signals and odd block tails. Both sides run product FIRs.
            std::vector<double> input(2 * 4096);
            for (size_t index = 0; index < input.size(); ++index)
                input[index] = 0.3 * std::sin(index * 0.07123456789) + 1.0e-13 * std::cos(index * 0.123);
            for (int frames : {1, 127, 513, 4096})
            {
                const auto output = actual.processDouble(input.data(), frames);
                std::vector<double> expected(output.size());
                if (firReferenceProcessDouble(scalar.get(), input.data(), frames, expected.data(), expected.size())
                        != output.size()
                    || std::memcmp(output.data(), expected.data(), output.size() * sizeof(double)) != 0)
                    throw std::runtime_error("fir_fp64_scalar_mismatch:" + route.id);
            }
            std::cout << "PASS actual FP64 FIR vs scalar " << route.id << '\n';
        }
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
