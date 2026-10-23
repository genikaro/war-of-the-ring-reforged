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
/// SIEGE_DAMAGE (x against buildings) and COOL_DOWN (x hero ability recharge). Not simulated yet: gather rates, fate,
/// line of sight, experience, costs, running speed, build rate and added or removed abilities. A random place of power
/// takes one source place allowed random placement, chosen from the match seed and the object's id.</summary>
public sealed partial class SimWorld
{
    public const string ArmorModifier = "ARMOR_CLASS",RegenerationModifier = "HEALTH_REGENERATION_RATE",MaxHealthModifier = "MAX_HEALTH",
        DamageAdditiveModifier = "DAMAGE_ADDITIVE",MeleeDamageModifier = "MELEE_DAMAGE",RangedDamageModifier = "RANGED_DAMAGE",
        DamageModifier = "DAMAGE",SiegeDamageModifier = "SIEGE_DAMAGE",CoolDownModifier = "COOL_DOWN";
    public static readonly IReadOnlySet<string> SimulatedPlaceModifiers = new HashSet<string> { ArmorModifier,RegenerationModifier,MaxHealthModifier,
        DamageAdditiveModifier,MeleeDamageModifier,RangedDamageModifier,DamageModifier,SiegeDamageModifier,CoolDownModifier };

    /// <summary>Every source place of power by id, and every place-of-power object type with its display name and default places.</summary>
    public Dictionary<int,PlaceOfPowerRule> PlaceRules { get; } = [];
    public Dictionary<string,(string Name,int[] Places)> PlaceObjects { get; } = [];
    public List<PlaceOfPower> PlacesOfPower { get; } = [];
    /// <summary>This tick's changes of hands.</summary>
    public List<PlaceCapture> PlaceCaptures { get; } = [];
    /// <summary>Chooses random places of power; the setup screen draws one per match and Restart keeps it.</summary>
    public int MatchSeed { get; set; }

    /// <summary>Places a map's place-of-power object: its default source places, or for the random object one of the places
    /// allowed random placement.</summary>
    public PlaceOfPower AddPlaceOfPower(int id,string type,int x,int z)
    {
        var (name,places) = PlaceObjects.TryGetValue(type,out var known) ? known : (type,[]);
        PlaceOfPowerRule[] rules = [.. places.Where(PlaceRules.ContainsKey).Select(p => PlaceRules[p])];
        if (places.Length == 0)
        {
            var pool = PlaceRules.Values.Where(r => r.Random).OrderBy(r => r.Id).ToArray();
            if (pool.Length > 0)
            {
                // The seed and the object's id, mixed so neighbouring seeds draw unrelated places.
                uint hash = unchecked((uint)MatchSeed*2654435761u^(uint)id*2246822519u);
                hash = unchecked((hash^hash >> 15)*2654435761u); hash ^= hash >> 13;
                var pick = pool[(int)(hash%(uint)pool.Length)];
                rules = [pick];
                // A place without a title of its own takes the name of the object that stands for it.
                string title = pick.DisplayName is "" or "Untitled"
                    ? PlaceObjects.Values.FirstOrDefault(o => o.Places.Contains(pick.Id)).Name ?? pick.Name : pick.DisplayName;
                name = $"{name}: {title}";
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
        long maxHealth = 1_000_000,damage = 1_000_000,melee = 1_000_000,ranged = 1_000_000,siege = 1_000_000,cooldown = 1_000_000;
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
                }
        }
        unit.PlaceDpsMilli = dps; unit.PlaceArmorMilli = armor; unit.PlaceRegenMilli = regeneration;
        unit.PlaceDamagePpm = (int)damage; unit.PlaceMeleePpm = (int)melee; unit.PlaceRangedPpm = (int)ranged;
        unit.PlaceSiegePpm = (int)siege; unit.PlaceCooldownPpm = (int)cooldown;
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

    private string PlacesDigest() => ":places:"+string.Join(';',PlacesOfPower.Select(p => $"{p.Id},{string.Join('/',p.Rules.Select(r => r.Id))},{p.Owner}"))
        +":place-bonuses:"+string.Join(';',Units.Select(u => $"{u.PlaceDpsMilli},{u.PlaceArmorMilli},{u.PlaceRegenMilli},{u.PlaceMaxHealthPpm},{u.PlaceDamagePpm},{u.PlaceMeleePpm},{u.PlaceRangedPpm},{u.PlaceSiegePpm},{u.PlaceCooldownPpm}"));
}
