#pragma once

namespace echo
{
// One coefficient channel represents one input-to-output convolution path.
struct ConvolutionRoute
{
    int input = 0;
    int output = 0;
};
}
