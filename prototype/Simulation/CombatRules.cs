using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Reforged.Simulation;

public sealed record MeleeHit(int Tick,int Scale);
public sealed record MeleeAttack(string Clip,int Weight,int DurationTicks,MeleeHit[] Hits,bool Melee = false);
public sealed record UnitDefinition(int Id,string Name,string DisplayName,int MaxHealth,int ArmorMilli,int DamageType,
    string DamageTypeName,int MaxAttackers,int DpsMilli,int ReachMm,string ReadyClip,MeleeAttack[] Attacks,int StartingHealthPercent = 100,
    int WalkScale = 500_000,int RunScale = 1_000_000,string WalkClip = "",string RunClip = "",int BodyRadiusMm = 350,int BodyHeightMm = 700,int SelectionRadiusMm = 650,int AttackGapMm = 0,
    int FriendlyCompressionPpm = 1_000_000,int ModelScalePpm = 1_000_000,
    int ProjectileSpeedMmPerSecond = 0,int MinimumRangeMm = 0,
    int MeleeDpsMilli = 0,int MeleeReachMm = 2000,int MeleeGapMm = 0,AbilityDefinition[]? Abilities = null,bool Hero = false,
    int RegenerationPpm = 1_000_000,int SiegePpm = 1_000_000,bool Passive = false)
{
    // Passive: never picks a fight nor answers one (the Sandbox's training dummies, build-59).
    public bool IsRanged => ProjectileSpeedMmPerSecond > 0;
    public bool HasMelee => IsRanged && Attacks.Any(a => a.Melee);
    public bool FiresProjectile(int attack) => IsRanged && !Attacks[attack].Melee;
    // A stable probe profile preserves the existing navigation/spacing fixtures.
    // Actual demo units always receive a definition from the hashed rules cache.
    public static UnitDefinition Probe { get; } = new(-1,"probe","Probe",100,0,-1,"Unclassified",0,12000,2000,"",
        [new("",1,20,[new(0,1_000_000)])]);

    public void Validate()
    {
        if (Abilities != null)
        {
            if (Abilities.Any(a => a == null) || Abilities.Select(a => a.Slot).Distinct().Count() != Abilities.Length)
                throw new ArgumentException("Invalid hero ability slots");
            foreach (var ability in Abilities) ability.Validate();
        }
        if (MaxHealth <= 0 || MaxHealth > 1_000_000 || ArmorMilli < 0 || DpsMilli < 0 || DpsMilli > 1_000_000
            || ReachMm is < 700 or > 50000 || MaxAttackers < 0 || StartingHealthPercent is <= 0 or > 100
            || ProjectileSpeedMmPerSecond is < 0 or > 200000 || MinimumRangeMm < 0 || MinimumRangeMm >= ReachMm
            || (!IsRanged && MinimumRangeMm != 0)
            || MeleeDpsMilli is < 0 or > 1_000_000 || MeleeReachMm is < 700 or > 10000 || MeleeGapMm is < 0 or > 10000
            || WalkScale is <= 0 or > 1_000_000 || RunScale is <= 0 or > 3_000_000
            || BodyRadiusMm is < 100 or > 5000 || BodyHeightMm is < 100 or > 15000 || SelectionRadiusMm is < 100 or > 6000
            || AttackGapMm is < 0 or > 10000 || RegenerationPpm is < 0 or > 10_000_000 || SiegePpm is <= 0 or > 10_000_000
            || FriendlyCompressionPpm is < 500_000 or > 1_000_000 || ModelScalePpm is < 500_000 or > 2_000_000
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
            var unit = item.Deserialize<UnitDefinition>() ?? throw new ArgumentException("Missing unit definition");
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
        return rules;
    }

    public UnitPairRule Pair(int from,int to) => _pairs.GetValueOrDefault((from,to),new(from,to,1_000_000,false));
    public int TypeScale(int from,int to) => _types.GetValueOrDefault((from,to),1_000_000);
}
