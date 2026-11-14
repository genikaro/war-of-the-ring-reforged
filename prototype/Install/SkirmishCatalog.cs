namespace Reforged.Install;

/// <summary>Build-194: the skirmish scope the rule sections are built for, the lists tools/skirmish_map.py,
/// skirmish_construction.py, combat_rules.py, content_pack.py and prepare_prototype.py hold: which buildings and units a
/// skirmish offers, each faction's start, the worker palettes and the player colours. Owner decisions and captured
/// orders, not source data; keep them equal to the Python lists while the import still builds the content pack.</summary>
public static class SkirmishCatalog
{
    public const string Citadel = "BUILDING_G_TOWN_CENTER_LEVEL_1";
    public const int StartWorkers = 4;
    public const string EvilTownCenter = "BUILDING_E_WORKER_STRUCTURE";
    public const string EvilFortress = "BUILDING_E_TOWN_CENTER_LEVEL_1";
    public const string EvilWarpost = "BUILDING_E_TERRITORY_MARKER";

    private static readonly string[] GoodUnits = ["UNIT_G_HUMAN_WORKER", "UNIT_G_GONDOR_SWORDSMAN", "UNIT_G_DWARVEN_AXETHROWER", "UNIT_G_HERO_FRODO",
        "UNIT_G_ROHAN_RIDER", "UNIT_G_DWARVEN_SHIELDBREAKER", "UNIT_G_RANGER",
        "UNIT_G_ELVEN_LIGHTBRINGER", "UNIT_G_BEORNING", "UNIT_G_HUORN", "UNIT_G_BEAR",
        "UNIT_G_HERO_GIMLI", "UNIT_G_HERO_LEGOLAS", "UNIT_G_HERO_GANDALF"];
    private static readonly string[] EvilUnits = ["UNIT_E_GOBLIN_SLAVE", "UNIT_E_GOBLIN_SPEARMAN", "UNIT_E_GOBLIN_SLAVE_MASTER",
        "UNIT_E_ORC_SLASHER", "UNIT_E_ORC_BOWMAN", "UNIT_E_URUK_HAI", "UNIT_E_WARG_RIDER", "UNIT_E_GIANT_SPIDER",
        "UNIT_E_WRAITH", "UNIT_E_HARADRIM_ASSASSIN", "UNIT_E_TROLL_STONEHURLER", "UNIT_E_TROLL_BONECLEAVER",
        "UNIT_E_HERO_GOLLUM", "UNIT_E_HERO_NAZGUL_KING", "UNIT_E_HERO_QAMARA", "UNIT_E_HERO_GRISHNAKH", "UNIT_E_HERO_SARUMAN",
        "UNIT_E_BONUS_BLACK_RIDER"];
    /// <summary>skirmish_map.SKIRMISH_UNITS: both factions' trainable units and the Wraith's Black Rider.</summary>
    public static readonly string[] SkirmishUnits = [.. GoodUnits, .. EvilUnits];
    /// <summary>combat_rules.UNITS, the first combat demo units.</summary>
    public static readonly string[] CombatDemoUnits = ["UNIT_G_HERO_ARAGORN", "UNIT_G_ELF_KNIGHT", "UNIT_E_URUK_HAI", "UNIT_G_ELVEN_ARCHER"];
    /// <summary>combat_rules.RANGED_UNITS: units with a missile weapon; their exported models get centred pivots.</summary>
    public static readonly string[] RangedUnits = ["UNIT_G_ELVEN_ARCHER", "UNIT_G_DWARVEN_AXETHROWER", "UNIT_G_ELVEN_LIGHTBRINGER", "UNIT_G_HERO_LEGOLAS",
        "UNIT_G_GIANT_ENT", "UNIT_E_ORC_BOWMAN", "UNIT_E_URUK_HAI", "UNIT_E_TROLL_STONEHURLER", "UNIT_E_HERO_QAMARA"];
    /// <summary>skirmish_production.RALLY_BANNER: the rally point's flag.</summary>
    public const string RallyBanner = "MODEL_G_GATHER_POINT";
    /// <summary>prepare_prototype.SKIRMISH_ROSTER: every unit the content pack exports, the Fate giants included.</summary>
    public static readonly string[] Roster = [.. CombatDemoUnits.Concat(SkirmishUnits).Concat(["UNIT_G_GIANT_ENT", "UNIT_E_HERO_BALROG"]).Distinct()];

