using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Reforged.Simulation;

public sealed record MeleeHit(int Tick,int Scale);
public sealed record MeleeAttack(string Clip,int Weight,int DurationTicks,MeleeHit[] Hits);
public sealed record UnitDefinition(int Id,string Name,string DisplayName,int MaxHealth,int ArmorMilli,int DamageType,
    string DamageTypeName,int MaxAttackers,int DpsMilli,int ReachMm,string ReadyClip,MeleeAttack[] Attacks,int StartingHealthPercent = 100,
    int WalkScale = 500_000,string WalkClip = "",string RunClip = "",int BodyRadiusMm = 350,int BodyHeightMm = 700,int SelectionRadiusMm = 650,int AttackGapMm = 0)
{
    // A stable probe profile preserves the existing navigation/spacing fixtures.
    // Actual demo units always receive a definition from the hashed rules cache.
    public static UnitDefinition Probe { get; } = new(-1,"probe","Probe",100,0,-1,"Unclassified",0,12000,2000,"",
        [new("",1,20,[new(0,1_000_000)])]);

    public void Validate()
    {
        if (MaxHealth <= 0 || MaxHealth > 1_000_000 || ArmorMilli < 0 || DpsMilli < 0 || DpsMilli > 1_000_000
            || ReachMm is < 700 or > 10000 || MaxAttackers < 0 || StartingHealthPercent is <= 0 or > 100
            || WalkScale is <= 0 or > 1_000_000
            || BodyRadiusMm is < 100 or > 5000 || BodyHeightMm is < 100 or > 15000 || SelectionRadiusMm is < 100 or > 6000
            || AttackGapMm is < 0 or > 10000
            || Attacks == null || Attacks.Length == 0 || string.IsNullOrEmpty(Name)) throw new ArgumentException("Invalid unit definition");
        foreach (var attack in Attacks)
            if (attack.Weight <= 0 || attack.DurationTicks is <= 0 or > 1200 || attack.Hits == null || attack.Hits.Length == 0
                || attack.Hits.Any(h => h.Tick < 0 || h.Tick >= attack.DurationTicks || h.Scale is <= 0 or > 10_000_000)
                || !attack.Hits.Select(h => h.Tick).SequenceEqual(attack.Hits.Select(h => h.Tick).Order()))
                throw new ArgumentException("Invalid melee attack timeline");
    }
}

public sealed record UnitPairRule(int From,int To,int Scale,bool Invincible);
public sealed record DamageTypeRule(int From,int To,int Scale);

public sealed class CombatRules
{
    public static CombatRules Probe { get; } = new();
    public string Hash { get; private set; } = "probe-v1";
    public Dictionary<string,UnitDefinition> Units { get; } = [];
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
