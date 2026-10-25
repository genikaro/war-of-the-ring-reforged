namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private static bool Answering(SimUnit unit,int enemy) => unit.Target == enemy
        || unit.Order == OrderKind.Attack && unit.OrderTarget == enemy
        || unit.HeldOrder is { Kind: OrderKind.Attack } held && held.Target == enemy;

    /// <summary>Keep a small, stable screen on each threat. The rest retain their building or march orders.</summary>
    private void AiScreenThreats(AiPlayer ai,SimUnit[] soldiers,SimUnit[] enemies,SortedDictionary<int,AiSighting> seen,bool urgent = false)
    {
        var available = soldiers.Select(u => u.Id).ToHashSet();
        var threats = enemies.Where(e => seen.ContainsKey(e.Id)).ToArray();
        for (int i = 0; i < threats.Length; i++)
        {
            var enemy = threats[i];
            int outside = Units.Count(u => u.Present && u.Team == ai.Team && !available.Contains(u.Id) && Answering(u,enemy.Id));
            int fair = (available.Count+threats.Length-i-1)/(threats.Length-i);
            int quota = Math.Min(fair,Math.Max(0,ThreatResponseLimit(enemy)-outside));
            var response = soldiers.Where(u => available.Contains(u.Id))
                .OrderBy(u => Answering(u,enemy.Id) ? 0 : 1)
                .ThenBy(u => enemy.Target == u.Id ? 0 : 1)
                .ThenBy(u => DistanceSquared(u.X,u.Z,enemy.X,enemy.Z)).ThenBy(u => u.Id).Take(quota).ToArray();
            foreach (var unit in response) available.Remove(unit.Id);
            // Urgency overrides patient swings only for a changed order, never restarts the same attack each think.
            AiOrder(ai,response.Where(u => !AiAlreadyOrdered(ai,u.Id,OrderKind.Attack,enemy.Node,enemy.Id)).Select(u => u.Id),
                OrderKind.Attack,enemy.Node,enemy.Id,urgent);
        }
    }

    private bool AiAttacksAssets(AiPlayer ai,SimUnit enemy,(int X,int Z)[] buildings)
    {
        if (enemy.Attacking || enemy.StrikeActive)
        {
            if (ConstructionSites.TryGetValue(SiegeTarget(enemy),out var site) && site.Team == ai.Team && site.Health > 0) return true;
            if (AttackingUnit(enemy,ai.Team) && IsAiWorker(Units[enemy.Target]) && buildings.Any(b =>
                DistanceSquared(b.X,b.Z,Units[enemy.Target].X,Units[enemy.Target].Z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm)) return true;
        }
        return RecentDamageTo(enemy,ai.Team) && enemy.LastHostileDamageAsset && buildings.Any(b =>
            DistanceSquared(b.X,b.Z,enemy.LastHostileDamageX,enemy.LastHostileDamageZ) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm);
    }

    /// <summary>A real attack on assets recalls enough nearby field soldiers, including marching parties and raiders.
    /// A visible worker alone is not an alarm. Keep the reserve through short gaps between enemy swings.</summary>
    private bool AiRecallDefenders(AiPlayer ai,(int X,int Z)[] buildings,ref SimUnit[] home)
    {
        ai.HomeDefenders.RemoveWhere(id => !Units[id].Present);
        var attackers = Units.Where(u => u.Team != ai.Team && u.Present && !u.RingActive && AiAttacksAssets(ai,u,buildings)).ToArray();
        bool holding = Units.Any(e => e.Team != ai.Team && e.Present && AttackingUnit(e,ai.Team) && ai.HomeDefenders.Contains(e.Target));
        if (attackers.Length > 0 || holding) ai.HomeDefenseUntil = Tick+RecentThreatTicks;
        if (Tick > ai.HomeDefenseUntil)
        { ai.HomeDefenders.Clear(); return false; }
        if (attackers.Length == 0) return true;
        long Gap(SimUnit unit) => attackers.Min(e => DistanceSquared(unit.X,unit.Z,e.X,e.Z));
        int required = attackers.Sum(ThreatResponseLimit);
        // An almost fallen Citadel needs the available army. A new foundation starts low and is not an emergency.
        bool critical = attackers.Any(e => ConstructionSites.TryGetValue(SiegeTarget(e),out var site) && site.Team == ai.Team
            && site.Complete && site.Building != null && StartStructures.Any(s => s.Team == ai.Team && Satisfies(site.Building,s.Building))
            && site.Health > 0 && site.Health*100L <= SiteRules(site).MaxHealth*40L);
        int local = home.Count(u => ai.HomeDefenders.Contains(u.Id) || Gap(u) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm);
        var field = ai.Party.Concat(ai.Raid).Concat(ai.PlaceSquad).Distinct().Select(id => Units[id])
            .Where(u => u.Present && !AiTrapped(u) && !ai.Recovering.Contains(u.Id)).OrderBy(Gap).ThenBy(u => u.Id);
        var recalled = (critical ? field : field.Take(Math.Max(0,required-local))).ToArray();
        foreach (var unit in recalled)
        {
            ai.Party.Remove(unit.Id); ai.Raid.Remove(unit.Id); ai.PlaceSquad.Remove(unit.Id);
            ai.HomeDefenders.Add(unit.Id);
        }
        // Cancel the outbound order now, even if it was in the middle of a structure attack.
        AiOrder(ai,recalled.Select(u => u.Id),OrderKind.Stop,urgent: true);
        home = [.. home.Concat(recalled).DistinctBy(u => u.Id).OrderBy(u => u.Id)];
        // Surplus emergency recalls head back too; only the local screen chases individual attackers.
        var (bx,bz) = AiBase(ai);
        AiOrder(ai,recalled.Select(u => u.Id),OrderKind.AttackMove,Navigation.Cell(bx,bz));
        return true;
    }
}
