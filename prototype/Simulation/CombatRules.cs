using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Reforged.Simulation;

public sealed record MeleeHit(int Tick,int Scale,string Joint = "",double SourceFraction = -1);
public sealed record MeleeAttack(string Clip,int Weight,int DurationTicks,MeleeHit[] Hits,bool Melee = false);
public sealed record UnitDefinition(int Id,string Name,string DisplayName,int MaxHealth,int ArmorMilli,int DamageType,
    string DamageTypeName,int MaxAttackers,int DpsMilli,int ReachMm,string ReadyClip,MeleeAttack[] Attacks,int StartingHealthPercent = 100,
    int WalkScale = 500_000,int RunScale = 1_000_000,string WalkClip = "",string RunClip = "",int BodyRadiusMm = 350,int BodyHeightMm = 700,int SelectionRadiusMm = 650,int AttackGapMm = 0,
    int FriendlyCompressionPpm = 1_000_000,int ModelScalePpm = 1_000_000,
    int ProjectileSpeedMmPerSecond = 0,int MinimumRangeMm = 0,
    int MeleeDpsMilli = 0,int MeleeReachMm = 2000,int MeleeGapMm = 0,AbilityDefinition[]? Abilities = null,bool Hero = false,
    int RegenerationPpm = 1_000_000,int SiegePpm = 1_000_000,bool Passive = false,int SightRangeMm = 20000,int DetectionMask = 0,
    int DamagePerHitMilli = 0,bool KnockbackImmune = false,string DazedClip = "",string KnockbackClip = "",
    HeroExperienceDefinition? Experience = null,int WeaponExperiencePpm = 1_000_000,int MeleeWeaponExperiencePpm = 1_000_000,StatusProtection NativeImmunities = StatusProtection.None,
    int KnockbackTicks = 0,string GetUpClip = "",int GetUpTicks = 0,
    bool CriticallyKillable = false,bool CriticalWeapon = false,bool CriticalMeleeWeapon = false,
    int SplashRadiusMm = 0,int SplashDpsMilli = 0,int FirePpmPerSecond = 0)
{
    // Build-171: SplashRadiusMm / SplashDpsMilli are the main weapon's source AreaOfEffectRange / AreaOfEffectDamage (a rate
    // per second like DpsMilli, so a hit's splash is that rate over the hit's share of the attack); FirePpmPerSecond is a melee
    // weapon's FireDamagePerSecond, the fire it puts into the buildings it strikes.
    // Passive: never picks a fight nor answers one (the Sandbox's training dummies, build-59).
    public bool IsRanged => ProjectileSpeedMmPerSecond > 0;
    public bool HasMelee => IsRanged && Attacks.Any(a => a.Melee);
    public bool FiresProjectile(int attack) => IsRanged && !Attacks[attack].Melee;
    /// <summary>Mean time between hit events, using the source choice weights. A ranged unit's
    /// missile and contact attacks have separate cadences.</summary>
    public decimal SecondsPerHit(bool melee = false)
    {
        decimal ticks = 0,hits = 0;
        foreach (var attack in Attacks)
        {
            if (IsRanged && attack.Melee != melee) continue;
            ticks += (decimal)attack.Weight*attack.DurationTicks;
            hits += (decimal)attack.Weight*attack.Hits.Length;
        }
        return hits > 0 ? ticks/(SimWorld.TicksPerSecond*hits) : 1;
    }

    /// <summary>The displayed base hit is authoritative: animation variants change timing, not damage.
    /// Explicit per-hit weapons keep their exact value; other weapons use the rounded HUD value.</summary>
    public long BaseHitDamageMilli(bool melee = false)
    {
        if (DamagePerHitMilli > 0 && !melee) return DamagePerHitMilli;
        int dps = IsRanged && melee ? MeleeDpsMilli : DpsMilli;
        decimal damage = dps*SecondsPerHit(melee);
        decimal quantum = damage is > 0 and < 1000 ? 100 : 1000;
        return (long)(Math.Round(damage/quantum,MidpointRounding.AwayFromZero)*quantum);
    }

    public long HitDamageAtRate(long rate,bool melee = false)
        => BaseHitDamageMilli(melee)*rate/Math.Max(1,IsRanged && melee ? MeleeDpsMilli : DpsMilli);

    public int HitDurationPpm(bool melee = false)
        => (int)(BaseHitDamageMilli(melee)*1_000_000/Math.Max(1,IsRanged && melee ? MeleeDpsMilli : DpsMilli));

    public long HitDamage(MeleeAttack attack,int hit,long rate) => HitDamageAtRate(rate,IsRanged && attack.Melee);

    /// <summary>Build-95: one source punch every 1.5 s; also adapts existing content packs.</summary>
    public UnitDefinition WithWorkerCombat() => Name != "UNIT_G_HUMAN_WORKER" ? this : this with {
        DpsMilli = 3333,DamagePerHitMilli = 5000,
        Attacks = [new("G_Human_Worker_Attack_Melee_01",1,30,[new(18,1_000_000)])]
    };
    // A stable probe profile preserves the existing navigation/spacing fixtures.
    // Actual demo units always receive a definition from the hashed rules cache.
    public static UnitDefinition Probe { get; } = new(-1,"probe","Probe",100,0,-1,"Unclassified",0,12000,2000,"",
        [new("",1,20,[new(0,1_000_000)])]);

    public void Validate()
    {
        Experience?.Validate();
        if (Experience!=null && !Hero || WeaponExperiencePpm is < 0 or > 10_000_000 || MeleeWeaponExperiencePpm is < 0 or > 10_000_000)
            throw new ArgumentException("Invalid hero progression");
        if (Abilities != null)
        {
            if (Abilities.Any(a => a == null) || Abilities.GroupBy(a=>a.Slot).Any(g=>g.Count()>1 && !(g.Count()==2 && g.Any(a=>a.ReplacedByTechnique!=null && g.Any(b=>b!=a && b.RequiredTechnique==a.ReplacedByTechnique)))))
                throw new ArgumentException("Invalid hero ability slots");
            foreach (var ability in Abilities) ability.Validate();
        }
        if (SightRangeMm is < 0 or > 200000 || DetectionMask is < 0 or > 3
            || MaxHealth <= 0 || MaxHealth > 1_000_000 || ArmorMilli < 0 || DpsMilli < 0 || DpsMilli > 1_000_000
            || DamagePerHitMilli is < 0 or > 1_000_000 || DamagePerHitMilli > 0 && (DpsMilli == 0 || IsRanged || Attacks.Any(a => a.Hits.Length != 1))
            || ReachMm is < 700 or > 50000 || MaxAttackers < 0 || StartingHealthPercent is <= 0 or > 100
            || ProjectileSpeedMmPerSecond is < 0 or > 200000 || MinimumRangeMm < 0 || MinimumRangeMm >= ReachMm
            || (!IsRanged && MinimumRangeMm != 0)
            || MeleeDpsMilli is < 0 or > 1_000_000 || MeleeReachMm is < 700 or > 10000 || MeleeGapMm is < 0 or > 10000
            || WalkScale is <= 0 or > 1_000_000 || RunScale is <= 0 or > 3_000_000
            || BodyRadiusMm is < 100 or > 5000 || BodyHeightMm is < 100 or > 15000 || SelectionRadiusMm is < 100 or > 6000
            || AttackGapMm is < 0 or > 10000 || RegenerationPpm is < 0 or > 10_000_000 || SiegePpm is <= 0 or > 10_000_000
            || FriendlyCompressionPpm is < 500_000 or > 1_000_000 || ModelScalePpm is < 500_000 or > 2_000_000
            || SplashRadiusMm is < 0 or > 20000 || SplashDpsMilli is < 0 or > 1_000_000 || FirePpmPerSecond is < 0 or > 100_000_000
            || Attacks == null || Attacks.Length == 0 || string.IsNullOrEmpty(Name)) throw new ArgumentException("Invalid unit definition");
        if (IsRanged && Attacks.All(a => a.Melee)) throw new ArgumentException("Ranged unit has no missile attack");
        foreach (var attack in Attacks)
            if (attack.Weight <= 0 || attack.DurationTicks is <= 0 or > 1200 || attack.Hits == null || attack.Hits.Length == 0
                || attack.Hits.Any(h => h.Tick < 0 || h.Tick >= attack.DurationTicks || h.Scale is <= 0 or > 10_000_000)
                || !attack.Hits.Select(h => h.Tick).SequenceEqual(attack.Hits.Select(h => h.Tick).Order()))
                throw new ArgumentException("Invalid attack timeline");
    }
}

