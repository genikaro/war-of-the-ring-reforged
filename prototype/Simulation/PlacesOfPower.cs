namespace Reforged.Simulation;

/// <summary>A source place of power (PlacesOfPower): its control radius, the units it affects and its modifier
/// (PlaceOfPowerModifiers name, float parameter in millionths, long parameter).</summary>
public sealed record PlaceOfPowerRule(int Id,string Name,string DisplayName,int RadiusMm,bool Random,bool AllUnits,bool HeroesOnly,int[] UnitTypes,
    string Modifier,int FloatPpm,int Long)
{
    public bool Affects(UnitDefinition unit) => HeroesOnly ? unit.Hero : AllUnits || UnitTypes.Contains(unit.Id);
}

/// <summary>A place of power on the map: the map object, the source places it stands for, and who controls it.</summary>
public sealed class PlaceOfPower(int id,string type,string name,int x,int z,PlaceOfPowerRule[] rules)
{
    public int Id { get; } = id;
    public string Type { get; } = type;
    public string Name { get; } = name;
    public int X { get; } = x;
    public int Z { get; } = z;
    public PlaceOfPowerRule[] Rules { get; } = rules;
    public int RadiusMm { get; } = rules.Select(r => r.RadiusMm).DefaultIfEmpty(13000).Max();
    /// <summary>The controlling team, or -1 before anyone took it.</summary>
    public int Owner { get; internal set; } = -1;
    public bool Contested { get; internal set; }
    public int ReadyAt { get; internal set; }
}

/// <summary>A place of power changing hands this tick (source PlayAlert: "play text/audio alert when this place of power
/// switches hands").</summary>
public sealed record PlaceCapture(int Tick,int Place,int Team,int Previous);

/// <summary>Places of power (build-54). The manual: "To gain control over a Place of Power, you must move your units into
/// the control zone AND be the only faction present within the zone. Enemies in the zone must be destroyed before your
/// forces can control the location"; then "all units in that army gain a benefit". Every source place must be captured
/// (MustCapture) and none limits its benefit to its zone (MustBeInRadius), so a team's units anywhere carry the bonuses
/// of every place it controls. Control changes the moment one team's living units are alone in the zone, workers and
/// heroes included, and stays after they leave, until another team is alone there. Simulated modifiers: ARMOR_CLASS
/// (+armor points), HEALTH_REGENERATION_RATE (+HP per second), MAX_HEALTH (x, current health keeping its share),
/// DAMAGE_ADDITIVE (+damage per second, as techniques' +1 attack), MELEE_DAMAGE, RANGED_DAMAGE, DAMAGE (x),
/// SIEGE_DAMAGE (x against buildings), COOL_DOWN (x ability recharge), FATE_GATHER_RATE, EXPERIENCE,
/// RUNNING_SPEED, LINE_OF_SIGHT, COST and the three additive recruitment costs. Random markers resolve to complete
/// source objects (including all their default rules) from the match seed and object id.</summary>
public sealed partial class SimWorld
{
    public const string ArmorModifier = "ARMOR_CLASS",RegenerationModifier = "HEALTH_REGENERATION_RATE",MaxHealthModifier = "MAX_HEALTH",
        DamageAdditiveModifier = "DAMAGE_ADDITIVE",MeleeDamageModifier = "MELEE_DAMAGE",RangedDamageModifier = "RANGED_DAMAGE",
        DamageModifier = "DAMAGE",SiegeDamageModifier = "SIEGE_DAMAGE",CoolDownModifier = "COOL_DOWN",
        RunningSpeedModifier = "RUNNING_SPEED",SightModifier = "LINE_OF_SIGHT",CostModifier = "COST",
        FoodCostModifier = "FOOD_COST_ADDITIVE",OreCostModifier = "ORE_COST_ADDITIVE",FateCostModifier = "FATE_COST_ADDITIVE",
        SpecificDamageModifier = "DAMAGE_ADDITIVE_VERSUS_SPECIFIC_UNITS";
    public static readonly IReadOnlySet<string> SimulatedPlaceModifiers = new HashSet<string> { ArmorModifier,RegenerationModifier,MaxHealthModifier,
        DamageAdditiveModifier,MeleeDamageModifier,RangedDamageModifier,DamageModifier,SiegeDamageModifier,CoolDownModifier,FateGatherModifier,ExperienceModifier,
        RunningSpeedModifier,SightModifier,CostModifier,FoodCostModifier,OreCostModifier,FateCostModifier,SpecificDamageModifier };

    /// <summary>Every source place of power by id, and every place-of-power object type with its display name and default places.</summary>
    public Dictionary<int,PlaceOfPowerRule> PlaceRules { get; } = [];
    public Dictionary<string,(string Name,int[] Places)> PlaceObjects { get; } = [];
    /// <summary>Source landmarks excluded from skirmishes, including explicit map placements. They resolve through the allowed random pool.</summary>
    public HashSet<string> ExcludedSkirmishPlaces { get; } = new(StringComparer.Ordinal);
    public List<PlaceOfPower> PlacesOfPower { get; } = [];
    /// <summary>This tick's changes of hands.</summary>
    public List<PlaceCapture> PlaceCaptures { get; } = [];
    /// <summary>Chooses random places of power; the setup screen draws one per match and Restart keeps it.</summary>
    public int MatchSeed { get; set; }

