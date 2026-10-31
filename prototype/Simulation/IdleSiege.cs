namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private bool NearGuardSite(SimUnit unit,ConstructionSite site)
    {
        var anchor=Navigation.Center(unit.IdleGuardOrigin>=0 ? unit.IdleGuardOrigin : unit.Node);
        return Navigation.HasFootprints(site.Obstacle)
            // Footprint sweeps use strict clearance; include the integer-mm boundary.
            ? Navigation.WithinFootprintReach(site.Obstacle,anchor.X,anchor.Z,IdleAwarenessMm+1)
            : DistanceSquared(anchor.X,anchor.Z,SitePosition(site).X,SitePosition(site).Z)
                <=(long)(IdleAwarenessMm+site.RadiusMm)*(IdleAwarenessMm+site.RadiusMm);
    }

    private bool InIdleGuardLeash(SimUnit unit,int x,int z)
    {
        var anchor=Navigation.Center(unit.IdleGuardOrigin>=0 ? unit.IdleGuardOrigin : unit.Node);
        long leash=IdleLeash(unit);
        return DistanceSquared(anchor.X,anchor.Z,x,z)<=leash*leash;
    }

    private bool SiegeNodeAllowed(SimUnit unit,int node)
    {
        if(!IdleGuardEnabled(unit) || unit.SiteTarget<0) return true;
        var at=Navigation.Center(node);
        return InIdleGuardLeash(unit,at.X,at.Z);
    }

    private void ReleaseIdleSiege(SimUnit unit)
    {
        unit.SiteTarget=-1; unit.SiegePlanTick=-1;
        if(unit.Target>=0) return; // A unit threat may have temporarily interrupted the building attack.
        ReleaseCombat(unit); unit.IdleGuardReturning=true;
    }

    // Runs after normal unit acquisition, so a building never wins over a unit
    // the guard would fight. Keep Idle and its original post rather than issuing
    // an unrestricted explicit AttackStructure order.
    private bool IdleGuardSiege(SimUnit unit)
    {
        if(!IdleGuardEnabled(unit) || unit.Target>=0 || unit.IdleGuardReturning || unit.RingActive || unit.Concealed
            || unit.Definition.DpsMilli<=0 || unit.YieldFor>=0 || unit.ReturningToRest) return false;
        if(unit.SiteTarget<0)
        {
            if((Tick+unit.Id)%(TicksPerSecond/2)!=0) return false;
            var site=ConstructionSites.Values.Where(s=>CanAttackSite(unit.Team,s.Id) && NearGuardSite(unit,s))
                .OrderBy(s=>DistanceSquared(unit.X,unit.Z,SitePosition(s).X,SitePosition(s).Z)).ThenBy(s=>s.Id)
                .FirstOrDefault(s=>EconomyRouteCandidates(unit,SiegeArea(unit,s),n=>{
                    var at=Navigation.Center(n); return InIdleGuardLeash(unit,at.X,at.Z);
                },ignoreUnits:true).Any());
            if(site==null) return false;
            CancelStrike(unit); RebaseCombatGround(unit);
            unit.SiteTarget=site.Id; unit.SiegePlanTick=-1;
            unit.Goal=-1; unit.Path.Clear(); unit.Waypoint=0; unit.FreeMovement=false; unit.CombatWaiting=false;
        }
        if(!ConstructionSites.TryGetValue(unit.SiteTarget,out var target) || !CanAttackSite(unit.Team,target.Id))
        { ReleaseIdleSiege(unit); return false; }
        var step=Besiege(unit,target,keepDestination:true);
        if(step==SiegeStep.Unreachable) ReleaseIdleSiege(unit);
        return step is SiegeStep.Striking or SiegeStep.Positioning;
    }
}
