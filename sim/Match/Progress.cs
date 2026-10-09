namespace FusionKingdoms.Sim;

// Research at the Keep (one topic at a time, permanent bonuses) and in-place building upgrades.
public sealed record TechDef(string Id, string Name, int Age, float Timber, float Stone, float Lumen, float Gold, float Time, string Description);
public sealed record UpgradeDef(BuildingType From, BuildingType To, int Level, int Age, float Timber, float Stone, float Lumen, float Time, string Name, string Description);

public sealed class Research
{
    public string Id = "";
    public float Remaining;
}

public static class Progress
{
    public static readonly TechDef[] Techs =
    [
        new("ploughs", "Heavy Ploughs", 1, 40, 10, 0, 0, 45, "+20% grain from every harvest."),
        new("axes", "Sharp Axes", 1, 30, 20, 0, 0, 45, "+20% timber and stone from every load."),
        new("boots", "Swift Boots", 1, 30, 0, 10, 0, 40, "Every subject moves 8% faster."),
        new("cellars", "Deep Cellars", 2, 20, 40, 0, 0, 60, "Grain upkeep -15%."),
        new("lenses", "Lumen Lenses", 2, 0, 30, 30, 0, 60, "+25% Lumen gathered."),
        new("drill", "Drill Sergeants", 2, 30, 30, 10, 0, 60, "+12% tussle strength; Guard Posts give +50% instead of +30%."),
        new("masonry", "Masonry", 2, 20, 50, 0, 0, 60, "Building sites need 25% less work."),
        new("crowns", "Royal Treasury", 3, 0, 40, 20, 20, 75, "Markets pay 25% more gold; the Merchant sells for less."),
    ];

    public static readonly UpgradeDef[] Upgrades =
    [
        new(BuildingType.House, BuildingType.Manor, 1, 2, 15, 30, 0, 25, "Raise to a Manor", "+12 population instead of +5."),
        new(BuildingType.Farm, BuildingType.Farm, 2, 2, 20, 10, 0, 20, "Irrigate", "Its crops yield 50% more."),
        new(BuildingType.Watchtower, BuildingType.Watchtower, 2, 2, 10, 20, 0, 20, "Raise the tower", "Sees 6 tiles farther."),
        new(BuildingType.GuardPost, BuildingType.GuardPost, 2, 2, 15, 25, 5, 25, "Fortify", "Its tussle bonus rises to +50%."),
        new(BuildingType.Granary, BuildingType.Granary, 2, 2, 25, 15, 0, 25, "Expand", "Upkeep -35% instead of -20%."),
    ];

    public static TechDef? Tech(string id) => Techs.FirstOrDefault(t => t.Id == id);

    public static UpgradeDef? UpgradeFor(Building b) =>
        Upgrades.FirstOrDefault(u => u.From == b.Type && (u.From != u.To || b.Level < u.Level));
}