    /// <summary>Landmarks the map places by name that stay themselves (build-161). The map registers them before any of its
    /// places, so a random marker loaded first never draws one of them.</summary>
    public HashSet<string> MapPlaceTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>Notes the map's place-of-power objects before they are added: the named landmarks that stay themselves.</summary>
    public void ReserveMapPlaces(IEnumerable<string> types) =>
        MapPlaceTypes.UnionWith(types.Where(t => PlaceObjects.ContainsKey(t) && t != RandomPlaceObject && !ExcludedSkirmishPlaces.Contains(t)));

    public const string RandomPlaceObject = "OBJECT_PLACE_OF_POWER_RANDOM";

    /// <summary>Places a map's place-of-power object: its default source places, or for the random object one of the places
    /// allowed random placement. Build-161: no landmark stands twice on a map. A random marker, an excluded landmark's
    /// stand-in or a repeat of a landmark already placed draws among those the map does not hold yet (all of them only
    /// when the map has more markers than landmarks).</summary>
    public PlaceOfPower AddPlaceOfPower(int id,string type,int x,int z)
    {
        var (name,places) = PlaceObjects.TryGetValue(type,out var known) ? known : (type,[]);
        PlaceOfPowerRule[] rules = [.. places.Where(PlaceRules.ContainsKey).Select(p => PlaceRules[p])];
        bool repeat = PlaceObjects.ContainsKey(type) && PlacesOfPower.Any(p => p.Type == type);
        if (type == RandomPlaceObject || ExcludedSkirmishPlaces.Contains(type) || repeat)
        {
            var pool = PlaceObjects.Where(o => !ExcludedSkirmishPlaces.Contains(o.Key)
                    && o.Value.Places.Any(p => PlaceRules.TryGetValue(p,out var r) && r.Random))
                .OrderBy(o => o.Key,StringComparer.Ordinal).ToArray();
            if (pool.Length > 0)
            {
                // The seed and the object's id, mixed so neighbouring seeds draw unrelated places.
                uint hash = unchecked((uint)MatchSeed*2654435761u^(uint)id*2246822519u);
                hash = unchecked((hash^hash >> 15)*2654435761u); hash ^= hash >> 13;
                var pick = pool[(int)(hash%(uint)pool.Length)];
                // A landmark the map already holds draws again among the free ones: still uniform over them, and a
                // draw that did not collide stays what it was before build-161.
                var taken = PlacesOfPower.Select(p => p.Type).Concat(MapPlaceTypes).ToHashSet(StringComparer.Ordinal);
                var free = pool.Where(o => !taken.Contains(o.Key)).ToArray();
                if (taken.Contains(pick.Key) && free.Length > 0)
                {
                    hash = unchecked((hash^0x9e3779b9u)*0x85ebca6bu); hash ^= hash >> 16;
                    pick = free[(int)(hash%(uint)free.Length)];
                }
                type = pick.Key; name = pick.Value.Name;
                rules = [.. pick.Value.Places.Where(PlaceRules.ContainsKey).Select(p => PlaceRules[p])];
            }
        }
        var place = new PlaceOfPower(id,type,name,x,z,rules);
        PlacesOfPower.Add(place);
        return place;
    }

    private void UpdatePlacesOfPower()
    {
        bool changed = false;
        foreach (var place in PlacesOfPower)
        {
            int alone = -1; bool contested = false;
            foreach (var unit in NearUnits(place.X,place.Z,place.RadiusMm))
            {
                if (!unit.Present || unit.Team < 0 || DistanceSquared(unit.X,unit.Z,place.X,place.Z) > (long)place.RadiusMm*place.RadiusMm) continue;
                if (alone < 0) alone = unit.Team;
                else if (alone != unit.Team) { contested = true; break; }
            }
            place.Contested = contested;
            if (contested || alone < 0 || alone == place.Owner) continue;
            PlaceCaptures.Add(new(Tick,place.Id,alone,place.Owner));
            place.Owner = alone; changed = true;
        }
        if (changed) foreach (var unit in Units) ApplyPlaceBonuses(unit);
    }

