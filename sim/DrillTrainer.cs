namespace FusionKingdoms.Sim;

// Training Grounds: real reinforcement learning on a drill. Each generation the subject plays a batch of drill
// episodes with its own brain, then the brain is nudged by backpropagation toward the choices that led to better
// than usual outcomes (PPO with a clipped objective, the same family of method that trained the base minds).
public sealed class DrillTrainer
{
    public const int DrillSteps = 200;
    public int Episodes = 24;
    public int Epochs = 4;
    public int MiniBatch = 512;
    public float LearningRate = 1e-3f;
    public float Gamma = 0.99f;
    public float Lambda = 0.95f;
    public float Clip = 0.2f;
    public float EntropyBonus = 0.003f;

    public Brain Brain { get; private set; }
    public readonly MindKind Drill;
    // Replaced (never mutated) each generation, so the UI can read it while training runs on another thread.
    public List<float> Curve { get; private set; } = new();
    public int Iteration { get; private set; }
    public float LastMove, LastClipFrac, LastEntropy;   // diagnostics

    readonly Random _rng;
    readonly Func<Scenario> _make;
    readonly float[] _m, _v;
    readonly Critic _critic;
    int _adamT, _clipped, _seen;
    float _ent;

    public DrillTrainer(Brain start, MindKind drill, int seed) : this(start, drill, () => Scenario.Create(drill), seed) { }

    // A custom drill, such as the Mirror Drill built from recordings of the real enemy.
    public DrillTrainer(Brain start, MindKind drill, Func<Scenario> make, int seed)
    {
        Brain = new Brain((float[])start.Weights.Clone());
        Drill = drill;
        _make = make;
        _rng = new Random(seed);
        _m = new float[BrainSpec.WeightCount];
        _v = new float[BrainSpec.WeightCount];
        _critic = new Critic(_rng);
    }

    public static float RunDrill(Brain brain, MindKind drill, int seed, int steps = DrillSteps) =>
        RunDrill(brain, () => Scenario.Create(drill), seed, steps);

    public static float RunDrill(Brain brain, Func<Scenario> make, int seed, int steps = DrillSteps)
    {
        var sc = make();
        sc.ContextChance = 1f;
        sc.Reset(seed);
        var obs = new float[BrainSpec.In.Count];
        var rng = new Random(seed ^ 0x5bd1e995);
        float total = 0f;
        for (int i = 0; i < steps && !sc.Done; i++)
        {
            Observer.Build(sc.W, sc.Learner, obs);
            var (m, a) = brain.Decide(obs, rng, false);
            total += sc.Step(m, a);
        }
        return total;
    }

    sealed class Rollout
    {
        public readonly List<float[]> Obs = new();
        public readonly List<int> Move = new(), Act = new();
        public readonly List<float> LogP = new(), Reward = new();
    }

