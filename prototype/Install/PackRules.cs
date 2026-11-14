using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-194: the content pack's rule sections that come from the database alone, built straight from the
/// install at startup. Each is a port of the Python function that writes it into content.json, giving the same JSON
/// (tests/formats --python-rules compares them with tools/rules_fingerprint.py): factions and team_colors
/// (content_pack.faction_data, prepare_prototype.team_colors), skirmish.start_state (content_pack.build),
/// skirmish.production with fate (skirmish_production), skirmish.research (skirmish_research),
/// skirmish.places_of_power (places_of_power.export) and skirmish.ai (skirmish_ai.export). CacheManifest puts them in
/// place of the pack's. The sections that need models (economy clips, construction footprints and defenses, bodies,
/// combat rules) still come from the pack. Python's rounding is kept: round() halves to even on the double,
/// quantize() halves away from zero on the shortest decimal form of the value, // floors.</summary>
public static class PackRules
{
    public static readonly string[] Sections = ["factions", "team_colors", "skirmish.start_state", "skirmish.production", "skirmish.research",
        "skirmish.places_of_power", "skirmish.ai"];

    /// <summary>The sections by path. <paramref name="language"/> is the install's text language (the AI players' code
    /// page); <paramref name="root"/> is the data folder the AI players' file paths are given relative to.</summary>
    public static Dictionary<string,JsonNode> FromInstall(string install,string language,string root) => Build(LqrDatabase.OpenInstall(install),install,language,root);

    public static Dictionary<string,JsonNode> Build(LqrDatabase db,string install,string language,string root)
    {
        var units = db["Units"];
        int workers = I(Named(units,"UNIT_G_HUMAN_WORKER"),"PopulationCost")*SkirmishCatalog.StartWorkers;
        var variables = db["WorldVariables"].Records[0];
        var production = Production(db,SkirmishCatalog.Roster);
        var aiUnits = production["units"]!.AsObject().Select(p => p.Key).ToArray();
        return new()
        {
            ["factions"] = Factions(db),
            ["team_colors"] = TeamColors(db),
            ["skirmish.start_state"] = new JsonObject
            {
                ["food"] = V(variables,"DefaultInitialFood"), ["ore"] = V(variables,"DefaultInitialOre"), ["population"] = workers,
                ["population_cap"] = I(Named(db["Buildings"],SkirmishCatalog.Citadel),"AdditionalPopAdded")+I(variables,"DefaultInitialPopulationCapacity"),
            },
            ["skirmish.production"] = production,
            ["skirmish.research"] = Research(db),
            ["skirmish.places_of_power"] = PlacesOfPower(db),
            ["skirmish.ai"] = Ai(db,[.. SkirmishCatalog.ConstructionOrder, .. SkirmishCatalog.EvilConstructionOrder, SkirmishCatalog.EvilWarpost],aiUnits,install,language,root),
        };
    }

    // ---- Values, the way the Python reads and rounds them --------------------------------------------------------

    private static LqrRecord Named(LqrTable table,string name) => table.Get(name) ?? throw new KeyNotFoundException($"{table.Name} has no {name}");
    private static LqrRecord ById(LqrTable table,int id) => table.Get(id) ?? throw new KeyNotFoundException($"{table.Name} has no record {id}");
    private static int I(LqrRecord record,string column) => record[column] is int value ? value
        : throw new InvalidCastException($"{record.Table.Name}.{column} is not an integer column");
    private static double F(LqrRecord record,string column) => record[column] switch { float value => value, int value => value,
        _ => throw new InvalidCastException($"{record.Table.Name}.{column} is not a number") };
    private static string S(LqrRecord record,string column) => (string)record[column];
    private static bool B(LqrRecord record,string column) => I(record,column) != 0;
    /// <summary>A value as it is, an int, a float (as the double Python makes of it) or a string.</summary>
    private static JsonNode V(LqrRecord record,string column) => Node(record[column]);
    private static JsonNode Node(object value) => value switch { int i => i, float f => (double)f, string s => s, _ => throw new InvalidCastException() };
    /// <summary>Every column of a record (Python's record.values).</summary>
    private static JsonObject Values(LqrRecord record)
    {
        var node = new JsonObject();
        for (int c = 0; c < record.Values.Length; c++) node[record.Table.Columns[c].Name] = Node(record.Values[c]);
        return node;
    }

    /// <summary>Python round(): the nearest integer, halves to even, on the double itself.</summary>
    public static long Round(double value) => (long)Math.Round(value,MidpointRounding.ToEven);
    /// <summary>combat_rules.quantize: the value's shortest decimal form times the scale, halves away from zero.</summary>
    public static long Quantize(object value,long scale = 1000)
    {
        string text = value switch
        {
            int i => i.ToString(CultureInfo.InvariantCulture),
            float f when double.IsFinite(f) => ((double)f).ToString("R",CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException("non-finite rules value"),
        };
        return (long)Math.Round(decimal.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture)*scale,MidpointRounding.AwayFromZero);
    }
    private static long Ppm(double value) => Round(value*1_000_000);
    private static int FloorDiv(int a,int b) => (int)Math.Floor((double)a/b);
    /// <summary>A Windows path's file name without its extension (Path(...).stem, PureWindowsPath(...).stem).</summary>
    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\','/'));
    private static JsonArray Strings(IEnumerable<string> items) => new([.. items.Select(s => (JsonNode?)s)]);
    private static JsonArray Ints(IEnumerable<int> items) => new([.. items.Select(i => (JsonNode?)i)]);

    // ---- factions, team_colors ---------------------------------------------------------------------------------

