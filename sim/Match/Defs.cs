namespace FusionKingdoms.Sim;

// Costs come out of storage when a site is placed; builders then put in the Work (build points) at the site.
public sealed record BuildingDef(BuildingType Type, string Name, int Age, float Timber, float Stone, float Lumen, float Work, float Radius,
    string Description, string Group)
{
    public string CostText => string.Join(", ", new[] { (Timber, "timber"), (Stone, "stone"), (Lumen, "Lumen") }
        .Where(c => c.Item1 > 0).Select(c => $"{c.Item1:0} {c.Item2}"));
}

public static class Defs
{
    public const float MatchLength = 1200f;
    public const int PopulationMax = 80;
    public const int ManorPopulation = 12;
    public const int KeepPopulation = 6;
    public const int HousePopulation = 5;
    public const float UpkeepInterval = 30f;
    public const float ProductionTime = 8f;
    public const float CaptiveEscapeTime = 60f;
    public const float ConvertTime = 45f;
    public const float SpireGoal = 1500f;
    public const float SpireRate = 2.5f;      // Lumen per second the Spire can absorb from its reservoir
    public const float RushPremium = 1.5f;
    public const float RoyalCommandTime = 8f;
    public const float RoyalCommandCost = 10f;
    public const float SealedOneWakes = 600f;

    // Costs are paid from storage when the site is placed; Work is build points (seconds for one average builder).
    public static readonly BuildingDef[] Buildings =
    [
        // Economy
        new(BuildingType.Farm, "Farm", 1, 15, 0, 0, 10, 1.5f, "A ploughed field with three crops. Farmers harvest it forever.", "Economy"),
        new(BuildingType.House, "House", 1, 20, 0, 0, 15, 1.0f, "+5 population cap.", "Economy"),
        new(BuildingType.Camp, "Work Camp", 1, 10, 0, 0, 8, 1.0f, "Drop-off for everything. Put one next to far-off trees, rocks or wells.", "Economy"),
        new(BuildingType.Granary, "Granary", 1, 25, 5, 0, 20, 1.5f, "Grain drop-off. Upkeep costs 20% less grain.", "Economy"),
        new(BuildingType.LumberMill, "Lumber Mill", 1, 25, 0, 0, 20, 1.5f, "Timber and stone drop-off; every load counts 20% more.", "Economy"),
        new(BuildingType.Manor, "Manor", 2, 30, 30, 0, 30, 1.5f, "+12 population cap.", "Economy"),
        new(BuildingType.LumenExtractor, "Lumen Extractor", 2, 0, 20, 20, 25, 1.0f, "Build beside a Lumen Well: draws Lumen on its own, forever.", "Economy"),
        new(BuildingType.Market, "Market", 2, 30, 10, 0, 20, 1.5f, "Trade grain, timber, stone and Lumen for gold, or gold for materials.", "Economy"),
        // Minds
        new(BuildingType.Cradle, "Cradle", 1, 30, 10, 0, 25, 1.5f, "Produces new subjects from your Library.", "Minds"),
        new(BuildingType.FusionAltar, "Fusion Altar", 1, 20, 10, 5, 20, 1.5f, "Sacrifice two subjects to blend their minds into a new class.", "Minds"),
        new(BuildingType.TrainingGrounds, "Training Grounds", 1, 25, 0, 0, 20, 1.5f, "Subjects learn live in drills, including Mirror Drills against the real enemy.", "Minds"),
        new(BuildingType.MutationPit, "Mutation Pit", 2, 0, 20, 15, 25, 1.5f, "Gamble on six mutated versions of a mind.", "Minds"),
        new(BuildingType.Dungeon, "Dungeon of Minds", 2, 0, 30, 10, 30, 1.5f, "Holds captives so they can't escape. Interrogate, Extract, Convert, Ransom.", "Minds"),
        // Defence
        new(BuildingType.Watchtower, "Watchtower", 1, 15, 10, 0, 15, 1.0f, "Sees far across the map.", "Defence"),
        new(BuildingType.GuardPost, "Guard Post", 1, 15, 5, 0, 12, 1.0f, "Your subjects within 7 tiles tussle 30% harder. Guard orders here hold the line.", "Defence"),
        new(BuildingType.Wall, "Wall", 1, 0, 2, 0, 2, 0.5f, "A stone wall segment. Click and drag to build a line. Enemies must break it to pass.", "Defence"),
        new(BuildingType.Gate, "Gate", 1, 4, 4, 0, 5, 0.5f, "Your subjects walk through; enemies have to break it down.", "Defence"),
        new(BuildingType.Shrine, "Shrine", 1, 0, 15, 10, 15, 1.0f, "Within 7 tiles: Will recovers three times faster, the subdued rise twice as fast.", "Defence"),
        new(BuildingType.Infirmary, "Infirmary", 2, 20, 15, 5, 20, 1.5f, "Subdued subjects within 8 tiles get back on their feet three times faster.", "Defence"),
        // Victory
        new(BuildingType.LumenSpire, "Lumen Spire", 3, 40, 80, 50, 60, 2.0f, "Feed it Lumen: at 1,500 charge you win by Ascension. It charges slowly, and sabotage drains it.", "Victory"),
    ];

