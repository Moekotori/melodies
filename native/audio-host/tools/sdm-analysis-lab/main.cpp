#include "SdmAnalysisLab.h"

#include <exception>
#include <iostream>
#include <stdexcept>

int main(int argc, char** argv)
{
    try
    {
        if (argc != 2)
            throw std::runtime_error("usage: echo-sdm-analysis-lab <fir-plan>");
        echo::sdmLab::runAnalysisLab(argv[1], std::cout);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
