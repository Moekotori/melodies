#include "RoutePlan.h"
#include <cmath>
#include <filesystem>
#include <fstream>
#include <stdexcept>

namespace echo::sdmRealtimeLab {
std::vector<Route> readRoutes(const std::string& path)
{
    if (std::filesystem::file_size(path) > 2 * 1024 * 1024)
        throw std::runtime_error("realtime_plan_too_large");
    std::ifstream input(path);
    std::string magic;
    int count = 0;
    if (!(input >> magic >> count) || magic != "ECHO_SDM_REALTIME_1" || count < 1 || count > 16)
        throw std::runtime_error("realtime_plan_invalid_header");
    std::vector<Route> routes;
    for (int index = 0; index < count; ++index)
    {
        Route route;
        int stageCount = 0;
        if (!(input >> route.id >> route.sourceRate >> route.carrierRate >> stageCount)
            || route.id.empty() || route.id.size() > 64
            || route.id.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789-") != std::string::npos
            || (route.sourceRate != 44'100 && route.sourceRate != 48'000)
            || stageCount < 2 || stageCount > 5)
            throw std::runtime_error("realtime_plan_invalid_route");
        int outputRate = route.sourceRate;
        for (int stageIndex = 0; stageIndex < stageCount; ++stageIndex)
        {
            EchoSrcStageConfig stage;
            int taps = 0;
            if (!(input >> stage.upsampleFactor >> taps) || stage.upsampleFactor != 2
                || taps < 3 || taps > 8191)
                throw std::runtime_error("realtime_plan_invalid_stage");
            stage.taps.resize(static_cast<size_t>(taps));
            for (auto& tap : stage.taps)
                if (!(input >> tap) || !std::isfinite(tap))
                    throw std::runtime_error("realtime_plan_invalid_tap");
            outputRate *= 2;
            route.stages.push_back(std::move(stage));
        }
        if (outputRate != route.carrierRate)
            throw std::runtime_error("realtime_plan_rate_mismatch");
        routes.push_back(std::move(route));
    }
    std::string trailing;
    if (input >> trailing)
        throw std::runtime_error("realtime_plan_trailing_content");
    return routes;
}
}
