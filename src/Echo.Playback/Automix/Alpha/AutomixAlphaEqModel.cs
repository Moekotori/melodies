namespace Echo.Playback;

// QPlayer MixEqMlp: Float32 normalization, double accumulation, unchanged weights.
internal sealed class AutomixAlphaEqModel
{
    private readonly double[] input = new double[36], hidden1 = new double[14], hidden2 = new double[14];

    internal bool Forward(ReadOnlySpan<float> features, Span<double> output)
    {
        if (features.Length != 36 || output.Length != 12) return false;
        for (var i = 0; i < 36; i++)
        {
            if (!float.IsFinite(features[i])) return false;
            var centered = (float)(features[i] - AutomixAlphaEqWeights.MEAN[i]);
            input[i] = (float)(centered / AutomixAlphaEqWeights.STD[i]);
        }
        for (var j = 0; j < 14; j++)
        {
            double sum = AutomixAlphaEqWeights.B1[j];
            for (var i = 0; i < 36; i++) sum += AutomixAlphaEqWeights.W1[j * 36 + i] * input[i];
            hidden1[j] = Math.Max(0, sum);
        }
        for (var j = 0; j < 14; j++)
        {
            double sum = AutomixAlphaEqWeights.B2[j];
            for (var i = 0; i < 14; i++) sum += AutomixAlphaEqWeights.W2[j * 14 + i] * hidden1[i];
            hidden2[j] = Math.Max(0, sum);
        }
        for (var k = 0; k < 12; k++)
        {
            double sum = AutomixAlphaEqWeights.B3[k];
            for (var j = 0; j < 14; j++) sum += AutomixAlphaEqWeights.W3[k * 14 + j] * hidden2[j];
            output[k] = 1 / (1 + Math.Exp(-sum));
            if (!double.IsFinite(output[k])) return false;
        }
        return true;
    }
}