    public static BuildingDef Of(BuildingType t) => Buildings.First(b => b.Type == t);

    public static string NameOf(BuildingType t) => t switch
    {
        BuildingType.Keep => "Keep",
        BuildingType.BuildSite => "Build site",
        BuildingType.Den => "Den",
        BuildingType.AncientAltar => "Ancient Altar",
        BuildingType.Merchant => "Wandering Merchant",
        _ => Of(t).Name,
    };

    public static float SightOf(BuildingType t) => t switch
    {
        BuildingType.Watchtower => 14f,
        BuildingType.GuardPost => 9f,
        BuildingType.Keep => 9f,
        BuildingType.BuildSite => 3f,
        _ => 5f,
    };

    // Age upgrade costs: (stone, timber, lumen).
    public static (float stone, float timber, float lumen, float gold) AgeCost(int toAge) => toAge switch
    {
        2 => (40, 20, 40, 0),
        _ => (80, 60, 100, 30),
    };

    // Market rates per lot: (what you give, amount) -> (what you get, amount).
    public static readonly (ResourceType give, float giveAmt, ResourceType get, float getAmt)[] Trades =
    [
        (ResourceType.Food, 20, ResourceType.Gold, 2), (ResourceType.Timber, 10, ResourceType.Gold, 2),
        (ResourceType.Stone, 10, ResourceType.Gold, 3), (ResourceType.Lumen, 10, ResourceType.Gold, 4),
        (ResourceType.Gold, 3, ResourceType.Timber, 10), (ResourceType.Gold, 4, ResourceType.Stone, 10),
        // Keep barter: poor rates, but always available, so a kingdom can never be stuck without materials.
        (ResourceType.Food, 30, ResourceType.Timber, 5), (ResourceType.Food, 40, ResourceType.Stone, 5),
    ];
    public const int FirstBarter = 6;
    public const float MerchantPrice = 40f;

    // Staffing: subjects with these traits standing near the building make it work better.
    public static readonly Dictionary<BuildingType, (string[] traits, string effect)> Staffing = new()
    {
        [BuildingType.Watchtower] = (["Patient", "Ambusher", "Sentinel", "Kingslayer"], "sees 6 tiles farther"),
        [BuildingType.Dungeon] = (["Homebody", "Brave", "Sentinel", "Fearless", "Escort", "Hoardmaster"], "captives can't be broken out"),
        [BuildingType.Market] = (["Greedy", "Pickpocket", "Hoardmaster", "Gatherer"], "trades pay 50% more gold"),
        [BuildingType.Infirmary] = (["Caring", "Medic", "Saint", "Selfless"], "subdued subjects recover even faster"),
    };

    public static string AgeName(int age) => age switch { 1 => "Settlement", 2 => "Realm", _ => "Dominion" };

    public static float GrainCost(Mind m) => 15f;
    public static float LumenCost(Mind m) => m.Parents.Length == 0 && m.Drills.Length == 0 ? 0f : 5f * (m.Generation + 1);
    public static float FusionCost(Mind a, Mind b) => 10f + 5f * Math.Max(a.Generation, b.Generation);
    public static float ForeseeCost => 3f;
    public static float MutationCost(int level) => 5f * level;
    public static float DrillCost => 5f;
    public static float InfusionCost => 15f;
}
