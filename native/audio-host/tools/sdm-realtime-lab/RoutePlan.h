#pragma once
#include "NativeFormatProcessor.h"
#include <string>
#include <vector>

namespace echo::sdmRealtimeLab {
struct Route {
    std::string id;
    int sourceRate = 0;
    int carrierRate = 0;
    std::vector<EchoSrcStageConfig> stages;
};
std::vector<Route> readRoutes(const std::string& path);
}
