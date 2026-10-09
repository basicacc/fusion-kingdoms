namespace FusionKingdoms.Sim;

// An Essence is one mind's skill boiled down: its difference from the shared base brain (a task vector).
// Adding it to another mind grants that skill; subtracting it removes it.
public sealed class Essence
{
    static int _nextId;

    public readonly int Id = Interlocked.Increment(ref _nextId);
    public string Name = "";
    public string Source = "";
    public Axis Axis;
    public float[] Vector = [];

    public static Essence Distill(Mind m, Brain baseBrain, TrialResult baseTrial)
    {
        var v = new float[BrainSpec.WeightCount];
        for (int i = 0; i < v.Length; i++) v[i] = m.Brain.Weights[i] - baseBrain.Weights[i];
        int best = 0;
        float bestDelta = float.MinValue;
        for (int a = 0; a < Trial.AxisCount; a++)
        {
            float d = m.Trial.Scores[a] - baseTrial.Scores[a];
            if (d > bestDelta) { bestDelta = d; best = a; }
        }
        var axis = (Axis)best;
        return new Essence { Name = $"{axis} Essence", Source = m.Name, Axis = axis, Vector = v };
    }

    // strength > 0 infuses, < 0 removes.
    public Brain ApplyTo(Brain b, float strength)
    {
        var w = new float[BrainSpec.WeightCount];
        for (int i = 0; i < w.Length; i++) w[i] = b.Weights[i] + strength * Vector[i];
        return new Brain(w);
    }
}
