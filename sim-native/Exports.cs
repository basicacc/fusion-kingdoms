using System.Runtime.InteropServices;
using FusionKingdoms.Sim;

namespace FusionKingdoms.Native;

public static unsafe class Exports
{
    static readonly Dictionary<int, VecEnv> Envs = new();
    static int _next = 1;

    [UnmanagedCallersOnly(EntryPoint = "fk_obs_size")]
    public static int ObsSize() => BrainSpec.In.Count;

    [UnmanagedCallersOnly(EntryPoint = "fk_num_moves")]
    public static int NumMoves() => BrainSpec.NumMoves;

    [UnmanagedCallersOnly(EntryPoint = "fk_num_actions")]
    public static int NumActions() => BrainSpec.NumActions;

    [UnmanagedCallersOnly(EntryPoint = "fk_hidden")]
    public static int Hidden(int layer) => layer == 1 ? BrainSpec.Hidden1 : BrainSpec.Hidden2;

    [UnmanagedCallersOnly(EntryPoint = "fk_weight_count")]
    public static int WeightCount() => BrainSpec.WeightCount;

    [UnmanagedCallersOnly(EntryPoint = "fk_create")]
    public static int Create(int n, int kind, int seed)
    {
        var env = new VecEnv(n, (MindKind)kind, seed);
        lock (Envs) { Envs[_next] = env; return _next++; }
    }

    [UnmanagedCallersOnly(EntryPoint = "fk_destroy")]
    public static void Destroy(int handle) { lock (Envs) Envs.Remove(handle); }

    [UnmanagedCallersOnly(EntryPoint = "fk_reset")]
    public static void Reset(int handle, float* obs)
    {
        var env = Envs[handle];
        env.Reset(new Span<float>(obs, env.N * BrainSpec.In.Count));
    }

    [UnmanagedCallersOnly(EntryPoint = "fk_step")]
    public static void Step(int handle, int* moves, int* actions, float* obs, float* rew, byte* done) =>
        Envs[handle].Step(moves, actions, obs, rew, done);

    [UnmanagedCallersOnly(EntryPoint = "fk_set_context")]
    public static void SetContext(int handle, float chance) => Envs[handle].SetContextChance(chance);

    [UnmanagedCallersOnly(EntryPoint = "fk_ctx")]
    public static void Ctx(int handle, byte* flags) => Envs[handle].ContextFlags(flags);

    [UnmanagedCallersOnly(EntryPoint = "fk_stats")]
    public static double Stats(int handle, int* count)
    {
        var (mean, c) = Envs[handle].TakeStats();
        *count = c;
        return mean;
    }

    [UnmanagedCallersOnly(EntryPoint = "fk_forward")]
    public static void Forward(float* weights, float* obs, float* logits)
    {
        var brain = new Brain(new ReadOnlySpan<float>(weights, BrainSpec.WeightCount).ToArray());
        brain.Forward(new ReadOnlySpan<float>(obs, BrainSpec.In.Count), new Span<float>(logits, BrainSpec.NumOutputs));
    }

    static TraitTable? _traits;

    [UnmanagedCallersOnly(EntryPoint = "fk_load_traits")]
    public static int LoadTraits(byte* path)
    {
        _traits = TraitTable.Parse(File.ReadAllText(Marshal.PtrToStringUTF8((nint)path)!));
        return _traits.Traits.Count;
    }

    // Runs the Trial; writes 12 axis scores and a comma-separated trait list. Returns 1 if Erratic.
    [UnmanagedCallersOnly(EntryPoint = "fk_trial")]
    public static int RunTrial(float* weights, int seed, float* scores, byte* traitBuf, int bufLen)
    {
        var brain = new Brain(new ReadOnlySpan<float>(weights, BrainSpec.WeightCount).ToArray());
        var res = Trial.Run(brain, seed);
        res.Scores.CopyTo(new Span<float>(scores, Trial.AxisCount));
        var names = _traits == null ? "" : string.Join(",", _traits.Match(res).Select(t => t.Name));
        var bytes = System.Text.Encoding.UTF8.GetBytes(names);
        int n = Math.Min(bytes.Length, bufLen - 1);
        bytes.AsSpan(0, n).CopyTo(new Span<byte>(traitBuf, n));
        traitBuf[n] = 0;
        return res.Erratic ? 1 : 0;
    }

    static readonly Dictionary<int, DrillTrainer> Trainers = new();

    [UnmanagedCallersOnly(EntryPoint = "fk_es_new")]
    public static int EsNew(float* weights, int drill, int seed)
    {
        var t = new DrillTrainer(new Brain(new ReadOnlySpan<float>(weights, BrainSpec.WeightCount).ToArray()), (MindKind)drill, seed);
        lock (Envs) { Trainers[_next] = t; return _next++; }
    }

    [UnmanagedCallersOnly(EntryPoint = "fk_es_config")]
    public static void EsConfig(int handle, float sigma, float lr, int pop)
    {
        // The drill trainer is gradient-based now: only the learning rate carries over (sigma and pop are ignored).
        Trainers[handle].LearningRate = lr;
    }

    [UnmanagedCallersOnly(EntryPoint = "fk_es_step")]
    public static float EsStep(int handle) => Trainers[handle].Step();

    [UnmanagedCallersOnly(EntryPoint = "fk_es_weights")]
    public static void EsWeights(int handle, float* outW) =>
        Trainers[handle].Brain.Weights.CopyTo(new Span<float>(outW, BrainSpec.WeightCount));

    [UnmanagedCallersOnly(EntryPoint = "fk_es_free")]
    public static void EsFree(int handle) { lock (Envs) Trainers.Remove(handle); }

    [UnmanagedCallersOnly(EntryPoint = "fk_eval")]
    public static double Eval(float* weights, int kind, int episodes, int seed, int greedy, int context, int specialOnly)
    {
        var brain = new Brain(new ReadOnlySpan<float>(weights, BrainSpec.WeightCount).ToArray());
        return VecEnv.Evaluate(brain, (MindKind)kind, episodes, seed, greedy != 0, context, specialOnly != 0);
    }
}
