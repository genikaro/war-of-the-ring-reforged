namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private void AiGlobalPowers(AiPlayer ai,int bx,int bz)
    {
        if (!ai.Personality.UsesSpecialAbilities || !Economies.TryGetValue(ai.Team,out var bank) || Rules.GlobalPowers.Count == 0) return;
        var powers = Rules.GlobalPowers.Where(p => p.Faction == FactionOf(ai.Team)).ToArray();
        var allies = ActiveUnits.Where(u => u.Team == ai.Team && u.Present && !IsAiWorker(u)).OrderBy(u => u.Id).ToArray();
        var enemies = ActiveUnits.Where(u => u.Present && u.Team >= 0 && u.Team != ai.Team && CanSeeUnit(ai.Team,u)
            && (DistanceSquared(bx,bz,u.X,u.Z) <= (long)AiDefenseRadiusMm*AiDefenseRadiusMm
                || allies.Any(a => SeparationSquared(a,u) <= 20000L*20000))).OrderBy(u => u.Id).ToArray();
        if (enemies.Length == 0) return;
        bool emergency = enemies.Count(u => DistanceSquared(bx,bz,u.X,u.Z) <= 18000L*18000) >= 3;
        // Leave enough for the first hero or a gear purchase, except when the base needs immediate help.
        int reserve = emergency || !ai.Personality.UsesHeroes ? 0 : allies.Any(u => u.Definition.Hero) ? 2 : 4;
        bool Ready(GlobalPowerDefinition p) => CanUseGlobalPower(ai.Team,p.Slot,out _) && bank.Fate-p.FateCost >= reserve;
        bool Near(SimUnit a,SimUnit b,int range) => SeparationSquared(a,b) <= (long)range*range;
        // Build-171: a point the bot cannot target (in its fog, on a building) is skipped rather than spent on a refused command.
        bool Cast(GlobalPowerDefinition p,SimUnit center,int target = -1)
        {
            if (!ValidGlobalTarget(ai.Team,p,center.Node,center.X,center.Z,target)) return false;
            Dispatch(new(Tick,_sequence++,ai.Team,[],center.Node,target,OrderKind.UseGlobalPower,
                AbilitySlot:p.Slot,TargetX:center.X,TargetZ:center.Z));
            return true;
        }

        foreach (var power in powers.OrderBy(p => p.Kind switch {
                     GlobalPowerKind.GiantEnt or GlobalPowerKind.SummonBalrog => 0,GlobalPowerKind.Blind or GlobalPowerKind.MurderOfCrows => 1,
                     GlobalPowerKind.Brambles or GlobalPowerKind.Marshlands => 2,GlobalPowerKind.BlessedWind or GlobalPowerKind.RainOfBlood => 3,_ => 4 }).ThenBy(p => p.Id))
        {
            if (!Ready(power)) continue;
            // Evil (build-135): a Summon Bog on the thickest cluster of enemies, Murder of Crows on enemy missile units, Vile Surge once
            // several friendly units around a hurt hero or soldier are wounded.
            if (power.Kind is GlobalPowerKind.Marshlands or GlobalPowerKind.MurderOfCrows)
            {
                var spot = enemies.Select(e => (Unit:e,Score:enemies.Count(v => Near(e,v,power.RadiusMm)
                        && (power.Kind == GlobalPowerKind.Marshlands || v.Definition.IsRanged && v.CrowsUntil <= Tick))))
                    .Where(e => allies.Any(a => Near(a,e.Unit,20000)) || emergency).OrderByDescending(e => e.Score).ThenBy(e => e.Unit.Id).FirstOrDefault();
                if (spot.Unit != null && spot.Score >= (power.Kind == GlobalPowerKind.Marshlands ? 4 : 3)) { if (Cast(power,spot.Unit)) return; }
                continue;
            }
            if (power.Kind == GlobalPowerKind.RainOfBlood)
            {
                var spot = allies.Select(a => (Unit:a,Score:allies.Count(v => Near(a,v,power.RadiusMm) && v.HealthMilli*100 < (long)v.MaxHealth*1000*60)))
                    .Where(a => enemies.Any(e => Near(a.Unit,e,25000))).OrderByDescending(a => a.Score).ThenBy(a => a.Unit.Id).FirstOrDefault();
                if (spot.Unit != null && spot.Score >= 4) { if (Cast(power,spot.Unit)) return; }
                continue;
            }
            if (power.Kind == GlobalPowerKind.Obelisk)
            {
                // Build-164: raise the Obelisk among the most friendly fighters already trading blows.
                var engaged = allies.Where(a => enemies.Any(e => Near(a,e,8000))).ToArray();
                var spot = engaged.Select(a => (Unit:a,Score:engaged.Count(v => Near(a,v,power.RadiusMm))))
                    .OrderByDescending(a => a.Score).ThenBy(a => a.Unit.Id).FirstOrDefault();
                if (spot.Unit != null && spot.Score >= 5) { if (Cast(power,spot.Unit)) return; }
                continue;
            }
            if (power.Kind is GlobalPowerKind.GiantEnt or GlobalPowerKind.SummonBalrog)
            {
                if (enemies.Length < 5 && !(ai.Party.Count >= 4 && CanAttackSite(ai.Team,ai.PartyTarget))) continue;
                var anchor = allies.Where(a => enemies.Any(e => Near(a,e,20000))).OrderBy(a => a.Id).FirstOrDefault();
                if (anchor == null) continue;
                var at = Navigation.Center(anchor.Node);
                int reach = 6000/Navigation.CellSize+1, ax = anchor.Node%Navigation.Width, az = anchor.Node/Navigation.Width;
                var cells = from row in Enumerable.Range(Math.Max(0,az-reach),Math.Min(Navigation.Height-1,az+reach)-Math.Max(0,az-reach)+1)
                            from col in Enumerable.Range(Math.Max(0,ax-reach),Math.Min(Navigation.Width-1,ax+reach)-Math.Max(0,ax-reach)+1)
                            let cell = row*Navigation.Width+col
                            let pos = Navigation.Center(cell)
                            where DistanceSquared(at.X,at.Z,pos.X,pos.Z) <= 6000L*6000
                            select cell;
                foreach (int node in cells.OrderBy(n => { var c = Navigation.Center(n); return DistanceSquared(c.X,c.Z,at.X,at.Z); }).ThenBy(n => n))
                {
                    var c = Navigation.Center(node);
                    if (!ValidGlobalTarget(ai.Team,power,node,c.X,c.Z)) continue;
                    int before = Units.Count;
                    Dispatch(new(Tick,_sequence++,ai.Team,[],node,Kind:OrderKind.UseGlobalPower,AbilitySlot:power.Slot));
                    if (Units.Count > before)
                    {
                        var target = enemies.OrderBy(e => DistanceSquared(c.X,c.Z,e.X,e.Z)).ThenBy(e => e.Id).First();
                        AiOrder(ai,[before],OrderKind.AttackMove,target.Node);
                    }
                    return;
                }
            }
            if (power.Kind is GlobalPowerKind.Blind or GlobalPowerKind.Brambles)
            {
                var center = enemies.Select(e => (Unit:e,Score:enemies.Count(v => Near(e,v,power.RadiusMm)
                        && (power.Kind == GlobalPowerKind.Blind ? v.BlindUntil <= Tick && (v.Immunities & StatusProtection.Blind) == 0
                            && v.Definition.DpsMilli > 0 : v.Moving || v.Goal >= 0))))
                    .Where(e => allies.Any(a => Near(a,e.Unit,20000)) || emergency)
                    .OrderByDescending(e => e.Score).ThenBy(e => e.Unit.Id).FirstOrDefault();
                if (center.Unit != null && center.Score >= 3) { if (Cast(power,center.Unit)) return; }
            }
            if (power.Kind == GlobalPowerKind.BlessedWind)
            {
                var center = allies.Where(a => a.Definition.IsRanged).Select(a => (Unit:a,Score:allies.Count(v => v.Definition.IsRanged
                    && v.BlessedWindUntil <= Tick && Near(a,v,power.RadiusMm))))
                    .Where(a => enemies.Any(e => Near(a.Unit,e,20000))).OrderByDescending(a => a.Score).ThenBy(a => a.Unit.Id).FirstOrDefault();
                if (center.Unit != null && center.Score >= 3) { if (Cast(power,center.Unit)) return; }
            }
            if (power.Kind == GlobalPowerKind.HeroicLegacy)
            {
                // Invest in an experienced healthy hero committed to a battle, never a retreating one.
                var hero = allies.Where(h => h.Definition.Hero && h.HeroicLegacy == null && h.HeroLevel >= 3
                        && h.HealthMilli*100 >= (long)h.MaxHealth*1000*75 && !ai.RecoveringHeroes.Contains(h.Id)
                        && enemies.Any(e => Near(h,e,18000)))
                    .OrderByDescending(h => h.HeroLevel).ThenBy(h => h.Id).FirstOrDefault();
                if (hero != null) { if (Cast(power,hero,hero.Id)) return; }
            }
        }
    }
}
