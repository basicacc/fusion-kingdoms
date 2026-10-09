using System.Numerics;

namespace FusionKingdoms.Sim;

public enum Terrain : byte { Grass, Road, Farmland, Forest, Hills, Marsh, River, Rock }
public enum ResourceType : byte { None, Food, Timber, Stone, Lumen, Gold }
public enum BuildingType : byte
{
    Keep, BuildSite, Dungeon, Den, House, Cradle, FusionAltar, TrainingGrounds, Watchtower, MutationPit, LumenSpire, Camp, Infirmary, Market, AncientAltar, Merchant,
    Farm, Granary, LumberMill, GuardPost, Shrine, Manor, LumenExtractor, Wall, Gate,
}

// What a subject has been told to do. Executed exactly by OrderController; the brain only steps in when idle
// (with Free Will on) and decides fight-or-flee when a working subject is jumped.
public enum OrderKind : byte { None, Move, Gather, Build, Capture, Raid, Sabotage, Guard, Hold, Heal }
public enum Species : byte { Human, Sprite, Troll, FoxKin, MothKin, Sage }

public readonly record struct Body(float Speed, float Strength, float Will, float Carry, float Sight)
{
    public static Body Of(Species s) => s switch
    {
        Species.Human => new(3.0f, 20f, 100f, 5f, 7f),
        Species.Sprite => new(4.5f, 8f, 50f, 3f, 9f),
        Species.Troll => new(1.8f, 35f, 200f, 8f, 5f),
        Species.FoxKin => new(4.0f, 12f, 70f, 4f, 8f),
        Species.MothKin => new(2.5f, 6f, 40f, 2f, 6f),
        Species.Sage => new(2.2f, 5f, 80f, 2f, 8f),
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}

public static class TerrainInfo
{
    public static float SpeedMul(Terrain t) => t switch
    {
        Terrain.Road => 1.4f,
        Terrain.Forest => 0.6f,
        Terrain.Hills => 0.7f,
        Terrain.Marsh => 0.4f,
        Terrain.River or Terrain.Rock => 0f,
        _ => 1f,
    };

    public static bool Blocked(Terrain t) => t is Terrain.River or Terrain.Rock;
}

public enum CommandType : byte { None, WorkZone, Rally, Raid }

public sealed class Unit
{
    public int Id;
    public int Team;
    public Species Species;
    public Body Body;
    public Vector2 Pos;
    public float Will;
    public bool Active = true;
    public bool IsRuler;

    public ResourceType CarryType;
    public float CarryAmount;
    public bool CarryStolen;
    public int CarryingUnit = -1;
    public int CarriedBy = -1;
    public float SubduedTimer;
    public int TussleTarget = -1;

    public CommandType Command;
    public Vector2 CommandTarget;
    public float Stance;

    public IController? Controller;
    public Mind? Mind;

    // Orders (see OrderController).
    public OrderKind Order;
    public Vector2 OrderPos;
    public int OrderTarget = -1;
    public ResourceType OrderRes;
    public bool FreeWill = true;
    public bool OrderFromWill;   // the current order was the subject's own idea (Free Will), not the player's

    // How good this subject is at each kind of work; set from its mind's measured skills (1 = average).
    public float FarmMul = 1f, MaterialMul = 1f, LumenMul = 1f, BuildMul = 1f, StrengthMul = 1f,
        SpeedMul = 1f, SightMul = 1f, StealMul = 1f, HealMul = 1f, CarryMul = 1f, HaulMul = 1f;
    public float AuraMul = 1f;
    public Mind? StatsFor;       // the mind the stats above were computed from   // temporary tussle bonus from nearby buildings (Guard Post)
    public float Carry => Body.Carry * CarryMul;
    public Building? Station;
    public int LastMove, LastAction;

    public bool Subdued => Will <= 0f;
    public bool CanAct => Active && !Subdued && CarriedBy < 0;
}

public sealed class Building
{
    public int Id;
    public BuildingType Type;
    public int Team;
    public Vector2 Pos;
    public float Radius = 1.5f;
    public readonly float[] Stored = new float[6];
    public float BuildProgress;
    public float SabotageProgress;
    public float DisabledTimer;
    public BuildingType SiteFor;
    public float Charge;
    public int Level = 1;
    public float UpgradeTimer;          // > 0 while an upgrade is under way
    public BuildingType UpgradeTo;

    public bool IsStore => Type is BuildingType.Keep or BuildingType.Den or BuildingType.Camp or BuildingType.Granary or BuildingType.LumberMill;
    public bool Working => DisabledTimer <= 0f && Type != BuildingType.BuildSite;
}

public sealed class ResourceNode
{
    public ResourceType Type;
    public Vector2 Pos;
    public float Amount = float.PositiveInfinity;
    public float Yield = 1f;   // irrigated farms: more per harvest
}
