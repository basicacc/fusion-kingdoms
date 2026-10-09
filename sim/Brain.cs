namespace FusionKingdoms.Sim;

public sealed class Brain
{
    public readonly float[] Weights;

    public Brain(float[] weights)
    {
        if (weights.Length != BrainSpec.WeightCount)
            throw new ArgumentException($"Brain needs {BrainSpec.WeightCount} weights, got {weights.Length}");
        Weights = weights;
    }

    // Fusion as task arithmetic: each parent's difference from the shared base (its "essence") is added on top
    // of the base. The major parent's essence counts in full and the minor one's scales with its share, up to
    // full at 50/50. Both are scaled back by up to 25% as the mix evens out, because two full essences
    // interfere and crowd out everyday work (measured in training/fusion_methods.py).
    public static Brain Fuse(Brain baseBrain, Brain a, Brain b, float ratioA)
    {
        float sa = MathF.Min(1f, 2f * ratioA), sb = MathF.Min(1f, 2f * (1f - ratioA));
        float scale = 1f - 0.25f * MathF.Min(sa, sb);
        var w = new float[BrainSpec.WeightCount];
        var w0 = baseBrain.Weights;
        for (int i = 0; i < w.Length; i++)
            w[i] = w0[i] + scale * (sa * (a.Weights[i] - w0[i]) + sb * (b.Weights[i] - w0[i]));
        return new Brain(w);
    }

    // Layout matches torch nn.Linear (weight is [out, in] row-major), tanh hidden layers.
    public void Forward(ReadOnlySpan<float> obs, Span<float> logits)
    {
        Span<float> h1 = stackalloc float[BrainSpec.Hidden1];
        Span<float> h2 = stackalloc float[BrainSpec.Hidden2];
        Layer(obs, h1, BrainSpec.W1, BrainSpec.B1, true);
        Layer(h1, h2, BrainSpec.W2, BrainSpec.B2, true);
        Layer(h2, logits, BrainSpec.W3, BrainSpec.B3, false);
    }

    void Layer(ReadOnlySpan<float> x, Span<float> y, int wOff, int bOff, bool tanh)
    {
        var w = Weights;
        int n = x.Length;
        for (int o = 0; o < y.Length; o++)
        {
            float s = w[bOff + o];
            int row = wOff + o * n;
            for (int i = 0; i < n; i++) s += w[row + i] * x[i];
            y[o] = tanh ? MathF.Tanh(s) : s;
        }
    }

    public (int move, int action) Decide(ReadOnlySpan<float> obs, Random rng, bool greedy)
    {
        Span<float> logits = stackalloc float[BrainSpec.NumOutputs];
        Forward(obs, logits);
        int move = Pick(logits[..BrainSpec.NumMoves], rng, greedy);
        int action = Pick(logits[BrainSpec.NumMoves..], rng, greedy);
        return (move, action);
    }

    static int Pick(ReadOnlySpan<float> logits, Random rng, bool greedy)
    {
        int best = 0;
        for (int i = 1; i < logits.Length; i++) if (logits[i] > logits[best]) best = i;
        if (greedy) return best;
        float max = logits[best], sum = 0f;
        Span<float> p = stackalloc float[logits.Length];
        for (int i = 0; i < logits.Length; i++) { p[i] = MathF.Exp(logits[i] - max); sum += p[i]; }
        float r = (float)rng.NextDouble() * sum;
        for (int i = 0; i < p.Length; i++) { r -= p[i]; if (r <= 0f) return i; }
        return p.Length - 1;
    }
}
