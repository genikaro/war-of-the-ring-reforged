namespace Reforged.Simulation;

public sealed record SimProjectile(int Id,int Unit,int Target,int Attack,int Hit,long DamageMilli,
    int StartTick,int ReleaseTick,int ImpactTick,int X,int Y,int Z,int TargetX,int TargetY,int TargetZ,int Site = -1,int Source = -1);

public sealed partial class SimWorld
{
    public List<SimProjectile> Projectiles { get; } = [];
    private int _projectileSequence;

    private void ReleaseProjectile(SimUnit unit,SimUnit target,int hit,long damage)
    {
        int y = unit.Y+unit.Definition.BodyHeightMm*2/3,targetY = target.Y+target.Definition.BodyHeightMm/2;
        long squared = DistanceSquared(unit.X,unit.Z,target.X,target.Z)+(long)(y-targetY)*(y-targetY);
        long distance = IntegerMotion.SqrtCeiling(squared);
        int travel = (int)Math.Max(1,(distance*TicksPerSecond+unit.Definition.ProjectileSpeedMmPerSecond-1)/unit.Definition.ProjectileSpeedMmPerSecond);
        Projectiles.Add(new(_projectileSequence++,unit.Id,target.Id,unit.AttackVariant,hit,damage,unit.AttackStartTick,
            Tick,Tick+travel,unit.X,y,unit.Z,target.X,targetY,target.Z));
        CombatEvents.Add(new(Tick,CombatEventKind.Release,unit.Id,target.Id,unit.AttackVariant,hit,0,
            unit.AttackStartTick,unit.X,y,unit.Z));
    }

    private void AdvanceProjectiles()
    {
        foreach (var arrow in Projectiles.Where(p => p.ImpactTick <= Tick).ToArray())
        {
            // Released arrows survive replacement orders and the shooter's death.
            // Target following is a prototype policy; dead targets consume arrows.
            if (arrow.Site >= 0)
            {
                if (ConstructionSites.TryGetValue(arrow.Site,out var site) && site.Team >= 0 && site.Health > 0)
                    DamageSite(Units[arrow.Unit],site,arrow.Attack,arrow.Hit,arrow.DamageMilli,arrow.StartTick);
            }
            else if (Units[arrow.Target].Present)
                ApplyHit(arrow.Unit >= 0 ? Units[arrow.Unit] : null,Units[arrow.Target],arrow.Attack,arrow.Hit,arrow.DamageMilli,arrow.StartTick,arrow.Source);
            Projectiles.Remove(arrow);
        }
    }

    /// <summary>A hit from a unit, or from a defending building (no unit, <paramref name="source"/> the site).</summary>
    private void ApplyHit(SimUnit? unit,SimUnit target,int attack,int hit,long damage,int startTick,int source = -1)
    {
        damage = Math.Min(damage,target.HealthMilli); target.HealthMilli -= damage;
        CombatEvents.Add(new(Tick,CombatEventKind.Hit,unit?.Id ?? -1,target.Id,attack,hit,damage,startTick,target.X,target.Y,target.Z,source));
        if (target.Health <= 0)
        {
            CancelAbility(target);
            if (unit != null && damage > 0 && target.Team != unit.Team) unit.KillCount++;
            CancelStrike(target);
        }
        if (unit == null) RetaliateAgainstSite(target,source);
        else if (unit.Present) Retaliate(target,unit);
    }
}