    private static JsonObject Factions(LqrDatabase db)
    {
        var result = new JsonObject();
        foreach (var faction in SkirmishCatalog.Factions)
        {
            var start = new JsonArray(new JsonObject { ["unit"] = faction.Worker, ["count"] = SkirmishCatalog.StartWorkers });
            foreach (var (unit,count) in faction.StartUnits) start.Add(new JsonObject { ["unit"] = unit, ["count"] = count });
            result[faction.Key] = new JsonObject
            {
                ["name"] = faction.Name, ["town_center"] = faction.TownCenter, ["worker"] = faction.Worker, ["tech_root"] = faction.TechRoot,
                ["food_building"] = faction.Food, ["ore_building"] = faction.Ore, ["start_units"] = start,
                ["start_population"] = I(Named(db["Units"],faction.Worker),"PopulationCost")*SkirmishCatalog.StartWorkers,
            };
        }
        return result;
    }

    private static JsonArray TeamColors(LqrDatabase db)
        => new([.. SkirmishCatalog.SlotColors.Select(name => (JsonNode?)new JsonArray([.. new[] { "Red","Green","Blue" }
            .Select(channel => (JsonNode?)(F(Named(db["TeamColors"],name),channel)/255))]))]);

    // ---- production -------------------------------------------------------------------------------------------

    private const string ProductionAdapter = "UnitTrainingTime milliseconds round up to 20 Hz ticks. Cost, Fate included, is paid and population reserved when a unit is queued. Cancellation refunds the full cost; the queue holds five units; units appear on the nearest free cell to the building center. These three rules are not traced to source data. A unique unit (source IsUnique) cannot be queued while one is alive or queued; the source HeroDeathRefreshTime is not applied. Heroes of a unique group (Gimli, Aragorn, Legolas) are capped by the best standing building of the team that holds that group (source AmountBuildingAffectsUniqueGroup: Citadel 2-5 hold 1, 2, 3, 3); whether several Citadels add up is unverified. A team starts with no Fate and earns it in battle (see fate); the Sandbox map keeps it full.";
    private const string FateAdapter = "The Power Acquisition setting is Medium (no lobby choice yet); every Good unit weapon has FateModifier 1, so only building defenses carry their own (fate_ppm); fixed-point millionths replace the source floats, and the bots' difficulty bonus to Fate (x0.85 to x1.5 in Rings.exe) is not applied.";

    /// <summary>skirmish_production.production_data: each producer's source UnitProduced order with the units' costs,
    /// training time, population, Fate cost and hero group; units needing a building outside the skirmish or outside
    /// the exported roster are listed as unavailable with the reason.</summary>
    public static JsonObject Production(LqrDatabase db,IReadOnlyCollection<string> roster)
    {
        LqrTable units = db["Units"], buildings = db["Buildings"];
        var variables = db["WorldVariables"].Records[0];
        var producers = new JsonObject();
        var rules = new JsonObject();
        foreach (string name in SkirmishCatalog.SkirmishBuildings)
        {
            var record = Named(buildings,name);
            if (!B(record,"IsUnitProducer")) continue;
            var produced = new JsonArray();
            var unavailable = new JsonObject();
            foreach (int id in Enumerable.Range(1,6).Select(i => I(record,$"UnitProduced{i}")).Where(id => id >= 0))
            {
                var source = Named(units,ById(units,id).Name);
                string unit = source.Name;
                var required = Enumerable.Range(1,4).Select(i => I(source,$"BuildingRequired{i}")).Where(b => b >= 0).Select(b => ById(buildings,b).Name).ToList();
                var missing = required.Where(b => !SkirmishCatalog.IsSkirmishBuilding(b)).ToList();
                string? reason = missing.Count > 0 ? "requires "+string.Join(", ",missing) : roster.Contains(unit) ? null : "model and rules not exported";
                if (reason != null) { unavailable[unit] = reason; continue; }
                produced.Add(unit);
                rules[unit] = new JsonObject
                {
                    ["display_name"] = S(source,"Name"), ["food_cost"] = V(source,"FoodCost"), ["ore_cost"] = V(source,"OreCost"),
                    ["fate_cost"] = V(source,"PowerCost"), ["population"] = V(source,"PopulationCost"),
                    ["duration_ticks"] = FloorDiv(I(source,"UnitTrainingTime")+49,50), ["unique"] = B(source,"IsUnique"),
                    ["unique_group"] = V(source,"UniqueGroup"), ["prerequisites"] = Strings(required),
                };
            }
            producers[name] = new JsonObject { ["units"] = produced, ["unavailable"] = unavailable };
        }
        var capacity = new JsonObject();
        var provided = new JsonObject();
        foreach (string name in SkirmishCatalog.SkirmishBuildings)
        {
            var record = Named(buildings,name);
            provided[name] = V(record,"AdditionalPopAdded");
            if (I(record,"UniqueGroupBuildingAffects") >= 0)
                capacity[name] = new JsonObject { ["group"] = V(record,"UniqueGroupBuildingAffects"), ["amount"] = V(record,"AmountBuildingAffectsUniqueGroup") };
        }
        var byUnit = new JsonObject();
        foreach (string name in roster)
        {
            // skirmish_production.added_population: 0 for a table without the column.
            var record = Named(units,name);
            if (!units.HasColumn("AdditionalPopAdded") || I(record,"AdditionalPopAdded") == 0) continue;
            byUnit[name] = new JsonObject { ["added"] = V(record,"AdditionalPopAdded"), ["max"] = V(record,"MaxPopulation") };
        }
        return new JsonObject
        {
            ["format_version"] = 1, ["ticks_per_second"] = 20, ["producers"] = producers, ["units"] = rules,
            ["population_provided"] = provided, ["population_by_unit"] = byUnit, ["unique_group_capacity"] = capacity,
            ["max_fate"] = V(variables,"MaxPowerPoints"), ["fate"] = Fate(db),
            ["initial_population_capacity"] = V(variables,"DefaultInitialPopulationCapacity"),
            ["max_population"] = V(variables,"MaxPopulationCap"), ["rally_banner"] = "objects/MODEL_G_GATHER_POINT.glb",
            ["adapter"] = ProductionAdapter,
        };
    }

