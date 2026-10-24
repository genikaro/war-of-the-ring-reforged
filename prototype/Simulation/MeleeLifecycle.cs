namespace Reforged.Simulation;

public enum CombatEventKind { Start, Hit, Cancel, Release }
/// <summary>Unit is -1 for a defending building's arrow, which Site then names.</summary>
public sealed record CombatEvent(int Tick,CombatEventKind Kind,int Unit,int Target,int Attack,int Hit,long DamageMilli,
    int StartTick,int X,int Y,int Z,int Site = -1);

public sealed partial class SimWorld
{
    public List<CombatEvent> CombatEvents { get; } = [];

    private void CancelStrike(SimUnit unit)
    {
        if (unit.AttackStartTick < 0) return;
        CombatEvents.Add(new(Tick,CombatEventKind.Cancel,unit.Id,unit.AttackVictim,unit.AttackVariant,-1,0,
            unit.AttackStartTick,unit.X,unit.Y,unit.Z));
        unit.AttackStartTick = unit.AttackVictim = unit.AttackVariant = -1; unit.NextHit = 0;
        // Preserve recovery after cancellation: repeating Stop cannot bypass cadence.
    }

    private bool AttackerSlot(SimUnit unit,SimUnit target)
    {
        if (UsesMissile(unit,target)) return true;
        int limit = target.Definition.MaxAttackers;
        if (limit == 0) return true;
        return NearUnits(target.X,target.Z,AnyReachBound).Where(u => u.Present && u.Target == target.Id && !UsesMissile(u,target)
            && (HoldsPosition(u) || RoomToFight(u))
            && AttackReach(u,u.Node,u.X,u.Y,u.Z,target)).OrderBy(u => u.Id).Take(limit).Any(u => u.Id == unit.Id);
    }

    private void Strike(SimUnit unit,SimUnit target)
    {
        bool melee = unit.Definition.HasMelee && !UsesMissile(unit,target);
        if (unit.AttackStartTick >= 0 && (unit.AttackVictim != target.Id
            || unit.Definition.IsRanged && unit.Definition.Attacks[unit.AttackVariant].Melee != melee)) CancelStrike(unit);
        if (unit.AttackStartTick >= 0 && Tick-unit.AttackStartTick >= unit.Definition.Attacks[unit.AttackVariant].DurationTicks)
        {
            unit.AttackStartTick = unit.AttackVictim = unit.AttackVariant = -1; unit.NextHit = 0;
        }
        if (unit.AttackStartTick < 0 && unit.Cooldown == 0)
        {
            if (!FacingAligned(unit,target)) return;
            // Per-unit stateless weighted choice; no renderer or global RNG can
            // affect clip selection, damage timing or another unit's choices.
            var eligible = Enumerable.Range(0,unit.Definition.Attacks.Length)
                .Where(i => !unit.Definition.IsRanged || unit.Definition.Attacks[i].Melee == melee).ToArray();
            long total = eligible.Sum(i => (long)unit.Definition.Attacks[i].Weight);
            long pick = ((long)unit.Id*73856093+unit.AttackSerial*19349663L)%total;
            int choice = 0;
            while (pick >= unit.Definition.Attacks[eligible[choice]].Weight) pick -= unit.Definition.Attacks[eligible[choice++]].Weight;
            int variant = eligible[choice];
            unit.AttackVariant = variant; unit.AttackSerial++; unit.AttackStartTick = Tick;
            unit.AttackVictim = target.Id; unit.NextHit = 0; unit.Cooldown = unit.Definition.Attacks[variant].DurationTicks;
            CombatEvents.Add(new(Tick,CombatEventKind.Start,unit.Id,target.Id,variant,-1,0,Tick,unit.X,unit.Y,unit.Z));
        }
        if (unit.AttackStartTick < 0) return;
        var attack = unit.Definition.Attacks[unit.AttackVariant];
        int elapsed = Tick-unit.AttackStartTick;
        while (unit.NextHit < attack.Hits.Length && attack.Hits[unit.NextHit].Tick <= elapsed)
        {
            int index = unit.NextHit++;
            if (!target.Present || unit.Target != target.Id || !AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target)) continue;
            var pair = Rules.Pair(unit.Definition.Id,target.Definition.Id);
            int dps = melee ? unit.Definition.MeleeDpsMilli+unit.TechniqueDpsMilli+unit.PlaceDpsMilli : unit.MeleeDamageRate;
            long typedDps = (long)dps*Rules.DamageScale(unit.Definition,target.Definition)/1_000_000;
            typedDps = typedDps*unit.AbilityDamageScale(target.Definition.Id)/1_000_000;
            long rate = Math.Max(0,typedDps-target.ArmorMilli);
            long damage = pair.Invincible ? 0 : rate*attack.DurationTicks*attack.Hits[index].Scale/(TicksPerSecond*attack.Hits.Sum(h => (long)h.Scale));
            damage = PlaceDamage(unit,damage,unit.Definition.FiresProjectile(unit.AttackVariant),building: false);
            if (unit.Definition.FiresProjectile(unit.AttackVariant)) ReleaseProjectile(unit,target,index,damage);
            else ApplyHit(unit,target,unit.AttackVariant,index,damage,unit.AttackStartTick);
        }
    }
}