public sealed record UnitPairRule(int From,int To,int Scale,bool Invincible);
public sealed record DamageTypeRule(int From,int To,int Scale);

public sealed class CombatRules
{
    public static CombatRules Probe { get; } = new();
    public string Hash { get; private set; } = "probe-v1";
    public Dictionary<string,UnitDefinition> Units { get; } = [];
    public List<GlobalPowerDefinition> GlobalPowers { get; } = [];
    /// <summary>Health regeneration by unit state (build-52); none for older caches and the synthetic rules.</summary>
    public RegenerationRates Regeneration { get; set; } = RegenerationRates.None;
    private readonly Dictionary<(int From,int To),UnitPairRule> _pairs = [];
    private readonly Dictionary<(int From,int To),int> _types = [];

    public static CombatRules Parse(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("FormatVersion").GetInt32() != 3 || root.GetProperty("TicksPerSecond").GetInt32() != SimWorld.TicksPerSecond)
            throw new ArgumentException("Unsupported combat rules");
        var rules = new CombatRules { Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) };
        foreach (var item in root.GetProperty("TypeMatchups").EnumerateArray())
        {
            var type = item.Deserialize<DamageTypeRule>() ?? throw new ArgumentException("Missing damage type matchup");
            if (type.From is < 0 or > 3 || type.To is < 0 or > 3 || type.Scale is < 0 or > 10_000_000
                || !rules._types.TryAdd((type.From,type.To),type.Scale)) throw new ArgumentException("Invalid damage type matchup");
        }
        if (rules._types.Count != 16) throw new ArgumentException("Incomplete damage type matchups");
        foreach (var item in root.GetProperty("Units").EnumerateArray())
        {
            var unit = (item.Deserialize<UnitDefinition>() ?? throw new ArgumentException("Missing unit definition")).WithWorkerCombat();
            unit.Validate(); rules.Units.Add(unit.Name,unit);
        }
        if (root.TryGetProperty("Regeneration",out var regeneration))
        {
            rules.Regeneration = regeneration.Deserialize<RegenerationRates>() ?? throw new ArgumentException("Missing regeneration rates");
            rules.Regeneration.Validate();
        }
        foreach (var item in root.GetProperty("Pairs").EnumerateArray())
        {
            var pair = item.Deserialize<UnitPairRule>() ?? throw new ArgumentException("Missing unit pair");
            if (pair.Scale is < 0 or > 10_000_000) throw new ArgumentException("Invalid damage multiplier");
            rules._pairs.Add((pair.From,pair.To),pair);
        }
        if (root.TryGetProperty("GlobalPowers",out var powers))
            foreach (var item in powers.EnumerateArray())
            {
                var power = item.Deserialize<GlobalPowerDefinition>() ?? throw new ArgumentException("Missing global power");
                power.Validate();
                if (rules.GlobalPowers.Any(p => p.Id == power.Id || p.Slot == power.Slot && p.Faction == power.Faction)) throw new ArgumentException("Duplicate global power");
                rules.GlobalPowers.Add(power);
            }
        return rules;
    }

    public UnitPairRule Pair(int from,int to) => _pairs.GetValueOrDefault((from,to),new(from,to,1_000_000,false));
    public int TypeScale(int from,int to) => _types.GetValueOrDefault((from,to),1_000_000);
    /// <summary>The source has no type multipliers: its unit pair rows are the type cycle (150% advantage, 90% reverse,
    /// 85% against heroes). A pair row therefore replaces the type matchup, which only fills pairs the source leaves
    /// out (build-61; stacking both gave a Rohan Rider 187.5% against a Gondor Swordsman).</summary>
    public int DamageScale(UnitDefinition from,UnitDefinition to)
        => _pairs.TryGetValue((from.Id,to.Id),out var pair) ? pair.Scale : TypeScale(from.DamageType,to.DamageType);
}