    /// <summary>skirmish_production.fate_data: how battle earns Fate, from WorldVariables (its docstring has the formula).</summary>
    private static JsonObject Fate(LqrDatabase db)
    {
        var v = db["WorldVariables"].Records[0];
        return new JsonObject
        {
            ["max_points"] = V(v,"MaxPowerPoints"), ["level_per_point"] = V(v,"PowerLevelPerPoint"),
            ["by_damage_ppm"] = Ppm(F(v,"PowerByDamageRate")), ["reduction_ppm"] = Ppm(F(v,"PowerReductionScalar")),
            ["max_distance_ppm"] = Ppm(F(v,"MaxPowerDistanceMultiplier")), ["warpost_ppm"] = Ppm(F(v,"WarpostPowerAcquisitionRate")),
            ["acquisition_ppm"] = new JsonObject
            {
                ["low"] = Ppm(F(v,"PowerAcquisitionRateLow")), ["medium"] = Ppm(F(v,"PowerAcquisitionRateMedium")), ["high"] = Ppm(F(v,"PowerAcquisitionRateHigh")),
            },
            ["town_centers"] = Strings(SkirmishCatalog.SkirmishBuildings.Where(name => B(Named(db["Buildings"],name),"IsTownCenter"))),
            ["adapter"] = FateAdapter,
        };
    }

    // ---- research -----------------------------------------------------------------------------------------------

    private static readonly Dictionary<string,string> TechniqueNames = new()
    {
        ["TECHNIQUE_FORGED_FITTINGS"] = "Iron Fittings", ["TECHNIQUE_IRON_FITTINGS"] = "Forged Fittings", ["TECHNIQUE_ASSASSINS_CLOAK"] = "Assassin's Cloak",
    };

    /// <summary>The descriptions the ability modules give researchable techniques (dwarf, ranger, elven, nature, then evil).</summary>
    private static readonly Dictionary<string,string> Descriptions = new()
    {
        ["TECHNIQUE_RAZORED_EDGE"] = "Thrown axes continue through one enemy behind the target within 5 m, dealing 70% damage.",
        ["TECHNIQUE_FLAMING_AXE"] = "Sets enemy buildings alight, dealing up to 7 fire damage per second for up to 6.25 seconds after the last hit.",
        ["TECHNIQUE_SHIELD_BREAK"] = "Each melee hit reduces enemy armor by 1 for 20 seconds. Stacks up to 5 times.",
        ["TECHNIQUE_CAMOUFLAGE"] = "Hides the Ranger from enemies until he moves or attacks. Enemy detectors can reveal him.",
        ["TECHNIQUE_EAGLE_EYE"] = "Doubles sight and detection range from 20 m to 40 m.",
        ["TECHNIQUE_DETECTION"] = "Reveals invisible enemies within sight range. Cannot reveal the One Ring.",
        ["TECHNIQUE_EAGLE_FLETCHING"] = "Increases Elven Archer missile range by 20%.",
        ["TECHNIQUE_ELVEN_CLOAKS"] = "Makes Elven Archers invisible to enemies, including while moving and fighting. Enemy detectors can reveal them.",
        ["TECHNIQUE_NEGATE_MAGIC"] = "Dispels harmful magic from allies and beneficial magic from enemies in a 7 m radius.",
        ["TECHNIQUE_LIGHT_OF_LOTHLORIEN"] = "Sears enemy units in a 5 m radius for 22 damage per second for 5 seconds.",
        ["TECHNIQUE_BEAR_FORM"] = "Transforms between human and bear form. Bear form has 420 base health, 17 attack and 2 armor, and cannot heal allies.",
        ["TECHNIQUE_SAVAGE_BLOW"] = "Deals 75 damage to enemies within a 4 m, 260-degree arc in front of the bear and knocks them back. Knockback-immune units still take damage.",
        ["TECHNIQUE_TREEFORM"] = "Roots the Huorn for 8 seconds, restoring 20 health per second. Enemies within 10 m when it takes root are dazed for 8 seconds.",
        ["TECHNIQUE_FLAME_ARROWS"] = "Orc Bowman arrows set enemy buildings alight.",
        ["TECHNIQUE_SAVAGE_INFLUENCE"] = "Whips allies within 10 m for 30 health. For 10 seconds they deal 25% more damage. Recharges in 36 seconds.",
        ["TECHNIQUE_POISON_DART"] = "Fires a dart within 10 m: 5 poison damage per second for 25 seconds. Costs 25 food and 25 ore per shot; 8 second recharge.",
        ["TECHNIQUE_ASSASSINS_CLOAK"] = "Haradrim Slayers are invisible to enemies except detectors (guide-based adapter).",
        ["TECHNIQUE_PARALYZE"] = "Spider melee hits stun enemies for 2 seconds.",
        ["TECHNIQUE_SPRAY_VENOM"] = "Stuns enemies in a 10 m, 180-degree cone for 4 seconds. Suppresses Paralyze for 30 seconds.",
        ["TECHNIQUE_SWEEPING_AXE"] = "Deals 100 damage to enemies within 5 m. Recharges in 12 seconds.",
        ["TECHNIQUE_DARKNESS"] = "Slows enemies within 5 m to half movement and attack speed for 10 seconds.",
        ["TECHNIQUE_PERPETUAL_DARKNESS"] = "Replaces Darkness: slows and blinds enemies within 7 m for 10 seconds.",
        ["TECHNIQUE_GREATER_PERCEPTION"] = "Doubles Wraith sight and detection range.",
        ["TECHNIQUE_MORGUL_BLADE"] = "Black Rider melee hits poison enemies: 3 damage per second for 5 seconds.",
        ["TECHNIQUE_POISON_ARROWS"] = "Evil Towers use poison arrows: 14 damage per second, plus 2 poison damage per second for 10 seconds.",
        ["TECHNIQUE_GOBLIN_FRENZY"] = "Goblin Spearmen and Slavemasters deal 50% more base damage and move and attack 30% faster (source unit list).",
    };

