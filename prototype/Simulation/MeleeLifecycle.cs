namespace Reforged.Simulation;

public enum CombatEventKind { Start, Hit, Cancel, Release, Burn }
/// <summary>Unit is -1 for a defending building's arrow, which Site then names.</summary>
public sealed record CombatEvent(int Tick,CombatEventKind Kind,int Unit,int Target,int Attack,int Hit,long DamageMilli,
    int StartTick,int X,int Y,int Z,int Site = -1);

public sealed partial class SimUnit
{
    /// <summary>Build-130: killed by a critical weapon's blow; the presentation bursts the body into its source gore.</summary>
    public bool CriticalDeath { get; internal set; }
}

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

    /// <summary>Build-129: a swing whose victim fell (or left the field) plays out before the unit acts on its own again,
    /// instead of being cut off mid-animation. It deals no more hits, and any new order still cancels it at once.</summary>
    private bool FollowThrough(SimUnit unit)
    {
        if (unit.AttackStartTick < 0 || unit.AttackVictim < 0 || Units[unit.AttackVictim].Present) return false;
        if (unit.StrikeElapsed(Tick) < unit.Definition.Attacks[unit.AttackVariant].DurationTicks) return true;
        unit.AttackStartTick = unit.AttackVictim = unit.AttackVariant = -1; unit.NextHit = 0;
        return false;
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

    private void Strike(SimUnit unit,SimUnit target,bool facingReady)
    {
        bool melee = unit.Definition.HasMelee && !UsesMissile(unit,target);
        if (unit.AttackStartTick >= 0 && (unit.AttackVictim != target.Id
            || unit.Definition.IsRanged && unit.Definition.Attacks[unit.AttackVariant].Melee != melee)) CancelStrike(unit);
        if (unit.AttackStartTick >= 0 && unit.StrikeElapsed(Tick) >= unit.Definition.Attacks[unit.AttackVariant].DurationTicks
            && unit.NextHit >= unit.Definition.Attacks[unit.AttackVariant].Hits.Length)
        {
            unit.AttackStartTick = unit.AttackVictim = unit.AttackVariant = -1; unit.NextHit = 0;
        }
        if (unit.AttackStartTick < 0 && unit.Cooldown == 0)
        {
            if (!facingReady) return;
            EndCamouflage(unit);
            int variant = AttackVariants.Choose(unit.Definition,unit.Id,unit.AttackSerial,melee);
            unit.AttackVariant = variant; unit.AttackSerial++; unit.AttackStartTick = Tick;
            unit.AttackVictim = target.Id; unit.NextHit = 0; unit.Cooldown = unit.Definition.Attacks[variant].DurationTicks;
            unit.AttackHasteMicroTicks = 0;
            CombatEvents.Add(new(Tick,CombatEventKind.Start,unit.Id,target.Id,variant,-1,0,Tick,unit.X,unit.Y,unit.Z));
        }
        if (unit.AttackStartTick < 0) return;
        var attack = unit.Definition.Attacks[unit.AttackVariant];
        int elapsed = unit.StrikeElapsed(Tick);
        while (unit.NextHit < attack.Hits.Length && attack.Hits[unit.NextHit].Tick <= elapsed)
        {
            int index = unit.NextHit++;
            if (!target.Present || unit.Target != target.Id || !AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,target)) continue;
            var pair = Rules.Pair(unit.Definition.Id,target.Definition.Id);
            int dps = (melee ? unit.MeleeWeaponDamageRate : unit.MeleeDamageRate)+PlaceDamageAgainst(unit,target);
            long typedDps = (long)dps*Rules.DamageScale(unit.Definition,target.Definition)/1_000_000;
            typedDps = typedDps*unit.AbilityDamageScale(target.Definition.Id)/1_000_000;
            long rate = Math.Max(0,typedDps-target.ArmorMilli);
            long damage = pair.Invincible ? 0 : unit.Definition.HitDamage(attack,index,rate);
            damage = PlaceDamage(unit,damage,unit.Definition.FiresProjectile(unit.AttackVariant),building: false);
            if (unit.Definition.FiresProjectile(unit.AttackVariant)) ReleaseProjectile(unit,target,index,damage);
            else
            {
                ApplyHit(unit,target,unit.AttackVariant,index,damage,unit.AttackStartTick);
                ApplySplash(unit,target,unit.AttackVariant,index,unit.AttackStartTick,missile: false);
            }
        }
    }
}
