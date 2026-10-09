namespace FusionKingdoms.Sim;

// Mutation Pit: random weight noise, scaled per layer so every layer is shaken by the same relative amount.
public static class Mutation
{
    public const int Mutants = 6;
    public static float Strength(int level) => level switch { 1 => 0.08f, 2 => 0.18f, _ => 0.35f };

    static readonly (int start, int end)[] Layers =
    [
        (BrainSpec.W1, BrainSpec.B1), (BrainSpec.B1, BrainSpec.W2),
        (BrainSpec.W2, BrainSpec.B2), (BrainSpec.B2, BrainSpec.W3),
        (BrainSpec.W3, BrainSpec.B3), (BrainSpec.B3, BrainSpec.WeightCount),
    ];

    public static Brain Mutate(Brain b, int level, Random rng)
    {
        var w = (float[])b.Weights.Clone();
        float strength = Strength(level);
        foreach (var (s, e) in Layers)
        {
            double sq = 0;
            for (int i = s; i < e; i++) sq += w[i] * w[i];
            float scale = strength * MathF.Max(0.05f, (float)Math.Sqrt(sq / (e - s)));
            for (int i = s; i < e; i++) w[i] += scale * Gaussian(rng);
        }
        return new Brain(w);
    }

    static float Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