    /// <summary>The bonuses of every place the unit's team controls whose rules cover its type.</summary>
    private void ApplyPlaceBonuses(SimUnit unit)
    {
        int dps = 0,armor = 0,regeneration = 0;
        long maxHealth = 1_000_000,damage = 1_000_000,melee = 1_000_000,ranged = 1_000_000,siege = 1_000_000,cooldown = 1_000_000,
            speed = 1_000_000,sight = 1_000_000;
        foreach (var place in PlacesOfPower)
        {
            if (place.Owner < 0 || place.Owner != unit.Team) continue;
            foreach (var rule in place.Rules.Where(r => r.Affects(unit.Definition)))
                switch (rule.Modifier)
                {
                    case ArmorModifier: armor += rule.Long*1000; break;
                    case RegenerationModifier: regeneration += rule.FloatPpm/1000; break;
                    case MaxHealthModifier: maxHealth = maxHealth*rule.FloatPpm/1_000_000; break;
                    case DamageAdditiveModifier: dps += rule.Long*1000; break;
                    case MeleeDamageModifier: melee = melee*rule.FloatPpm/1_000_000; break;
                    case RangedDamageModifier: ranged = ranged*rule.FloatPpm/1_000_000; break;
                    case DamageModifier: damage = damage*rule.FloatPpm/1_000_000; break;
                    case SiegeDamageModifier: siege = siege*rule.FloatPpm/1_000_000; break;
                    case CoolDownModifier: cooldown = cooldown*rule.FloatPpm/1_000_000; break;
                    case RunningSpeedModifier: speed = speed*rule.FloatPpm/1_000_000; break;
                    case SightModifier: sight = sight*rule.FloatPpm/1_000_000; break;
                }
        }
        unit.PlaceDpsMilli = dps; unit.PlaceArmorMilli = armor; unit.PlaceRegenMilli = regeneration;
        unit.PlaceDamagePpm = (int)damage; unit.PlaceMeleePpm = (int)melee; unit.PlaceRangedPpm = (int)ranged;
        unit.PlaceSiegePpm = (int)siege; unit.PlaceCooldownPpm = (int)cooldown;
        unit.PlaceSpeedPpm = (int)speed; unit.PlaceSightPpm = (int)sight;
        if (maxHealth != unit.PlaceMaxHealthPpm)
        {
            // Health keeps its share of the maximum, as a wounded unit's bar does.
            long before = unit.MaxHealth;
            unit.PlaceMaxHealthPpm = (int)maxHealth;
            if (unit.Health > 0 && before > 0) unit.HealthMilli = Math.Max(1,unit.HealthMilli*unit.MaxHealth/before);
        }
    }

    /// <summary>A unit's damage this hit after its places' multipliers: ranged or melee, all damage, and siege against buildings.</summary>
    private long PlaceDamage(SimUnit unit,long damage,bool ranged,bool building) =>
        damage*(ranged ? unit.PlaceRangedPpm : unit.PlaceMeleePpm)/1_000_000*unit.PlaceDamagePpm/1_000_000*(building ? unit.PlaceSiegePpm : 1_000_000)/1_000_000;

    /// <summary>Source targeted additive attack: the listed types are victims, not recipients.</summary>
    private int PlaceDamageAgainst(SimUnit source,SimUnit victim) => PlacesOfPower.Where(p => p.Owner == source.Team && p.Owner >= 0)
        .SelectMany(p => p.Rules).Where(r => r.Modifier == SpecificDamageModifier && r.UnitTypes.Contains(victim.Definition.Id))
        .Sum(r => r.Long*1000);

    /// <summary>Recruitment prices at order time; never change the source definitions. Additive discounts follow multipliers.</summary>
    public ProductionDefinition TrainingCost(int team,ProductionDefinition rules)
    {
        if (!Rules.Units.TryGetValue(rules.Unit,out var unit)) return rules;
        long scale = 1_000_000; int food = 0,ore = 0,fate = 0;
        foreach (var rule in PlacesOfPower.Where(p => p.Owner == team && team >= 0).SelectMany(p => p.Rules).Where(r => r.Affects(unit)))
            switch (rule.Modifier)
            {
                case CostModifier: scale = scale*rule.FloatPpm/1_000_000; break;
                case FoodCostModifier: food += rule.Long; break;
                case OreCostModifier: ore += rule.Long; break;
                case FateCostModifier: fate += rule.Long; break;
            }
        return rules with { FoodCost = Math.Max(0,(int)(rules.FoodCost*scale/1_000_000)+food),
            OreCost = Math.Max(0,(int)(rules.OreCost*scale/1_000_000)+ore),FateCost = Math.Max(0,rules.FateCost+fate) };
    }

    private string PlacesDigest() => ":excluded-places:"+string.Join('/',ExcludedSkirmishPlaces.Order(StringComparer.Ordinal))
        +":places:"+string.Join(';',PlacesOfPower.Select(p => $"{p.Id},{p.Type},{string.Join('/',p.Rules.Select(r => r.Id))},{p.Owner},{p.Contested},{p.ReadyAt}"))
        +":place-bonuses:"+string.Join(';',Units.Select(u => $"{u.PlaceDpsMilli},{u.PlaceArmorMilli},{u.PlaceRegenMilli},{u.PlaceMaxHealthPpm},{u.PlaceDamagePpm},{u.PlaceMeleePpm},{u.PlaceRangedPpm},{u.PlaceSiegePpm},{u.PlaceCooldownPpm},{u.PlaceSpeedPpm},{u.PlaceSightPpm}"))
        +PlacePowersDigest();
}
