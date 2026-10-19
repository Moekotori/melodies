#pragma once

// One-shot offline analysis. This entry point does not open an output device or
// interact with the playback daemon.
int runAutomixFeatureAnalysis(int sampleRate, bool includeAlphaFeatures = false);
