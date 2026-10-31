namespace Reforged.Simulation;

public sealed record HeroLevelDefinition(int Level,long ExperienceMilli,int HitPoints,int ArmorMilli,int DamageBonusPpm);
public sealed record HeroExperienceDefinition(int UnitDamagePpm,int BuildingDamagePpm,HeroLevelDefinition[] Levels)
{
    public void Validate()
    {
        if (UnitDamagePpm is < 0 or > 10_000_000 || BuildingDamagePpm is < 0 or > 10_000_000 || Levels is not {Length: >= 2 and <= 10}
            || Levels.Any(l=>l==null) || Levels.Where((l,i)=>l.Level!=i+1 || l.ExperienceMilli<0 || l.ExperienceMilli>1_000_000_000
                || l.HitPoints is < 0 or > 1_000_000 || l.ArmorMilli is < 0 or > 1_000_000 || l.DamageBonusPpm is < 0 or > 10_000_000).Any()
            || Levels[0] != new HeroLevelDefinition(1,0,0,0,0)
            || Levels.Zip(Levels.Skip(1)).Any(p=>p.Second.ExperienceMilli<=p.First.ExperienceMilli))
            throw new ArgumentException("Invalid hero experience rules");
    }
}

public sealed record HeroLevelEvent(int Tick,int Unit,int PreviousLevel,int Level);

public sealed partial class SimUnit
{
    public long ExperienceMicro { get; internal set; }
    public int HeroLevel { get; internal set; } = 1;
    public int MaxHeroLevel => Definition.Experience?.Levels.Length ?? 1;
    public HeroLevelDefinition? ExperienceBonus => Definition.Experience?.Levels[HeroLevel-1];
    public int ExperienceHitPoints => ExperienceBonus?.HitPoints ?? 0;
    public int ExperienceArmorMilli => ExperienceBonus?.ArmorMilli ?? 0;
    public int ExperienceDamageBonusPpm => ExperienceBonus?.DamageBonusPpm ?? 0;
    public long LevelStartExperienceMicro => (ExperienceBonus?.ExperienceMilli ?? 0)*1000;
    public long NextLevelExperienceMicro => (Definition.Experience?.Levels[Math.Min(HeroLevel,MaxHeroLevel-1)].ExperienceMilli ?? 0)*1000;
    public int ExperienceProgressPpm => Definition.Experience==null ? 0 : HeroLevel>=MaxHeroLevel ? 1_000_000
        : (int)Math.Clamp((ExperienceMicro-LevelStartExperienceMicro)*1_000_000/Math.Max(1,NextLevelExperienceMicro-LevelStartExperienceMicro),0,1_000_000);
    private int ExperienceDamage(int rate) => (int)((long)rate*(1_000_000+ExperienceDamageBonusPpm)/1_000_000*GlobalDamagePpm/1_000_000);
    public int MeleeWeaponDamageRate => (int)((long)ExperienceDamage(Definition.MeleeDpsMilli+TechniqueDpsMilli+PlaceDpsMilli)*SavageDamagePpm/1_000_000);
}

public sealed partial class SimWorld
{
    public const string ExperienceModifier = "EXPERIENCE";
    public List<HeroLevelEvent> HeroLevelEvents { get; } = [];
    private sealed record HeroExperienceRecord(int Unit,long ExperienceMicro,int Level);
    private readonly Dictionary<(int Team,string Type),HeroExperienceRecord> _heroExperienceRecords=[];

    /// <summary>The original saves a defeated hero's progression by player and hero type.
    /// Repeated death cleanup and recruitment must never overwrite a more recent life.</summary>
    private void RememberHeroExperience(SimUnit hero)
    {
        if (hero.Health>0 || hero.Team<0 || !hero.Definition.Hero || hero.Definition.Experience==null) return;
        var key=(hero.Team,hero.Definition.Name);
        if (_heroExperienceRecords.TryGetValue(key,out var saved) && saved.Unit>=hero.Id) return;
        _heroExperienceRecords[key]=new(hero.Id,hero.ExperienceMicro,hero.HeroLevel);
    }