    /// <summary>evil_upgrades.SUPPORTED: the Evil techniques whose effects the simulation covers by name.</summary>
    private static readonly HashSet<string> EvilSupported = ["TECHNIQUE_ASSASSINS_CLOAK", "TECHNIQUE_DARKNESS", "TECHNIQUE_FLAME_ARROWS", "TECHNIQUE_GOBLIN_FRENZY",
        "TECHNIQUE_GREATER_PERCEPTION", "TECHNIQUE_MORGUL_BLADE", "TECHNIQUE_PARALYZE", "TECHNIQUE_PERPETUAL_DARKNESS", "TECHNIQUE_POISON_ARROWS",
        "TECHNIQUE_POISON_DART", "TECHNIQUE_SAVAGE_INFLUENCE", "TECHNIQUE_SPRAY_VENOM", "TECHNIQUE_SWEEPING_AXE"];
    /// <summary>BattleGear a technique may add and still count as simulated (the passive adapters handle them).</summary>
    private static readonly HashSet<int> SimulatedGear = [14, 26, 9, 23, 32, 28, 33];

    private const string ResearchAdapter = "BuildTime milliseconds round up to 20 Hz ticks. A building researches one technique at a time and cannot research while upgrading; cancelling refunds the full cost (no source refund field). Bonuses use the per-unit TechniqueUnitModifiers rows, added to weapon DPS and armor, which matches the integer Attack/Armor ratings of the unit panel; the technique rows also carry a DamageModifier multiplier (1.2 for Iron Blades) whose relation to them is unverified. A later tier of a chain replaces the earlier one (+1, +2, +3, not a sum), as the Elven Archer panel showed after each Outpost blade (good-035, -046, -056). A replaced missile weapon sets the missile DPS; its chain target and projectile are handled by the Dwarf passive adapter. Fire damage adds normalized building fire intensity; source extinguish rate decays it and MaxFireDamagePerSecond scales its damage (engine semantics unverified). Eagle Eye doubles Ranger sight and the radius used to detect normal invisibility; terrain fog is not simulated. Camouflage follows its spell graph: idle and hide until motion or attack, then start its three-second recharge. The building health multiplier raises the maximum and the current health of every building by the same share (unverified).";

