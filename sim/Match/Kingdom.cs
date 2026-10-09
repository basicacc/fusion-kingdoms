using System.Numerics;

namespace FusionKingdoms.Sim;

public sealed class Captive
{
    public Unit Unit = null!;
    public Mind? Mind;
    public int FromTeam;
    public float Held;
    public float Convert = -1f;
    public bool Interrogated;
}

public sealed class ProductionOrder
{
    public Mind Mind = null!;
    public float Remaining = Defs.ProductionTime;
}

public sealed class TrainingJob
{
    public Unit Unit = null!;
    public DrillTrainer? Trainer;
    public string Drill = "";
    public Task<float>? Running;
    public bool Stop;
    public int MirrorIteration;
    public readonly List<float> MirrorCurve = new();

    public int Iteration => Trainer?.Iteration ?? MirrorIteration;
    public List<float> Curve => Trainer?.Curve ?? MirrorCurve;
}

// What a freshly made subject does first.
public enum NewbornJob { FreeWill, Food, Timber, Stone, Lumen, Guard }

public sealed class Kingdom
{
    public Vector2? Rally;                      // where new subjects walk to (Cradle rally point)
    public NewbornJob Job = NewbornJob.FreeWill;
    public Mind? Repeat;
    public readonly HashSet<string> Techs = new();
    public Research? Researching;
    public bool Has(string tech) => Techs.Contains(tech);                        // keep producing this mind whenever the queue runs dry
    public int Team;
    public string Name = "";
    public bool IsAi;
    public Building Keep = null!;
    public Unit Ruler = null!;
    public int Age = 1;
    public readonly List<Mind> Library = new();
    public readonly List<Captive> Captives = new();
    public readonly List<Essence> Essences = new();
    public readonly List<ProductionOrder> Queue = new();
    public readonly List<TrainingJob> Training = new();
    public readonly HashSet<string> Discovered = new();
    public readonly Dictionary<int, TrialResult> Intel = new();
    public float MutationHeat;
    public float Bonus = 1f;
    public int PopBonus;         // difficulty handicap on the population cap (AI only)
    public int TheftsSuffered;   // steps of stealing from our stores (for the AI to notice raids)   // difficulty handicap on work and tussles (AI only)
    public readonly MirrorRecorder Mirror = new();
    public int MirrorRaiders, MirrorGuards;   // tape counts (what a LAN client sees)
    public float UpkeepTimer = Defs.UpkeepInterval;
    public float LumenCollected;
    public int MindsCaptured;   // rival subjects taken (worth 25 points each)
    public int WildCaptured;    // wild creatures taken (worth 5)
    public int Lost;

    public float[] Stored => Keep.Stored;
    public float Grain => Stored[(int)ResourceType.Food];
    public float Timber => Stored[(int)ResourceType.Timber];
    public float Stone => Stored[(int)ResourceType.Stone];
    public float Lumen => Stored[(int)ResourceType.Lumen];
    public float Gold => Stored[(int)ResourceType.Gold];

    public bool CanAfford(float grain = 0, float timber = 0, float stone = 0, float lumen = 0, float gold = 0) =>
        Grain >= grain && Timber >= timber && Stone >= stone && Lumen >= lumen && Gold >= gold;

    public bool Spend(float grain = 0, float timber = 0, float stone = 0, float lumen = 0, float gold = 0)
    {
        if (!CanAfford(grain, timber, stone, lumen, gold)) return false;
        Stored[(int)ResourceType.Gold] -= gold;
        Stored[(int)ResourceType.Food] -= grain;
        Stored[(int)ResourceType.Timber] -= timber;
        Stored[(int)ResourceType.Stone] -= stone;
        Stored[(int)ResourceType.Lumen] -= lumen;
        return true;
    }

    public Vector2 Home => Keep.Pos;
}
