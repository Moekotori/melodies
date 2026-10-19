#pragma once
#include "RoutePlan.h"
#include <ostream>

namespace echo::sdmRealtimeLab {
void writeShadowMeasurements(const std::vector<Route>& routes, std::ostream& output);
void writeReferenceChainMeasurements(const std::vector<Route>& routes, std::ostream& output);
}