    private void RestoreHeroExperience(SimUnit hero)
    {
        if (hero.Team<0 || !hero.Definition.Hero || hero.Definition.Experience is not {} rules) return;
        // Production runs before corpse cleanup. Also handle a scripted defeat followed by
        // instant recruitment in the same tick, before the normal death pass can save it.
        var fallen=Units.LastOrDefault(u=>u.Team==hero.Team && u.Health<=0 && u.Definition.Name==hero.Definition.Name
            && u.Definition.Hero && u.Definition.Experience!=null);
        if (fallen!=null) RememberHeroExperience(fallen);
        if (!_heroExperienceRecords.TryGetValue((hero.Team,hero.Definition.Name),out var saved)) return;
        hero.HeroLevel=Math.Clamp(saved.Level,1,hero.MaxHeroLevel);
        hero.ExperienceMicro=Math.Clamp(saved.ExperienceMicro,0,rules.Levels[^1].ExperienceMilli*1000);
        // Only progression carries over. The new life starts at its restored maximum,
        // before ApplyPlaceBonuses scales its health for the team's current places.
        hero.HealthMilli=(long)hero.MaxHealth*1000;
    }

    public int PlaceExperiencePpm(SimUnit unit)
    {
        long scale=1_000_000;
        foreach (var rule in PlacesOfPower.Where(p=>p.Owner==unit.Team).SelectMany(p=>p.Rules)
                     .Where(r=>r.Modifier==ExperienceModifier && r.Affects(unit.Definition)))
            scale=scale*rule.FloatPpm/1_000_000;
        return (int)Math.Clamp(scale,0,100_000_000);
    }

    private void EarnHeroExperience(SimUnit hero,int victimTeam,long damageMilli,bool building,int effectPpm)
    {
        if (!hero.Present || !hero.Definition.Hero || hero.Definition.Experience is not {} rules || hero.Team<0
            || victimTeam==hero.Team || damageMilli<=0 || effectPpm<=0 || hero.HeroLevel>=hero.MaxHeroLevel) return;
        Int128 award=(Int128)damageMilli*(building ? rules.BuildingDamagePpm : rules.UnitDamagePpm)/1000;
        award=award*effectPpm/1_000_000*PlaceExperiencePpm(hero)/1_000_000;
        long cap=rules.Levels[^1].ExperienceMilli*1000;
        hero.ExperienceMicro+=(long)Int128.Min(Math.Max(0,cap-hero.ExperienceMicro),award);
        int previous=hero.HeroLevel,maximum=hero.MaxHealth;
        long health=hero.HealthMilli;
        while (hero.HeroLevel<hero.MaxHeroLevel && hero.ExperienceMicro>=rules.Levels[hero.HeroLevel].ExperienceMilli*1000)
            hero.HeroLevel++;
        if (previous==hero.HeroLevel) return;
        // The original reapplies the old health fraction after adding the new HP bonus.
        hero.HealthMilli=Math.Clamp(health*hero.MaxHealth/Math.Max(1,maximum),1,(long)hero.MaxHealth*1000);
        HeroLevelEvents.Add(new(Tick,hero.Id,previous,hero.HeroLevel));
    }

    private static int WeaponExperiencePpm(SimUnit unit,int attack) => unit.Definition.IsRanged
        && attack>=0 && attack<unit.Definition.Attacks.Length && !unit.Definition.Attacks[attack].Melee
            ? unit.Definition.WeaponExperiencePpm : unit.Definition.MeleeWeaponExperiencePpm;

    private string HeroExperienceDigest() => ":hero-experience:"+string.Join(';',Units.Where(u=>u.Definition.Experience!=null)
        .Select(u=>$"{u.Id},{u.ExperienceMicro},{u.HeroLevel}"))
        +(_heroExperienceRecords.Count==0 ? "" : ":hero-revival:"+string.Join(';',_heroExperienceRecords
            .OrderBy(p=>p.Key.Team).ThenBy(p=>p.Key.Type,StringComparer.Ordinal)
            .Select(p=>$"{p.Key.Team},{p.Key.Type},{p.Value.Unit},{p.Value.ExperienceMicro},{p.Value.Level}")));
}