    // One generation. Returns the batch's mean drill score, which is what the learning curve shows.
    public float Step()
    {
        const int nIn = BrainSpec.In.Count, nM = BrainSpec.NumMoves, nOut = BrainSpec.NumOutputs;
        var brain = Brain;
        int iterSeed = _rng.Next();
        var rolls = new Rollout[Episodes];
        // Leave half the cores to the game so drilling never stutters the frame rate.
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
        Parallel.For(0, Episodes, opts, e =>
        {
            var sc = _make();
            sc.ContextChance = 1f;
            sc.Reset(HashCode.Combine(iterSeed, e));
            var rng = new Random(HashCode.Combine(iterSeed, e, 7));
            var r = new Rollout();
            Span<float> logits = stackalloc float[nOut];
            for (int t = 0; t < DrillSteps && !sc.Done; t++)
            {
                var obs = new float[nIn];
                Observer.Build(sc.W, sc.Learner, obs);
                brain.Forward(obs, logits);
                int mv = Sample(logits[..nM], rng), ac = Sample(logits[nM..], rng);
                r.Obs.Add(obs);
                r.Move.Add(mv);
                r.Act.Add(ac);
                r.LogP.Add(LogSoftmax(logits[..nM], mv) + LogSoftmax(logits[nM..], ac));
                r.Reward.Add(sc.Step(mv, ac));
            }
            rolls[e] = r;
        });

        // Generalized advantage estimation against the critic's value guesses.
        var obsAll = new List<float[]>();
        var mvAll = new List<int>();
        var acAll = new List<int>();
        var lpAll = new List<float>();
        var advAll = new List<float>();
        var retAll = new List<float>();
        foreach (var r in rolls)
        {
            int T = r.Reward.Count;
            var v = new float[T + 1];
            for (int t = 0; t < T; t++) v[t] = _critic.Value(r.Obs[t]);
            var adv = new float[T];
            float gae = 0f;
            for (int t = T - 1; t >= 0; t--)
            {
                float delta = r.Reward[t] + Gamma * v[t + 1] - v[t];
                adv[t] = gae = delta + Gamma * Lambda * gae;
            }
            for (int t = 0; t < T; t++)
            {
                obsAll.Add(r.Obs[t]);
                mvAll.Add(r.Move[t]);
                acAll.Add(r.Act[t]);
                lpAll.Add(r.LogP[t]);
                advAll.Add(adv[t]);
                retAll.Add(adv[t] + v[t]);
            }
        }
        int N = obsAll.Count;
        float mean = advAll.Average(), std = MathF.Sqrt(advAll.Average(a => (a - mean) * (a - mean))) + 1e-6f;
        for (int i = 0; i < N; i++) advAll[i] = (advAll[i] - mean) / std;

        var w = (float[])brain.Weights.Clone();
        var idx = Enumerable.Range(0, N).ToArray();
        var grad = new float[w.Length];
        for (int epoch = 0; epoch < Epochs; epoch++)
        {
            Shuffle(idx);
            for (int start = 0; start < N; start += MiniBatch)
            {
                int end = Math.Min(N, start + MiniBatch);
                Array.Clear(grad);
                for (int k = start; k < end; k++)
                {
                    int i = idx[k];
                    Backprop(w, obsAll[i], mvAll[i], acAll[i], lpAll[i], advAll[i], grad);
                }
                Adam(w, grad, 1f / (end - start));
                _critic.Train(obsAll, retAll, idx, start, end);
            }
        }
        float moved = 0f;
        for (int i = 0; i < w.Length; i++) moved += MathF.Abs(w[i] - brain.Weights[i]);
        LastMove = moved / w.Length;
        LastClipFrac = _clipped / (float)Math.Max(1, _seen);
        LastEntropy = _ent / Math.Max(1, _seen);
        _clipped = _seen = 0;
        _ent = 0f;
        Brain = new Brain(w);
        Iteration++;
        float score = rolls.Average(r => r.Reward.Sum());
        Curve = [.. Curve, score];
        return score;
    }

    // Gradient of the clipped PPO loss (minus an entropy bonus) for one sample, accumulated into grad.
    void Backprop(float[] w, float[] x, int mv, int ac, float oldLogP, float adv, float[] grad)
    {
        const int nIn = BrainSpec.In.Count, h1n = BrainSpec.Hidden1, h2n = BrainSpec.Hidden2, nOut = BrainSpec.NumOutputs, nM = BrainSpec.NumMoves;
        Span<float> h1 = stackalloc float[h1n], h2 = stackalloc float[h2n], z = stackalloc float[nOut];
        for (int o = 0; o < h1n; o++)
        {
            float s = w[BrainSpec.B1 + o];
            int row = BrainSpec.W1 + o * nIn;
            for (int i = 0; i < nIn; i++) s += w[row + i] * x[i];
            h1[o] = MathF.Tanh(s);
        }
        for (int o = 0; o < h2n; o++)
        {
            float s = w[BrainSpec.B2 + o];
            int row = BrainSpec.W2 + o * h1n;
            for (int i = 0; i < h1n; i++) s += w[row + i] * h1[i];
            h2[o] = MathF.Tanh(s);
        }
        for (int o = 0; o < nOut; o++)
        {
            float s = w[BrainSpec.B3 + o];
            int row = BrainSpec.W3 + o * h2n;
            for (int i = 0; i < h2n; i++) s += w[row + i] * h2[i];
            z[o] = s;
        }
        Span<float> p = stackalloc float[nOut];
        Softmax(z[..nM], p[..nM]);
        Softmax(z[nM..], p[nM..]);
        float logP = MathF.Log(p[mv] + 1e-8f) + MathF.Log(p[nM + ac] + 1e-8f);
        float ratio = MathF.Exp(logP - oldLogP);
        bool clipped = (adv > 0f && ratio > 1f + Clip) || (adv < 0f && ratio < 1f - Clip);
        _seen++;
        if (clipped) _clipped++;
        float dLogP = clipped ? 0f : -ratio * adv;   // d(loss)/d(logP)

        // d(loss)/d(logits): policy term plus the entropy bonus of each head.
        Span<float> dz = stackalloc float[nOut];
        for (int head = 0; head < 2; head++)
        {
            int off = head == 0 ? 0 : nM, n = head == 0 ? nM : nOut - nM, chosen = head == 0 ? mv : ac;
            float ent = 0f;
            for (int i = 0; i < n; i++) ent -= p[off + i] * MathF.Log(p[off + i] + 1e-8f);
            _ent += ent;
            for (int i = 0; i < n; i++)
            {
                float pi = p[off + i];
                float g = dLogP * ((i == chosen ? 1f : 0f) - pi);
                g += EntropyBonus * pi * (MathF.Log(pi + 1e-8f) + ent);   // minus d(entropy)/dz
                dz[off + i] = g;
            }
        }

        Span<float> d2 = stackalloc float[h2n];
        for (int o = 0; o < nOut; o++)
        {
            float g = dz[o];
            if (g == 0f) continue;
            grad[BrainSpec.B3 + o] += g;
            int row = BrainSpec.W3 + o * h2n;
            for (int i = 0; i < h2n; i++) { grad[row + i] += g * h2[i]; d2[i] += g * w[row + i]; }
        }
        Span<float> d1 = stackalloc float[h1n];
        for (int o = 0; o < h2n; o++)
        {
            float g = d2[o] * (1f - h2[o] * h2[o]);
            grad[BrainSpec.B2 + o] += g;
            int row = BrainSpec.W2 + o * h1n;
            for (int i = 0; i < h1n; i++) { grad[row + i] += g * h1[i]; d1[i] += g * w[row + i]; }
        }
        for (int o = 0; o < h1n; o++)
        {
            float g = d1[o] * (1f - h1[o] * h1[o]);
            grad[BrainSpec.B1 + o] += g;
            int row = BrainSpec.W1 + o * nIn;
            for (int i = 0; i < nIn; i++) grad[row + i] += g * x[i];
        }
    }