    /// <summary>skirmish_research.display_name: TECHNIQUE_IRON_BLADES -> Iron Blades, as the English manual names them.</summary>
    public static string TechniqueName(string name)
    {
        if (TechniqueNames.TryGetValue(name,out string? known)) return known;
        string[] words = (name.StartsWith("TECHNIQUE_",StringComparison.Ordinal) ? name[10..] : name).ToLowerInvariant().Split('_');
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            words[i] = i > 0 && w is "of" or "the" ? w : w == "natures" ? "Nature's" : w.Length > 0 ? char.ToUpperInvariant(w[0])+w[1..] : w;
        }
        return string.Join(' ',words).Replace("Lothlorien","Lothlórien");
    }

    /// <summary>skirmish_research.unsupported: why a technique's effect is not simulated, or null when all of it is.</summary>
    private static string? Unsupported(LqrRecord r)
    {
        if (EvilSupported.Contains(r.Name)) return null;
        if (I(r,"AddBattleGear") >= 0 && !SimulatedGear.Contains(I(r,"AddBattleGear")) || I(r,"RemoveBattleGear") >= 0) return "grants an ability";
        if (F(r,"LineOfSightRadiusModifier") != 1 && r.Name != "TECHNIQUE_EAGLE_EYE") return "changes sight; there is no fog of war yet";
        if (I(r,"AffectedBuildingType1") >= 0 || I(r,"AffectedBuildingType2") >= 0 || B(r,"AffectsAllBuildings") && F(r,"HitPointsModifier") == 1)
            return "affects some buildings";
        if (F(r,"TimeScaleModifier") != 1 || B(r,"IsDetector") || I(r,"MeleeWeapon") >= 0 || F(r,"HitPointsModifier") != 1 && !B(r,"AffectsAllBuildings"))
            return "effect not simulated";
        return null;
    }

    /// <summary>skirmish_research.unit_bonuses: per affected unit, the TechniqueUnitModifiers row's additions where one
    /// exists, else the technique's DamageModifier on the unit's melee DPS and its ArmorModifier; plus range, sight, time
    /// scale and a replaced missile weapon.</summary>
    private static JsonObject UnitBonuses(LqrDatabase db,LqrRecord technique)
    {
        LqrTable units = db["Units"], weapons = db["Weapons"];
        var rows = new Dictionary<int,LqrRecord>();
        foreach (var row in db["TechniqueUnitModifiers"].Records)
            if (I(row,"TechniqueType") == technique.Id) rows[I(row,"UnitType")] = row;
        var bonuses = new JsonObject();
        for (int i = 1; i <= 5; i++)
        {
            int unitId = I(technique,$"AffectedUnitType{i}");
            if (unitId < 0) continue;
            var unit = ById(units,unitId);
            double damage, armor;
            if (rows.TryGetValue(unitId,out var row)) (damage,armor) = (F(row,"DamageAddifier"),F(row,"ArmorAddifier"));
            else (damage,armor) = (F(ById(weapons,I(unit,"MeleeWeapon")),"CuttingDamagePerSecond")*(F(technique,"DamageModifier")-1),F(technique,"ArmorModifier"));
            var bonus = new JsonObject
            {
                ["damage_milli"] = Round(damage*1000), ["armor_milli"] = Round(armor*1000),
                ["source"] = rows.ContainsKey(unitId) ? "TechniqueUnitModifiers" : "Techniques",
            };
            if (F(technique,"MissileWeaponRangeModifier") != 1) bonus["range_ppm"] = Ppm(F(technique,"MissileWeaponRangeModifier"));
            if (F(technique,"LineOfSightRadiusModifier") != 1) bonus["sight_ppm"] = Ppm(F(technique,"LineOfSightRadiusModifier"));
            if (F(technique,"TimeScaleModifier") != 1) bonus["time_scale_ppm"] = Ppm(F(technique,"TimeScaleModifier"));
            if (I(technique,"MissileWeapon") >= 0)
            {
                var weapon = ById(weapons,I(technique,"MissileWeapon"));
                bonus["missile_dps_milli"] = Round((F(weapon,"CuttingDamagePerSecond")+F(weapon,"FireDamagePerSecond"))*1000);
                bonus["missile_weapon"] = weapon.Name;
                if (B(weapon,"AlwaysKnockbackTarget") && F(weapon,"KnockbackStrength") > 0)
                {
                    bonus["missile_knockback_mm"] = weapon["KnockbackStrength"] is int strength ? strength*1000 : F(weapon,"KnockbackStrength")*1000;
                    bonus["missile_knockback_angle"] = Round(F(weapon,"KnockbackAngle"));
                }
                if (F(weapon,"AreaOfEffectRange") > 0)
                {
                    bonus["missile_splash_radius_mm"] = Round(F(weapon,"AreaOfEffectRange")*1000);
                    bonus["missile_splash_dps_milli"] = Round(F(weapon,"AreaOfEffectDamage")*1000);
                }
                int projectile = I(weapon,"AssociatedProjectile");
                if (projectile >= 0 && projectile != I(ById(weapons,I(unit,"MissileWeapon")),"AssociatedProjectile")) bonus["missile_projectile"] = projectile;
            }
            bonuses[unit.Name] = bonus;
        }
        return bonuses;
    }

    private static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.TryGetValue(out string? s) => s.Length > 0,
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v => double.Parse(v.ToJsonString(),CultureInfo.InvariantCulture) != 0,
        _ => true,
    };

    /// <summary>evil_upgrades.poison_data: a weapon's poison as fixed-point fields.</summary>
    private static IEnumerable<KeyValuePair<string,JsonNode?>> Poison(LqrDatabase db,LqrRecord weapon)
    {
        var poison = ById(db["Poisons"],I(weapon,"PoisonType"));
        yield return new("DpsMilli",Round(F(poison,"DamagePerSecond")*1000));
        yield return new("DurationTicks",(long)Math.Ceiling(F(poison,"Duration")/50));
        yield return new("ExperiencePpm",Ppm(F(poison,"ExperienceModifier")));
        yield return new("PoisonFatePpm",Ppm(F(poison,"FateModifier")));
    }

    /// <summary>skirmish_research.research_data: the techniques the skirmish buildings research, with what each costs
    /// and changes; the rest listed as unavailable with the reason.</summary>
    public static JsonObject Research(LqrDatabase db)
    {
        LqrTable buildings = db["Buildings"], techniques = db["Techniques"];
        var available = new JsonObject();
        var unavailable = new JsonObject();
        var unsimulated = new JsonObject();
        JsonObject With(JsonObject entry,string key,JsonNode? value,Func<string,bool>? keep = null)
        {
            var copy = new JsonObject();
            foreach (var (k,v) in entry) if (keep?.Invoke(k) ?? true) copy[k] = v?.DeepClone();
            copy[key] = value;
            return copy;
        }
        foreach (var technique in techniques.Records)
        {
            int researchable = I(technique,"ResearchableIn");
            if (researchable < 0 || !SkirmishCatalog.IsSkirmishBuilding(ById(buildings,researchable).Name)) continue;
            string building = ById(buildings,researchable).Name;
            string? required = I(technique,"BuildingRequired") >= 0 ? ById(buildings,I(technique,"BuildingRequired")).Name : null;
            int previous = I(technique,"ChainPreviousTechnique");
            var portrait = ById(db["UVAs"],I(technique,"PortraitBorder"));
            var entry = new JsonObject
            {
                ["display_name"] = TechniqueName(technique.Name), ["building"] = building,
                ["slot"] = V(technique,"SpecificIndexForPurchasing"), ["food_cost"] = V(technique,"CostFood"), ["ore_cost"] = V(technique,"CostOre"),
                ["duration_ticks"] = FloorDiv(I(technique,"BuildTime")+49,50), ["prerequisite"] = required,
                ["previous"] = previous >= 0 ? ById(techniques,previous).Name : null,
                ["portrait"] = Stem(S(portrait,"Filename")),
            };
            string? reason = required != null && !SkirmishCatalog.IsSkirmishBuilding(required) ? "requires "+required : Unsupported(technique);
            if (reason != null)
            {
                unavailable[technique.Name] = reason;
                unsimulated[technique.Name] = With(entry,"reason",reason);
                continue;
            }
            var bonuses = UnitBonuses(db,technique);
            if (!EvilSupported.Contains(technique.Name) && technique.Name != "TECHNIQUE_ELVEN_CLOAKS" && !B(technique,"AffectsAllBuildings")
                && I(technique,"AddBattleGear") < 0 && !bonuses.Any(b => b.Value!.AsObject().Any(p => p.Key != "source" && Truthy(p.Value))))
            {
                // Elven Cloaks: no source field carries its effect; the tooltip's cloaking lives in the game program.
                unavailable[technique.Name] = "effect not in the source tables";
                unsimulated[technique.Name] = With(entry,"reason","effect not in the source tables");
                continue;
            }
            var item = With(entry,"bonuses",bonuses);
            if (Descriptions.TryGetValue(technique.Name,out string? description)) item["description"] = description;
            if (B(technique,"AffectsAllBuildings")) item["building_health_ppm"] = Ppm(F(technique,"HitPointsModifier"));
            if (technique.Name == "TECHNIQUE_POISON_ARROWS")
            {
                var weapon = ById(db["Weapons"],I(technique,"MissileWeapon"));
                var poison = new JsonObject
                {
                    ["Id"] = -2000-technique.Id, ["Slot"] = 0, ["Name"] = "Poison Arrows", ["Icon"] = entry["portrait"]!.DeepClone(),
                    ["Passive"] = true, ["Kind"] = 50,
                };
                foreach (var (key,value) in Poison(db,weapon)) poison[key] = value;
                item["building_weapon"] = new JsonObject
                {
                    ["Building"] = "BUILDING_E_TOWER", ["DpsMilli"] = Round(F(weapon,"CuttingDamagePerSecond")*1000),
                    ["RangeMm"] = Round(F(weapon,"MaximumRange")*1000), ["MinimumRangeMm"] = Round(F(weapon,"MinimumRange")*1000),
                    ["FatePpm"] = Ppm(F(weapon,"FateModifier")), ["Poison"] = poison,
                };
            }
            available[technique.Name] = item;
        }
        // A chain step whose predecessor is not offered cannot be reached either (one pass, as the Python makes it).
        foreach (string name in available.Where(p => p.Value!["previous"] is JsonValue v && v.TryGetValue(out string? before) && !available.ContainsKey(before))
                     .Select(p => p.Key).ToList())
        {
            string before = available[name]!["previous"]!.GetValue<string>();
            unavailable[name] = "requires "+before;
            var item = available[name]!.AsObject();
            available.Remove(name);
            unsimulated[name] = With(item,"reason","requires "+before,key => key != "bonuses");
        }
        var fire = new JsonObject();
        foreach (var b in buildings.Records.Where(b => SkirmishCatalog.IsSkirmishBuilding(b.Name)))
            fire[b.Name] = new JsonObject
            {
                ["MaxDpsMilli"] = Round(F(b,"MaxFireDamagePerSecond")*1000), ["ArmorPpm"] = Ppm(F(b,"FireArmor")),
                ["ExtinguishPpmPerSecond"] = Ppm(F(b,"FireExtinguishRate")),
            };
        return new JsonObject
        {
            ["format_version"] = 1, ["ticks_per_second"] = 20, ["techniques"] = available, ["unavailable"] = unavailable,
            ["building_fire"] = fire, ["unsimulated"] = unsimulated, ["adapter"] = ResearchAdapter,
        };
    }

    // ---- places of power ----------------------------------------------------------------------------------------

    private static readonly string[] SkirmishExcludedObjects = ["OBJECT_PLACE_OF_POWER_STEED_OF_ROHAN", "OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY",
        "OBJECT_PLACE_OF_POWER_ENCHANTED_WILLOW"];
    private const string PlacesPolicy = "Source PlacesOfPower, PlaceOfPowerModifiers and the place-of-power Objects. A random place takes a complete object whose default rules allow random placement, chosen from the match seed. Models follow Objects.ModelEvent -> ModelEvents.AssociatedModelResource1; shared source models stay shared. Skirmishes replace Steed of Rohan, Aviary and Willow with allowed random landmarks. Owner balance override: Monument of Narsil grants heroes +25% damage; its original rule is preserved in source_rule. See docs/PLACES-OF-POWER.md for spell adapters and source-only campaign entries.";

    /// <summary>hero_abilities.linked: records chained by a next-record column, from <paramref name="first"/>.</summary>
    private static List<LqrRecord> Linked(LqrTable table,int first,string next)
    {
        var result = new List<LqrRecord>();
        var seen = new HashSet<int>();
        while (first >= 0)
        {
            if (!seen.Add(first)) throw new InvalidDataException("cycle in spell linkage");
            var record = table.Get(first) ?? throw new InvalidDataException("missing spell linkage");
            result.Add(record);
            first = I(record,next);
        }
        return result;
    }

    /// <summary>elven_abilities.graph: a spell's (event, action) pairs, child spells' pairs following the action that spawns them.</summary>
    private static List<(LqrRecord Event,LqrRecord Action)> SpellGraph(LqrDatabase db,LqrRecord spell,HashSet<int>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(spell.Id)) return [];
        var result = new List<(LqrRecord,LqrRecord)>();
        foreach (var e in Linked(db["SpellAndEvents"],I(spell,"FirstSpellAndEvent"),"NextSpellAndEvent"))
            foreach (var action in Linked(db["SpellAndEventAndActions"],I(e,"FirstSpellAndEventAndAction"),"NextSpellAndEventAndAction"))
            {
                result.Add((e,action));
                if (I(action,"SpellType") >= 0) result.AddRange(SpellGraph(db,ById(db["Spells"],I(action,"SpellType")),seen));
            }
        return result;
    }

    /// <summary>places_of_power.power_record: the landmark spell of an object (the Aviary and the Willow), or null.</summary>
    private static JsonNode? PowerRecord(LqrDatabase db,LqrRecord record)
    {
        if (db["BattleGear"].Get(I(record,"BattleGear1")) is not { } gear) return null;
        var spell = ById(db["Spells"],I(gear,"SpellType"));
        var actions = SpellGraph(db,spell);
        string Type(LqrRecord action) => ById(db["SpellActions"],I(action,"SpellActionType")).Name;
        List<LqrRecord> Typed(string suffix) => [.. actions.Where(p => Type(p.Action).EndsWith(suffix,StringComparison.Ordinal)).Select(p => p.Action)];
        var timer = Typed("_START_TIMER")[0];
        var slow = Typed("_APPLY_TIMESCALE_MULTIPLIER");
        var cursor = ById(db["Cursors"],I(spell,"TargetCursor"));
        var source = new JsonObject
        {
            ["Gear"] = Values(gear), ["Spell"] = Values(spell),
            ["Actions"] = new JsonArray([.. actions.Select(p => (JsonNode?)new JsonObject { ["Event"] = p.Event.Name, ["Type"] = Type(p.Action), ["Values"] = Values(p.Action) })]),
        };
        return new JsonObject
        {
            ["Kind"] = slow.Count > 0 ? "Willow" : "Aviary",
            ["Icon"] = Stem(S(ById(db["UVAs"],I(gear,"PortraitBorderless")),"Filename")),
            ["Cursor"] = Stem(S(cursor,"Filename")),
            ["RangeMm"] = slow.Count > 0 ? 30000 : 0,
            ["RadiusMm"] = Round(F(spell,"MaxAreaOfEffectRange")*1000),
            ["DurationTicks"] = Round(F(timer,"Float_01")*20), ["RechargeTicks"] = Round(F(spell,"RechargeTime")*20),
            ["TimeScalePpm"] = slow.Count > 0 ? Ppm(F(slow[0],"Float_01")) : 1_000_000,
            ["SoundEvents"] = Ints(Typed("_PLAY_SOUND_EVENT").Select(a => I(a,"SoundEventType")).Distinct().Order()),
            ["Source"] = source,
        };
    }

    private static JsonObject PlaceRecord(LqrRecord record,LqrTable modifiers)
    {
        const string prefix = "PLACE_OF_POWER_MODIFIER_";
        int type = I(record,"ModifierType");
        string modifier = type < 0 ? "NONE" : ById(modifiers,type).Name;
        if (modifier.StartsWith(prefix,StringComparison.Ordinal)) modifier = modifier[prefix.Length..];
        return new JsonObject
        {
            ["id"] = record.Id, ["name"] = record.Name, ["display_name"] = S(record,"Name"), ["radius_mm"] = Quantize(record["Radius"]),
            ["must_capture"] = B(record,"MustCapture"), ["must_be_in_radius"] = B(record,"MustBeInRadius"), ["random"] = B(record,"AllowRandomPlacement"),
            ["all_units"] = B(record,"AffectsAllUnitTypes"), ["heroes_only"] = B(record,"AffectsHeroesOnly"),
            ["unit_types"] = Ints(Enumerable.Range(1,6).Select(i => I(record,$"AffectedUnitType{i}")).Where(u => u >= 0)),
            ["modifier"] = modifier,
            ["float_ppm"] = Quantize(record["ModifierFloatParam1"],1_000_000), ["long"] = V(record,"ModifierLongParam1"),
        };
    }

    /// <summary>places_of_power.export: place-of-power objects by type and the source places of power, with the owner's
    /// Monument of Narsil rule (heroes deal 25% more damage; the source rule kept as source_rule).</summary>
    public static JsonObject PlacesOfPower(LqrDatabase db)
    {
        var modifiers = db["PlaceOfPowerModifiers"];
        var places = db["PlacesOfPower"].Records.Select(r => PlaceRecord(r,modifiers)).ToList();
        var narsil = places.First(p => p["name"]!.GetValue<string>() == "PLACE_OF_POWER_MONUMENT_OF_NARSIL");
        narsil["source_rule"] = narsil.DeepClone();
        narsil["modifier"] = "DAMAGE"; narsil["float_ppm"] = 1_250_000; narsil["long"] = 0; narsil["all_units"] = false; narsil["heroes_only"] = true;
        narsil["unit_types"] = new JsonArray();
        var objects = new JsonObject();
        foreach (var record in db["Objects"].Records.Where(r => r.Name.StartsWith("OBJECT_PLACE_OF_POWER_",StringComparison.Ordinal)))
        {
            var e = ById(db["ModelEvents"],I(record,"ModelEvent"));
            var model = ById(db["Models"],I(e,"AssociatedModelResource1"));
            objects[record.Name] = new JsonObject
            {
                ["display_name"] = S(record,"Name").Replace("Radaghast","Radagast"), ["model"] = model.Name,
                ["file"] = "objects/places/"+model.Name+".glb", ["power"] = PowerRecord(db,record),
                ["places"] = Ints(Enumerable.Range(1,3).Select(i => I(record,$"DefaultPlaceOfPower{i}")).Where(p => p >= 0)),
            };
        }
        return new JsonObject
        {
            ["format_version"] = 3, ["objects"] = objects, ["places"] = new JsonArray([.. places.Select(p => (JsonNode?)p)]),
            ["skirmish_excluded_objects"] = Strings(SkirmishExcludedObjects), ["policy"] = PlacesPolicy,
        };
    }

    // ---- AI -----------------------------------------------------------------------------------------------------

    private static readonly string[] Knobs = ["Difficulty", "Aggressiveness", "Tactics", "Scouting", "Harassment", "UsesSpecialAbilities", "UsesHeroes"];
    private static readonly string[] BuildingClasses = ["MILITARY", "DEFENSIVE", "ECONOMY", "UPGRADE", "SPECIAL"];
    private const string AiPolicy = "Source data: .rai knobs, Buildings AI columns, Units AIAssaultEffectiveness/AIRaidingEffectiveness/AINeverAddUnit and the AIWARPARTY_ASSAULT, AIWARPARTY_RAIDING and AIWARPARTY_PLACEOFPOWERSCOUT sizes and member shares. Decision logic is an adapter; see docs/SKIRMISH.md build-27, build-53 and build-54.";
    /// <summary>game_text.CODE_PAGES: the ANSI code page each release was written in; Western European otherwise.</summary>
    private static readonly Dictionary<string,int> CodePages = new()
    {
        ["ru"] = 1251, ["uk"] = 1251, ["pl"] = 1250, ["ko"] = 949, ["ja"] = 932, ["zh-Hans"] = 936, ["zh-Hant"] = 950,
    };

    /// <summary>The ANSI code page of an install in <paramref name="language"/> (game_text.code_page).</summary>
    public static Encoding CodePage(string language)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Once is enough; registering again changes nothing.
        return Encoding.GetEncoding(CodePages.GetValueOrDefault(language,1252));
    }

    /// <summary>skirmish_ai.parse_rai: key = value pairs; # lines are comments.</summary>
    public static Dictionary<string,string> ParseRai(byte[] data,Encoding encoding)
    {
        var values = new Dictionary<string,string>();
        foreach (string raw in encoding.GetString(data).ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            int equals = line.IndexOf('=');
            if (equals >= 0 && !line.StartsWith('#')) values[line[..equals].Trim()] = line[(equals+1)..].Trim();
        }
        return values;
    }

    private static JsonObject DefaultPersonality() => new()
    {
        ["id"] = "Default", ["display_name"] = "Default (medium)", ["file"] = null, ["difficulty"] = 1, ["aggressiveness"] = 50, ["tactics"] = 50,
        ["scouting"] = 50, ["harassment"] = 50, ["uses_special_abilities"] = true, ["uses_heroes"] = true,
    };

    /// <summary>skirmish_ai.personalities: the default and the install's AIPlayers\*.rai, names in the install's code page.</summary>
    public static JsonArray Personalities(string install,string language,string root)
    {
        var result = new JsonArray(DefaultPersonality());
        string folder = Path.Combine(install,"AIPlayers");
        if (!Directory.Exists(folder)) return result;
        var encoding = CodePage(language);
        foreach (string path in Directory.EnumerateFiles(folder).Where(f => Path.GetExtension(f).Equals(".rai",StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => Path.GetFileName(f).ToLowerInvariant(),StringComparer.Ordinal))
        {
            byte[] data = File.ReadAllBytes(path);
            var values = ParseRai(data,encoding);
            var missing = Knobs.Append("Name").Where(k => !values.ContainsKey(k)).ToList();
            if (missing.Count > 0) throw new InvalidDataException($"{Path.GetFileName(path)} lacks {string.Join(", ",missing)}");
            string full = Path.GetFullPath(path), relative = Path.GetRelativePath(Path.GetFullPath(root),full);
            string file = relative.StartsWith("..",StringComparison.Ordinal) || Path.IsPathRooted(relative) ? "AIPlayers/"+Path.GetFileName(path) : relative.Replace('\\','/');
            int Number(string key) => int.Parse(values[key],NumberStyles.Integer,CultureInfo.InvariantCulture);
            result.Add(new JsonObject
            {
                ["id"] = Path.GetFileNameWithoutExtension(path).Replace(" ",""), ["display_name"] = values["Name"], ["file"] = file,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(data)),
                ["difficulty"] = Number("Difficulty"), ["aggressiveness"] = Number("Aggressiveness"), ["tactics"] = Number("Tactics"),
                ["scouting"] = Number("Scouting"), ["harassment"] = Number("Harassment"),
                ["uses_special_abilities"] = values["UsesSpecialAbilities"] == "1", ["uses_heroes"] = values["UsesHeroes"] == "1",
            });
        }
        return result;
    }

    /// <summary>skirmish_ai.member_kind: whether a war-party member class wants melee or missile users.</summary>
    private static (string Label,string Kind) MemberKind(LqrDatabase db,int memberClass)
    {
        var record = ById(db["AIWarPartyMemberClasses"],memberClass);
        for (int i = 1; i <= 10; i++)
        {
            int descriptor = I(record,$"Descriptor{i}");
            if (descriptor < 0) continue;
            string name = ById(db["AIUnitDescriptors"],descriptor).Name;
            string modifier = ById(db["AIUnitDescriptorModifiers"],I(record,$"Descriptor{i}Modifier")).Name;
            if ((modifier.EndsWith("DESIRED",StringComparison.Ordinal) || modifier.EndsWith("REQUIRED",StringComparison.Ordinal)) && !modifier.EndsWith("UNDESIRED",StringComparison.Ordinal))
            {
                if (name.EndsWith("MELEE_USER",StringComparison.Ordinal)) return (S(record,"Name"),"melee");
                if (name.EndsWith("MISSILE_USER",StringComparison.Ordinal)) return (S(record,"Name"),"missile");
            }
        }
        return (S(record,"Name"),"any");
    }

    private static JsonObject WarParty(LqrDatabase db,string name)
    {
        var record = Named(db["AIWarParties"],name);
        var members = new JsonArray();
        for (int i = 1; i <= 5; i++)
        {
            if (I(record,$"MemberClass{i}") < 0) continue;
            var (label,kind) = MemberKind(db,I(record,$"MemberClass{i}"));
            members.Add(new JsonObject { ["class"] = label, ["kind"] = kind, ["share_ppm"] = Ppm(F(record,$"MemberClass{i}Percentage")) });
        }
        return new JsonObject { ["min_size"] = V(record,"MinSize"), ["max_size"] = V(record,"MaxSize"), ["members"] = members };
    }

    /// <summary>skirmish_ai.export: the AI columns of the given buildings and units, the three war parties and the personalities.</summary>
    public static JsonObject Ai(LqrDatabase db,IEnumerable<string> buildings,IEnumerable<string> units,string install,string language,string root)
    {
        var buildingRules = new JsonObject();
        foreach (string name in buildings)
        {
            var record = Named(db["Buildings"],name);
            int cls = I(record,"AIBuildingClass");
            buildingRules[name] = new JsonObject
            {
                ["class"] = BuildingClasses[cls < 0 ? BuildingClasses.Length+cls : cls], ["priority"] = V(record,"AIBuildingPriority"),
                ["must_have_first_pass"] = B(record,"AIMustHaveFirstPass"), ["max_default"] = V(record,"AIMaxDefault"),
                ["lower_priority_if_exists"] = B(record,"AILowerPriorityIfExists"), ["limit_multiples"] = B(record,"AILimitMultiples"),
                ["defensive_score"] = V(record,"AIDefensiveScore"), ["offensive_score"] = V(record,"AIOffensiveScore"),
            };
        }
        var unitRules = new JsonObject();
        foreach (string name in units)
        {
            var record = Named(db["Units"],name);
            var origin = db["Buildings"].Get(I(record,"AIBuildingOfOrigin"));
            unitRules[name] = new JsonObject
            {
                ["origin"] = origin?.Name, ["never_add"] = B(record,"AINeverAddUnit"), ["always_add"] = B(record,"AIAlwaysAddUnit"),
                ["assault"] = V(record,"AIAssaultEffectiveness"), ["raiding"] = V(record,"AIRaidingEffectiveness"),
                ["max_per_party"] = V(record,"AIMaxUnitsOfTypePerWarParty"), ["max_per_party_easy"] = V(record,"AIMaxUnitsOfTypePerWarPartyEasy"),
            };
        }
        return new JsonObject
        {
            ["format_version"] = 1, ["personalities"] = Personalities(install,language,root), ["default_personality"] = "Default",
            ["buildings"] = buildingRules, ["units"] = unitRules,
            ["war_parties"] = new JsonObject
            {
                ["ASSAULT"] = WarParty(db,"AIWARPARTY_ASSAULT"), ["RAIDING"] = WarParty(db,"AIWARPARTY_RAIDING"),
                ["PLACEOFPOWERSCOUT"] = WarParty(db,"AIWARPARTY_PLACEOFPOWERSCOUT"),
            },
            ["policy"] = AiPolicy,
        };
    }
}
