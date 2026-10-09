namespace FusionKingdoms.Sim;

public sealed class VecEnv
{
    public readonly int N;
    readonly Scenario[] _envs;
    readonly float[] _returns;
    readonly int _seed;
    int _episodes;
    double _doneSum;
    int _doneCount;
    readonly object _lock = new();

    public VecEnv(int n, MindKind kind, int seed)
    {
        N = n;
        _seed = seed;
        _envs = new Scenario[n];
        _returns = new float[n];
        // "All": a multi-task batch, each environment running a different specialist's scenario.
        for (int i = 0; i < n; i++) _envs[i] = Scenario.Create(kind == MindKind.All ? (MindKind)(1 + i % 8) : kind);
    }

    int NextSeed()
    {
        lock (_lock) return HashCode.Combine(_seed, _episodes++);
    }

    public void Reset(Span<float> obs)
    {
        for (int i = 0; i < N; i++)
        {
            _envs[i].Reset(NextSeed());
            _returns[i] = 0f;
            Observer.Build(_envs[i].W, _envs[i].Learner, obs.Slice(i * BrainSpec.In.Count, BrainSpec.In.Count));
        }
    }

    public unsafe void Step(int* moves, int* actions, float* obs, float* rew, byte* done)
    {
        Parallel.For(0, N, i =>
        {
            var env = _envs[i];
            float r = env.Step(moves[i], actions[i]);
            rew[i] = r;
            _returns[i] += r;
            done[i] = env.Done ? (byte)1 : (byte)0;
            if (env.Done)
            {
                lock (_lock) { _doneSum += _returns[i]; _doneCount++; }
                _returns[i] = 0f;
                env.Reset(NextSeed());
            }
            Observer.Build(env.W, env.Learner, new Span<float>(obs + i * BrainSpec.In.Count, BrainSpec.In.Count));
        });
    }

    public void SetContextChance(float chance)
    {
        foreach (var e in _envs) e.ContextChance = chance;
    }

    // Whether each env's current episode has its specialist trigger present (matches the latest observations).
    public unsafe void ContextFlags(byte* outFlags)
    {
        for (int i = 0; i < N; i++) outFlags[i] = _envs[i].InContext ? (byte)1 : (byte)0;
    }

    public (double mean, int count) TakeStats()
    {
        lock (_lock)
        {
            var res = (_doneCount > 0 ? _doneSum / _doneCount : 0.0, _doneCount);
            _doneSum = 0;
            _doneCount = 0;
            return res;
        }
    }

    // context: -1 = random as in training, 0 = trigger absent, 1 = trigger present. specialOnly scores just the specialty.
    public static double Evaluate(Brain brain, MindKind kind, int episodes, int seed, bool greedy, int context = -1, bool specialOnly = false)
    {
        var totals = new double[episodes];
        Parallel.For(0, episodes, e =>
        {
            var sc = Scenario.Create(kind);
            if (context >= 0) sc.ContextChance = context;
            sc.SpecialOnly = specialOnly;
            sc.Reset(unchecked(seed * 7919 + e * 104729 + 13));
            var obs = new float[BrainSpec.In.Count];
            var rng = new Random(unchecked(seed * 31 + e * 977 + 5));
            double total = 0;
            while (!sc.Done)
            {
                Observer.Build(sc.W, sc.Learner, obs);
                var (m, a) = brain.Decide(obs, rng, greedy);
                total += sc.Step(m, a);
            }
            totals[e] = total;
        });
        return totals.Average();
    }
}