    public static readonly string[] GoodBuildings = [
        "BUILDING_G_TOWN_CENTER_LEVEL_1", "BUILDING_G_FOOD_STRUCTURE", "BUILDING_G_BLACKSMITH",
        "BUILDING_G_WORKER_STRUCTURE", "BUILDING_G_GONDORIAN_BARRACKS", "BUILDING_G_DWARF_BARRACKS", "BUILDING_G_TOWER",
        "BUILDING_G_TOWN_CENTER_LEVEL_2", "BUILDING_G_POPULATION_STRUCTURE_UPGRADE", "BUILDING_G_ARMORY",
        "BUILDING_G_RANGER_BARRACKS", "BUILDING_G_SKILLS_STRUCTURE",
        "BUILDING_G_TOWN_CENTER_LEVEL_3", "BUILDING_G_STABLES", "BUILDING_G_ELF_BARRACKS", "BUILDING_G_HOSPITAL",
        "BUILDING_G_TOWN_CENTER_LEVEL_4", "BUILDING_G_TOWER_UPGRADE",
        "BUILDING_G_TOWN_CENTER_LEVEL_5"];
    public static readonly string[] EvilBuildings = [
        EvilTownCenter, EvilWarpost, "BUILDING_E_FOOD_STRUCTURE", "BUILDING_E_BLACKSMITH", EvilFortress,
        "BUILDING_E_ORC_BARRACKS", "BUILDING_E_STABLES", "BUILDING_E_TOWER", "BUILDING_E_ARMORY",
        "BUILDING_E_TOWN_CENTER_LEVEL_2", "BUILDING_E_HARADRIM_BARRACKS", "BUILDING_E_BEAST_UPGRADES_STRUCTURE",
        "BUILDING_E_TOWN_CENTER_LEVEL_3", "BUILDING_E_TROLL_BARRACKS", "BUILDING_E_HOSPITAL",
        "BUILDING_E_TOWN_CENTER_LEVEL_4",
        "BUILDING_E_TOWN_CENTER_LEVEL_5"];
    /// <summary>skirmish_map.SKIRMISH_BUILDINGS, in its order (Good tiers 1-5, then Evil tiers 1-5).</summary>
    public static readonly string[] SkirmishBuildings = [.. GoodBuildings, .. EvilBuildings];
    private static readonly HashSet<string> SkirmishBuildingSet = [.. SkirmishBuildings];
    public static bool IsSkirmishBuilding(string? name) => name != null && SkirmishBuildingSet.Contains(name);

    public sealed record Faction(string Key,string Name,string[] Prefixes,string TownCenter,string TechRoot,string Worker,string Food,string Ore,
        (string Unit,int Count)[] StartUnits);
    /// <summary>skirmish_map.FACTIONS with content_pack.START_UNITS: the Evil side starts with one Slavemaster too.</summary>
    public static readonly Faction[] Factions = [
        new("good","Good",["BUILDING_G_","UNIT_G_"],Citadel,Citadel,"UNIT_G_HUMAN_WORKER","BUILDING_G_FOOD_STRUCTURE","BUILDING_G_BLACKSMITH",[]),
        new("evil","Evil",["BUILDING_E_","UNIT_E_"],EvilTownCenter,EvilFortress,"UNIT_E_GOBLIN_SLAVE","BUILDING_E_FOOD_STRUCTURE","BUILDING_E_BLACKSMITH",
            [("UNIT_E_GOBLIN_SLAVE_MASTER",1)]),
    ];

    /// <summary>The faction a unit or building name belongs to ("good" for a name of neither side).</summary>
    public static string FactionOf(string name)
        => Factions.FirstOrDefault(f => f.Prefixes.Any(p => name.StartsWith(p,StringComparison.Ordinal)))?.Key ?? "good";

    /// <summary>skirmish_construction.CONSTRUCTION_ORDER: the Good worker's palette as the original shows it.</summary>
    public static readonly string[] ConstructionOrder = [Citadel, "BUILDING_G_FOOD_STRUCTURE", "BUILDING_G_BLACKSMITH", "BUILDING_G_TOWER",
        "BUILDING_G_WORKER_STRUCTURE", "BUILDING_G_GONDORIAN_BARRACKS", "BUILDING_G_DWARF_BARRACKS",
        "BUILDING_G_SKILLS_STRUCTURE", "BUILDING_G_RANGER_BARRACKS", "BUILDING_G_ARMORY",
        "BUILDING_G_STABLES", "BUILDING_G_ELF_BARRACKS", "BUILDING_G_HOSPITAL"];
    /// <summary>skirmish_construction.EVIL_CONSTRUCTION_ORDER.</summary>
    public static readonly string[] EvilConstructionOrder = [EvilTownCenter, "BUILDING_E_FOOD_STRUCTURE", "BUILDING_E_BLACKSMITH",
        EvilFortress, "BUILDING_E_ORC_BARRACKS", "BUILDING_E_STABLES", "BUILDING_E_TOWER",
        "BUILDING_E_ARMORY", "BUILDING_E_HARADRIM_BARRACKS", "BUILDING_E_BEAST_UPGRADES_STRUCTURE",
        "BUILDING_E_TROLL_BARRACKS", "BUILDING_E_HOSPITAL"];

    /// <summary>prepare_prototype.SLOT_COLORS: the TeamColors records of player slots 1-8.</summary>
    public static readonly string[] SlotColors = ["TEAM_COLOR_BLUE", "TEAM_COLOR_RED", "TEAM_COLOR_GREEN", "TEAM_COLOR_PURPLE", "TEAM_COLOR_YELLOW",
        "TEAM_COLOR_CYAN", "TEAM_COLOR_ORANGE", "TEAM_COLOR_PINK"];
}
