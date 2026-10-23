namespace Reforged.Simulation;

/// <summary>Source health regeneration (build-52), milli-HP per second by unit state for ordinary units and for heroes:
/// the UnitAnimStates HealthRegenerationRate and HealthRegenerationRateHero columns of the animation state each
/// simulation state stands for. Idle, walking, the attack stance and casting regain 1 HP/s; running, gathering and
/// (for ordinary units) swinging regain nothing; heroes regain 0.167 HP/s mid-swing; building and repairing 2 HP/s.</summary>
public sealed record RegenerationRates(int IdleMilli,int IdleHeroMilli,int WalkMilli,int WalkHeroMilli,int RunMilli,int RunHeroMilli,
    int ReadyMilli,int ReadyHeroMilli,int AttackMilli,int AttackHeroMilli,int GatherMilli,int GatherHeroMilli,
    int RepairMilli,int RepairHeroMilli,int SpecialMilli,int SpecialHeroMilli)
{
    /// <summary>Rules without regeneration: caches exported before build-52 and the synthetic test rules.</summary>
    public static readonly RegenerationRates None = new(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);

    public void Validate()
    {
        int[] rates = [IdleMilli,IdleHeroMilli,WalkMilli,WalkHeroMilli,RunMilli,RunHeroMilli,ReadyMilli,ReadyHeroMilli,
            AttackMilli,AttackHeroMilli,GatherMilli,GatherHeroMilli,RepairMilli,RepairHeroMilli,SpecialMilli,SpecialHeroMilli];
        if (rates.Any(r => r is < 0 or > 100_000)) throw new ArgumentException("Invalid regeneration rate");
    }
}

/// <summary>Every tick a living unit regains its state's rate times its source HealthRegenerationMultiplier (heroes 2.25),
/// never above its maximum health; nothing while the One Ring is on (its OnActivate action disables regeneration),
/// inside a Mill or Foundry, or dead. Adapters: the rates read as HP per second (the base constant is in Rings.exe),
/// the mapping of simulation states to animation states, and whole milli-HP per tick. A held place of power adds its
/// bonus to the rate (Ancient Fountain +1 HP/s, build-54).</summary>
public sealed partial class SimWorld
{
    private void UpdateRegeneration()
    {
        var rates = Rules.Regeneration;
        if (rates == RegenerationRates.None) return;
        foreach (var unit in Units)
        {
            if (unit.Health <= 0 || unit.InsideSite >= 0 || unit.RingActive) continue;
            long most = (long)unit.MaxHealth*1000;
            if (unit.HealthMilli >= most) continue;
            // A place of power's bonus adds to the state's rate (the source modifier "increases the HP/second health regen rate").
            long gain = (long)(RegenerationRate(unit)+unit.PlaceRegenMilli)*unit.Definition.RegenerationPpm/1_000_000/TicksPerSecond;
            if (gain > 0) unit.HealthMilli = Math.Min(most,unit.HealthMilli+gain);
        }
    }

    /// <summary>The unit's regeneration rate this tick, milli-HP per second before its multiplier: casting, swinging,
    /// gathering or carrying a load, building or repairing on the spot, walking, running, in an attack stance, or idle.</summary>
    public int RegenerationRate(SimUnit unit)
    {
        var r = Rules.Regeneration;
        var (ordinary,hero) = unit.Casting ? (r.SpecialMilli,r.SpecialHeroMilli)
            : Swinging(unit) ? (r.AttackMilli,r.AttackHeroMilli)
            : unit.GatherPhase != GatherPhase.None || unit.ReturningCargo ? (r.GatherMilli,r.GatherHeroMilli)
            : unit.Order == OrderKind.Build && !unit.Moving ? (r.RepairMilli,r.RepairHeroMilli)
            : unit.Moving ? unit.Walking ? (r.WalkMilli,r.WalkHeroMilli) : (r.RunMilli,r.RunHeroMilli)
            : unit.Attacking ? (r.ReadyMilli,r.ReadyHeroMilli)
            : (r.IdleMilli,r.IdleHeroMilli);
        return unit.Definition.Hero ? hero : ordinary;
    }

    /// <summary>A swing under way: its start is kept after a fight until the next swing, so it is bounded by its length.</summary>
    private bool Swinging(SimUnit unit) => unit.StrikeActive && unit.AttackVariant >= 0
        && Tick-unit.AttackStartTick < unit.Definition.Attacks[unit.AttackVariant].DurationTicks;
}