    // Compares the analytic gradient with finite differences of the loss on one sample. Returns the worst relative error.
    public static float GradCheck(Brain b, int seed)
    {
        var rng = new Random(seed);
        var t = new DrillTrainer(b, MindKind.Base, seed) { EntropyBonus = 0.01f };
        var x = Enumerable.Range(0, BrainSpec.In.Count).Select(_ => (float)rng.NextDouble() * 2 - 1).ToArray();
        int mv = 3, ac = 5;
        float adv = 0.7f, oldLogP = -3.5f;
        var w = (float[])b.Weights.Clone();
        var grad = new float[w.Length];
        t.Backprop(w, x, mv, ac, oldLogP, adv, grad);
        float Loss(float[] ww)
        {
            Span<float> z = stackalloc float[BrainSpec.NumOutputs];
            new Brain(ww).Forward(x, z);
            int nM = BrainSpec.NumMoves;
            Span<float> p = stackalloc float[BrainSpec.NumOutputs];
            Softmax(z[..nM], p[..nM]);
            Softmax(z[nM..], p[nM..]);
            float logP = MathF.Log(p[mv]) + MathF.Log(p[nM + ac]);
            float ratio = MathF.Exp(logP - oldLogP);
            float ent = 0f;
            for (int i = 0; i < p.Length; i++) ent -= p[i] * MathF.Log(p[i]);
            return -ratio * adv - t.EntropyBonus * ent;
        }
        float worst = 0f;
        foreach (int i in new[] { BrainSpec.W1 + 5, BrainSpec.B1 + 3, BrainSpec.W2 + 77, BrainSpec.B2 + 9, BrainSpec.W3 + 40, BrainSpec.B3 + 2, BrainSpec.B3 + 14 })
        {
            const float h = 1e-3f;
            var a = (float[])w.Clone(); a[i] += h;
            var c = (float[])w.Clone(); c[i] -= h;
            float num = (Loss(a) - Loss(c)) / (2 * h);
            float err = MathF.Abs(num - grad[i]) / MathF.Max(1e-4f, MathF.Abs(num) + MathF.Abs(grad[i]));
            Console.WriteLine($"  w[{i}] numeric {num:0.000000} analytic {grad[i]:0.000000}");
            worst = MathF.Max(worst, err);
        }
        return worst;
    }

