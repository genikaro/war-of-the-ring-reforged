namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private const int RecentThreatTicks=3*TicksPerSecond,SiegeSupportMm=10000,SiegeContactMm=6000;

    private void RecordHostileDamage(SimUnit attacker,int team,int x,int z,bool asset)
    {
        if (attacker.Team==team || team<0) return;
        attacker.LastHostileDamageTick=Tick; attacker.LastHostileDamageTeam=team;
        attacker.LastHostileDamageX=x; attacker.LastHostileDamageZ=z; attacker.LastHostileDamageAsset=asset;
    }

    private bool RecentDamageTo(SimUnit enemy,int team) => enemy.LastHostileDamageTeam==team
        && enemy.LastHostileDamageTick>=0 && Tick-enemy.LastHostileDamageTick<=RecentThreatTicks;

    private bool AttackingUnit(SimUnit enemy,int team) => enemy.Target>=0 && Units[enemy.Target].Present
        && Units[enemy.Target].Team==team && (enemy.Attacking || enemy.StrikeActive);

    private bool ThreatensNearbyAllies(SimUnit unit,SimUnit enemy)
    {
        if (AttackingUnit(enemy,unit.Team) && Units[enemy.Target] is var victim
            && DistanceSquared(unit.X,unit.Z,victim.X,victim.Z)<=(long)SiegeSupportMm*SiegeSupportMm) return true;
        return RecentDamageTo(enemy,unit.Team)
            && DistanceSquared(unit.X,unit.Z,enemy.LastHostileDamageX,enemy.LastHostileDamageZ)<=(long)SiegeSupportMm*SiegeSupportMm;
    }

    private int ThreatResponseLimit(SimUnit enemy) => IsAiWorker(enemy) || enemy.Definition.DpsMilli<=0 ? 2 : AiAttackersPerEnemy;

    private bool MayInterruptSiege(SimUnit unit,SimUnit enemy)
    {
        // The bot's waiting ranks retain the party objective even if no building attack spot is available.
        int site = unit.Order == OrderKind.AttackMove && unit.SiteTarget >= 0 ? unit.SiteTarget
            : unit.Order is OrderKind.Idle or OrderKind.AttackMove ? unit.AiSiegeObjective : -1;
        if (!CanAttackSite(unit.Team,site)) return true;
        bool danger=ThreatensNearbyAllies(unit,enemy),contact=AttackReach(unit,unit.Node,unit.X,unit.Y,unit.Z,enemy);
        if (!danger && (IsAiWorker(enemy) || enemy.Definition.DpsMilli<=0) && !contact) return false;
        if (!danger && !contact && SeparationSquared(unit,enemy)>(long)SiegeContactMm*SiegeContactMm) return false;
        // An attacked soldier can always answer; otherwise use a small local screen rather than the whole army.
        if (AttackingUnit(enemy,unit.Team) && enemy.Target==unit.Id) return true;
        return unit.Target==enemy.Id || Grid.Targeters(enemy.Id).Count(u=>u.Team==unit.Team && u.Present)<ThreatResponseLimit(enemy);
    }
}
