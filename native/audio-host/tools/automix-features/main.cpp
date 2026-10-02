#include "../../src/AutomixFeatureAnalysis.h"

#include <cstdlib>

int main(int argc, char* argv[]) {
    if (argc != 2) return 2;
    char* end = nullptr;
    const long rate = std::strtol(argv[1], &end, 10);
    if (end == argv[1] || *end != '\0' || rate < 8000 || rate > 48000) return 2;
    return runAutomixFeatureAnalysis(static_cast<int>(rate));
}