    void Adam(float[] w, float[] grad, float scale)
    {
        const float b1 = 0.9f, b2 = 0.999f;
        _adamT++;
        float c1 = 1f - MathF.Pow(b1, _adamT), c2 = 1f - MathF.Pow(b2, _adamT);
        for (int i = 0; i < w.Length; i++)
        {
            float g = grad[i] * scale;
            _m[i] = b1 * _m[i] + (1 - b1) * g;
            _v[i] = b2 * _v[i] + (1 - b2) * g * g;
            w[i] -= LearningRate * (_m[i] / c1) / (MathF.Sqrt(_v[i] / c2) + 1e-8f);
        }
    }

    static void Softmax(ReadOnlySpan<float> z, Span<float> p)
    {
        float max = z[0];
        for (int i = 1; i < z.Length; i++) max = MathF.Max(max, z[i]);
        float sum = 0f;
        for (int i = 0; i < z.Length; i++) { p[i] = MathF.Exp(z[i] - max); sum += p[i]; }
        for (int i = 0; i < z.Length; i++) p[i] /= sum;
    }

    static float LogSoftmax(ReadOnlySpan<float> z, int k)
    {
        float max = z[0];
        for (int i = 1; i < z.Length; i++) max = MathF.Max(max, z[i]);
        float sum = 0f;
        for (int i = 0; i < z.Length; i++) sum += MathF.Exp(z[i] - max);
        return z[k] - max - MathF.Log(sum);
    }

    static int Sample(ReadOnlySpan<float> z, Random rng)
    {
        Span<float> p = stackalloc float[z.Length];
        Softmax(z, p);
        float r = (float)rng.NextDouble();
        for (int i = 0; i < p.Length; i++) { r -= p[i]; if (r <= 0f) return i; }
        return p.Length - 1;
    }

    void Shuffle(int[] a)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    // A small value network that only lives during training: it guesses how well an episode will go from here,
    // which turns noisy episode scores into a much cleaner learning signal.
    sealed class Critic
    {
        const int H = 64;
        static readonly int NIn = BrainSpec.In.Count;
        readonly float[] _w, _m, _v;
        int _t;

        public Critic(Random rng)
        {
            _w = new float[H * NIn + H + H + 1];
            float s = 1f / MathF.Sqrt(NIn);
            for (int i = 0; i < H * NIn; i++) _w[i] = ((float)rng.NextDouble() * 2 - 1) * s;
            for (int i = 0; i < H; i++) _w[H * NIn + H + i] = ((float)rng.NextDouble() * 2 - 1) * 0.1f;
            _m = new float[_w.Length];
            _v = new float[_w.Length];
        }

        public float Value(float[] x)
        {
            Span<float> h = stackalloc float[H];
            return Forward(x, h);
        }

        float Forward(float[] x, Span<float> h)
        {
            int b1 = H * NIn, w2 = b1 + H, b2 = w2 + H;
            float y = _w[b2];
            for (int o = 0; o < H; o++)
            {
                float s = _w[b1 + o];
                for (int i = 0; i < NIn; i++) s += _w[o * NIn + i] * x[i];
                h[o] = MathF.Tanh(s);
                y += _w[w2 + o] * h[o];
            }
            return y;
        }

        public void Train(List<float[]> obs, List<float> ret, int[] idx, int start, int end)
        {
            int b1 = H * NIn, w2 = b1 + H, b2 = w2 + H;
            var g = new float[_w.Length];
            Span<float> h = stackalloc float[H];
            for (int k = start; k < end; k++)
            {
                var x = obs[idx[k]];
                float err = Forward(x, h) - ret[idx[k]];
                g[b2] += err;
                for (int o = 0; o < H; o++)
                {
                    g[w2 + o] += err * h[o];
                    float d = err * _w[w2 + o] * (1f - h[o] * h[o]);
                    g[b1 + o] += d;
                    for (int i = 0; i < NIn; i++) g[o * NIn + i] += d * x[i];
                }
            }
            _t++;
            float c1 = 1f - MathF.Pow(0.9f, _t), c2 = 1f - MathF.Pow(0.999f, _t), scale = 1f / (end - start);
            for (int i = 0; i < _w.Length; i++)
            {
                float gi = g[i] * scale;
                _m[i] = 0.9f * _m[i] + 0.1f * gi;
                _v[i] = 0.999f * _v[i] + 0.001f * gi * gi;
                _w[i] -= 1e-3f * (_m[i] / c1) / (MathF.Sqrt(_v[i] / c2) + 1e-8f);
            }
        }
    }
}
