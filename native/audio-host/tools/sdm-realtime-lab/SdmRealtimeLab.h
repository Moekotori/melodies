#pragma once
#include <ostream>
#include <string>
namespace echo::sdmRealtimeLab {
void runRealtimeLab(const std::string& planPath, std::ostream& output,
    bool focusedAdmission = false, bool smallBlocksOnly = false, bool shadowAdmission = false, bool terminalOnly = false);
}
