#pragma once
// Establish immutable FIFO/decoder geometry through RPC before allocation.
bool initializeNativeDaemonChannels(int inputFd, int outputFd, int& channels);
