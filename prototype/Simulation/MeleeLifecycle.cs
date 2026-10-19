namespace Reforged.Simulation;

public enum CombatEventKind { Start, Hit, Cancel }
public sealed record CombatEvent(int Tick,CombatEventKind Kind,int Unit,int Target,int Attack,int Hit,long DamageMilli,
    int StartTick,int X,int Y,int Z);

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
        int limit = target.Definition.MaxAttackers;
        if (limit == 0) return true;
        return Units.Where(u => u.Health > 0 && u.Target == target.Id
            && (HoldsPosition(u) || RoomToFight(u))
            && MeleeReach(u.Node,u.X,u.Y,u.Z,target,u.Definition.ReachMm)).OrderBy(u => u.Id).Take(limit).Any(u => u.Id == unit.Id);
    }

    private void Strike(SimUnit unit,SimUnit target)
    {
        if (unit.AttackStartTick >= 0 && unit.AttackVictim != target.Id) CancelStrike(unit);
        if (unit.AttackStartTick >= 0 && Tick-unit.AttackStartTick >= unit.Definition.Attacks[unit.AttackVariant].DurationTicks)
        {
            unit.AttackStartTick = unit.AttackVictim = unit.AttackVariant = -1; unit.NextHit = 0;
        }
        if (unit.AttackStartTick < 0 && unit.Cooldown == 0)
        {
            // Per-unit stateless weighted choice; no renderer or global RNG can
            // affect clip selection, damage timing or another unit's choices.
            long total = unit.Definition.Attacks.Sum(a => (long)a.Weight);
            long pick = ((long)unit.Id*73856093+unit.AttackSerial*19349663L)%total;
            int variant = 0;
            while (pick >= unit.Definition.Attacks[variant].Weight) pick -= unit.Definition.Attacks[variant++].Weight;
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
            if (target.Health <= 0 || unit.Target != target.Id || !MeleeReach(unit.Node,unit.X,unit.Y,unit.Z,target,unit.Definition.ReachMm)) continue;
            var pair = Rules.Pair(unit.Definition.Id,target.Definition.Id);
            long rate = Math.Max(0,(long)unit.Definition.DpsMilli*pair.Scale/1_000_000-target.Definition.ArmorMilli);
            long damage = pair.Invincible ? 0 : rate*attack.DurationTicks*attack.Hits[index].Scale/(TicksPerSecond*attack.Hits.Sum(h => (long)h.Scale));
            damage = Math.Min(damage,target.HealthMilli); target.HealthMilli -= damage;
            CombatEvents.Add(new(Tick,CombatEventKind.Hit,unit.Id,target.Id,unit.AttackVariant,index,damage,
                unit.AttackStartTick,target.X,target.Y,target.Z));
            if (target.Health <= 0) CancelStrike(target);
            Retaliate(target,unit);
        }
    }
}
