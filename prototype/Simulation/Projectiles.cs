namespace Reforged.Simulation;

public sealed record SimProjectile(int Id,int Unit,int Target,int Attack,int Hit,long DamageMilli,
    int StartTick,int ReleaseTick,int ImpactTick,int X,int Y,int Z,int TargetX,int TargetY,int TargetZ);

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
            var target = Units[arrow.Target];
            // Released arrows survive replacement orders and the shooter's death.
            // Target following is a prototype policy; dead targets consume arrows.
            if (target.Health > 0)
                ApplyHit(Units[arrow.Unit],target,arrow.Attack,arrow.Hit,arrow.DamageMilli,arrow.StartTick);
            Projectiles.Remove(arrow);
        }
    }

    private void ApplyHit(SimUnit unit,SimUnit target,int attack,int hit,long damage,int startTick)
    {
        damage = Math.Min(damage,target.HealthMilli); target.HealthMilli -= damage;
        CombatEvents.Add(new(Tick,CombatEventKind.Hit,unit.Id,target.Id,attack,hit,damage,startTick,target.X,target.Y,target.Z));
        if (target.Health <= 0)
        {
            if (damage > 0 && target.Team != unit.Team) unit.KillCount++;
            CancelStrike(target);
        }
        if (unit.Health > 0) Retaliate(target,unit);
    }
}
