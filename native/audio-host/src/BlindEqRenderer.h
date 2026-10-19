#pragma once
#include <string>

// Bounded, device-free preparation. Uses the production EQ/DSP processors.
int renderBlindEq(const std::string& configurationPath);
