#pragma once

namespace echo
{
// REW exports 128k-tap filters. Keep the aggregate processing budget bounded.
inline constexpr int convolutionMaxTaps = 131072;
inline constexpr int convolutionMaxChannels = 32;
inline constexpr int convolutionMaxCoefficients = 262144;
}
