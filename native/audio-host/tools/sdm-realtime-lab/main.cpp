#include "SdmRealtimeLab.h"
#include "SdmShadowMeasurement.h"
#include <iomanip>
#include <iostream>
#include <stdexcept>
int main(int argc, char** argv)
{
    try
    {
        if (argc == 3 && std::string(argv[2]) == "--reference-chain")
        {
            std::cout << std::setprecision(12) << "{\"schemaVersion\":1,\"mode\":\"reference-chain\","
                << "\"packingAndSynchronizationIncludedInSdm\":true,\"deviceUnderruns\":null,\"measurements\":[";
            echo::sdmRealtimeLab::writeReferenceChainMeasurements(echo::sdmRealtimeLab::readRoutes(argv[1]), std::cout);
            std::cout << "]}\n";
            return 0;
        }
        const bool focused = argc == 3 && std::string(argv[2]) == "--focused-admission";
        const bool small = argc == 3 && std::string(argv[2]) == "--small-blocks-only";
        const bool shadow = argc == 3 && std::string(argv[2]) == "--shadow-admission";
        const bool terminal = argc == 3 && std::string(argv[2]) == "--terminal-cost-only";
        if (argc != 2 && !focused && !small && !shadow && !terminal)
            throw std::runtime_error("usage: echo-sdm-realtime-lab <fir-plan> [--focused-admission|--small-blocks-only|--shadow-admission]");
        echo::sdmRealtimeLab::runRealtimeLab(argv[1], std::cout, focused, small, shadow, terminal);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
