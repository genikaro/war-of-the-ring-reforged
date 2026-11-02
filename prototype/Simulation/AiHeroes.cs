namespace Reforged.Simulation;

/// <summary>Hero decisions use the player's training, purchase and ability commands. Heroes support the main army;
/// they do not fill regular assault slots or get assigned to raids, expansion guards or place scouts. Owner request
/// (build-150): heroes kept so safe they never levelled, so their courage follows the bot's Aggression (A, 0-100). A hero
/// runs home below 40 - 0.3 A % of its health (40% for the most careful bot, 10%, the very last moment, for the boldest)
/// and comes back from 80 - 0.35 A %. It takes on enemies it sees within 20 + 0.3 A m of itself while the odds at the
/// enemy, as power (AiPower) within 15 m, are no worse than (50 + 2 A)% can take: a careful hero wants two to one, a
/// balanced one fights at two to three and the boldest at two to five, enemy heroes first. It breaks off a fight whose
/// odds turned worse than that, toward its army or home. With no assault out, a hero of Aggression 40 or more goes along
/// with a place-of-power squad or a raid instead of waiting at home.</summary>
public sealed partial class AiPlayer
{
    /// <summary>Heroes walking out of a fight whose odds turned (build-150), and until when they walk on.</summary>
    public SortedDictionary<int,int> HeroRetreats { get; } = [];
}

public sealed partial class SimWorld
{
    public const int AiHeroOddsMm = 15000,AiHeroBreakOffTicks = 10*TicksPerSecond;
    public static int AiHeroRetreatPercent(AiPlayer ai) => 40-3*ai.Profile.Style.Aggression/10;
    public static int AiHeroReturnPercent(AiPlayer ai) => 80-35*ai.Profile.Style.Aggression/100;
    public static int AiHeroCourage(AiPlayer ai) => 50+2*ai.Profile.Style.Aggression;
    public static int AiHeroHuntMm(AiPlayer ai) => 20000+300*ai.Profile.Style.Aggression;

    /// <summary>A hero of the team stands or is in training.</summary>
    private bool AiHasHero(AiPlayer ai) => ActiveUnits.Any(u => u.Team == ai.Team && u.Health > 0 && u.Definition.Hero)
        || Producers.Values.Any(p => p.Team == ai.Team && p.Queue.Any(q => Rules.Units.GetValueOrDefault(q.Unit)?.Hero == true));

    /// <summary>Build-136: the food and ore a bot with a full party and no hero yet keeps from soldiers and workers for the hero that costs
    /// them instead of Fate (Frodo, Gollum), once nothing but the money is missing. Evil's 35-food soldiers drained the bank before it ever
    /// held the 150 a hero asks, and its armies went to war without one.</summary>
    private (int Food,int Ore) AiFirstHeroSavings(AiPlayer ai,int soldiers)
    {
        if (!ai.Personality.UsesHeroes || soldiers < AiAssaultParty.MinSize || AiHasHero(ai)) return (0,0);
        (int Food,int Ore)? cheapest = null;
        foreach (var producer in Producers.Values.Where(p => p.Team == ai.Team))
            for (int slot = 0; slot < producer.Units.Length; slot++)
            {
                if (Rules.Units.GetValueOrDefault(producer.Units[slot])?.Hero != true || !ProductionRules.TryGetValue(producer.Units[slot],out var rules)
                    || rules.FateCost > 0 || rules.FoodCost+rules.OreCost == 0) continue;
                bool ready = CanTrain(ai.Team,producer.Id,slot,out string reason);
                if (!ready && reason != "Insufficient resources") continue;
                var cost = TrainingCost(ai.Team,rules);
                if (cheapest == null || cost.FoodCost+cost.OreCost < cheapest.Value.Food+cheapest.Value.Ore) cheapest = (cost.FoodCost,cost.OreCost);
            }
        return cheapest ?? (0,0);
    }

    private int AiHeroRecruit(AiPlayer ai,ProductionBuilding producer,int reserveFood,int reserveOre)
    {
        var bank = Economies[ai.Team];
        // The first hero is bought ahead of the plan's savings, like the saving for it above.
        if (!AiHasHero(ai)) (reserveFood,reserveOre) = (0,0);
        return Enumerable.Range(0,producer.Units.Length)
            .Where(i => Rules.Units.GetValueOrDefault(producer.Units[i])?.Hero == true
                && ProductionRules.TryGetValue(producer.Units[i],out var rules) && TrainingCost(ai.Team,rules) is var cost
                // Build-136: the plan's reserve guards food and ore a hero would spend; one paid in Fate alone (the Nazgul King, Gimli)
                // takes none of it, and waiting behind a building the bot could not afford left Evil bots without a hero for minutes.
                && bank.FoodMilli >= (cost.FoodCost+(cost.FoodCost > 0 ? reserveFood : 0))*1000L
                && bank.OreMilli >= (cost.OreCost+(cost.OreCost > 0 ? reserveOre : 0))*1000L
                && CanTrain(ai.Team,producer.Id,i,out _))
            // Restore experienced heroes first, then prefer the strongest unlocked Fate tier.
            // Equal tiers use a per-match preference instead of always taking the first button (Gimli).
            .OrderByDescending(i => Units.Where(u => u.Team == ai.Team && u.Definition.Name == producer.Units[i])
                .Select(u => u.HeroLevel).DefaultIfEmpty(0).Max())
            .ThenByDescending(i => ProductionRules[producer.Units[i]].FateCost)
            .ThenBy(i => AiStrategyRoll(ai,"hero:"+producer.Units[i])).ThenBy(i => i).DefaultIfEmpty(-1).First();
    }

    /// <summary>Build-136: an upgrade pauses the building's training queue, so the next Citadel or Fortress level waits while a hero is in
    /// the queue or one paid in Fate alone can be trained now by a bot with a full party to guard it (AiTrain's own condition). Without
    /// this the bot began the next level in the think that finished the last, and the hero it queued stood out the whole chain.</summary>
    private bool AiHeroBeforeUpgrade(AiPlayer ai,ConstructionSite site)
    {
        if (!ai.Personality.UsesHeroes || !Producers.TryGetValue(site.Id,out var producer)) return false;
        if (producer.Queue.Any(q => Rules.Units.GetValueOrDefault(q.Unit)?.Hero == true)) return true;
        if (ActiveUnits.Count(u => u.Team == ai.Team && u.Health > 0 && IsAiSoldier(u)) < AiAssaultParty.MinSize) return false;
        return AiHeroRecruit(ai,producer,0,0) is int slot && slot >= 0 && ProductionRules[producer.Units[slot]].FateCost > 0
            && ProductionRules[producer.Units[slot]].FoodCost == 0 && ProductionRules[producer.Units[slot]].OreCost == 0;
    }

    /// <summary>Build-136: the bank already holds the Fate of a hero that only <paramref name="building"/> (the next Citadel or Fortress
    /// level) unlocks, so the bot saves for that upgrade even before its party is full.</summary>
    private bool AiFateHeroWaitsFor(AiPlayer ai,string building) => ai.Personality.UsesHeroes && Economies.TryGetValue(ai.Team,out var bank)
        && Producers.Values.Where(p => p.Team == ai.Team).SelectMany(p => p.Units).Distinct().Any(unit => Rules.Units.GetValueOrDefault(unit)?.Hero == true
            && ProductionRules.TryGetValue(unit,out var rules) && rules.FateCost > 0 && bank.Fate >= rules.FateCost
            && MissingPrerequisite(ai.Team,rules.Prerequisites ?? []) == building);

    private void AiHeroes(AiPlayer ai,SimUnit[] heroes,int bx,int bz,bool strategic)
    {
        if (!ai.Personality.UsesHeroes) return;
        bool stranded = heroes.Length > 0 && AiFoodStranded(ai);
        ai.RecoveringHeroes.RemoveWhere(id => !Units[id].Present || Units[id].HealthMilli*100 >= (long)Units[id].MaxHealth*1000*AiHeroReturnPercent(ai));
        foreach (var hero in heroes)
        {
            if (hero.HealthMilli*100 <= (long)hero.MaxHealth*1000*AiHeroRetreatPercent(ai)) ai.RecoveringHeroes.Add(hero.Id);
            if (strategic && ai.Personality.UsesSpecialAbilities)
                foreach (var ability in (hero.Definition.Abilities ?? []).OrderBy(a => a.Slot))
                    if (ability.Kind is not (AbilityKind.Sneak or AbilityKind.SurpriseAttack or AbilityKind.ShadowWalk) && CanPurchaseAbility(ai.Team,hero.Id,ability.Slot,out _))
                        Dispatch(new(Tick,_sequence++,ai.Team,[hero.Id],-1,Kind:OrderKind.PurchaseAbility,AbilitySlot:ability.Slot));
            // An approach to a valid cast/capture is also a commitment. Do not replace it every think.
            if (hero.Order == OrderKind.UseAbility || hero.Casting || hero.NatureLocked(Tick)) continue;
            bool recovering = ai.RecoveringHeroes.Contains(hero.Id);
            if (ai.Personality.UsesSpecialAbilities && AiHeroAbility(ai,hero,recovering)) continue;
            if (recovering)
            {
                int cell = AiHeroHomeCell(hero,bx,bz);
                if (cell >= 0 && (hero.Order != OrderKind.Move || hero.OrderCell != cell))
                    AiOrder(ai,[hero.Id],OrderKind.Move,cell,urgent:true);
                continue;
            }
            if (ai.Intruder >= 0 && Units[ai.Intruder] is { Present:true } intruder
                && CanSeeUnit(ai.Team,intruder) && Nav(hero).IsConnected(hero.Node,intruder.Node))
            {
                AiOrder(ai,[hero.Id],OrderKind.Attack,intruder.Node,intruder.Id);
                continue;
            }
            // The army out, or with none out a bold hero goes with a place squad or a raid (build-150).
            var escorted = ai.Party.Count > 0 || ai.Profile.Style.Aggression < 40 ? ai.Party : ai.PlaceSquad.Count > 0 ? ai.PlaceSquad : ai.Raid;
            var party = escorted.Select(id => Units[id]).Where(u => u.Present && Nav(hero).IsConnected(hero.Node,u.Node)).ToArray();
            if (AiHeroFight(ai,hero,party,bx,bz)) continue;
            if (party.Length > 0)
            {
                int x = (int)party.Average(u => (long)u.X),z = (int)party.Average(u => (long)u.Z);
                var anchor = party.OrderBy(u => DistanceSquared(u.X,u.Z,x,z)).ThenBy(u => u.Id).First();
                if (ConstructionSites.TryGetValue(ai.PartyTarget,out var target) && target.Health > 0
                    && party.Any(u => SiegeTarget(u) == target.Id) && DistanceSquared(hero.X,hero.Z,anchor.X,anchor.Z) <= 12000L*12000
                    && AiReaches(hero,target))
                    AiOrder(ai,[hero.Id],OrderKind.AttackStructure,target:target.Id);
                else AiHeroGuard(ai,hero,anchor.Node,anchor.Id);
            }
            else if ((!ActiveUnits.Any(u => u.Team == ai.Team && u.Health > 0 && (IsAiWorker(u) || IsAiSoldier(u)))
                    || stranded && !ActiveUnits.Any(u => u.Team == ai.Team && u.Present && IsAiSoldier(u)))
                && AiKnownSites(ai)
                    .OrderBy(s => stranded && s.Building == null && Resources.TryGetValue(s.Resource,out var food)
                        && food.Kind == ResourceKind.Food && CanGather(s.Team,food.Id) ? 0 : 1)
                    .ThenBy(s => DistanceSquared(hero.X,hero.Z,s.X,s.Z)).ThenBy(s => s.Id).FirstOrDefault(s => AiReaches(hero,s)) is { } lastTarget)
                AiOrder(ai,[hero.Id],OrderKind.AttackStructure,target:lastTarget.Id);
            else AiHeroGuard(ai,hero,AiHeroHomeCell(hero,bx,bz));
        }
    }

    private int AiHeroHomeCell(SimUnit hero,int bx,int bz) => Nav(hero).Nearest(Navigation.Cell(bx,bz),connectedTo:hero.Node);

    /// <summary>The hero's own fights (build-150): it stays in one while the odds hold and breaks off when they turn,
    /// toward the units it goes with or home; otherwise it attacks the best enemy in its hunting range whose odds it can
    /// take, enemy heroes first, then soldiers before workers, the nearest first. True when it fights or breaks off.</summary>
    private bool AiHeroFight(AiPlayer ai,SimUnit hero,SimUnit[] party,int bx,int bz)
    {
        bool Holds(int x,int z,bool join)
        {
            var (ours,theirs) = AiLocalBalance(ai,x,z,AiHeroOddsMm);
            if (join && DistanceSquared(hero.X,hero.Z,x,z) > (long)AiHeroOddsMm*AiHeroOddsMm) ours += AiPower(hero);
            return (long)ours*AiHeroCourage(ai) >= (long)theirs*100;
        }
        if (hero.Target >= 0 && Units[hero.Target] is { Present: true } foe && foe.Team != ai.Team)
        {
            if (Holds(foe.X,foe.Z,false)) return true;
            var anchor = party.OrderBy(u => DistanceSquared(u.X,u.Z,hero.X,hero.Z)).ThenBy(u => u.Id).FirstOrDefault();
            int cell = anchor != null && DistanceSquared(anchor.X,anchor.Z,foe.X,foe.Z) > (long)AiHeroOddsMm*AiHeroOddsMm ? anchor.Node : AiHeroHomeCell(hero,bx,bz);
            if (cell >= 0) { AiOrder(ai,[hero.Id],OrderKind.Move,cell,urgent: true); ai.HeroRetreats[hero.Id] = Tick+AiHeroBreakOffTicks; }
            return true;
        }
        // A hero on its way out of a lost fight walks on for up to 10 s; it fights again once it stands.
        if (ai.HeroRetreats.TryGetValue(hero.Id,out int until) && until > Tick && hero.Order == OrderKind.Move && hero.Goal >= 0) return true;
        ai.HeroRetreats.Remove(hero.Id);
        int hunt = AiHeroHuntMm(ai);
        var foes = NearUnits(hero.X,hero.Z,hunt).Where(e => e.Present && e.Team >= 0 && e.Team != ai.Team && CanSeeUnit(ai.Team,e)
                && !ai.Unreachable.ContainsKey(e.Id) && (e.Definition.DpsMilli > 0 || IsAiWorker(e))
                && SeparationSquared(hero,e) <= (long)hunt*hunt && !Rules.Pair(hero.Definition.Id,e.Definition.Id).Invincible
                && Nav(hero).IsConnected(hero.Node,e.Node))
            .OrderByDescending(e => e.Definition.Hero).ThenBy(e => IsAiWorker(e) ? 1 : 0).ThenBy(e => SeparationSquared(hero,e)).ThenBy(e => e.Id);
        foreach (var enemy in foes)
            if (Holds(enemy.X,enemy.Z,true)) { AiOrder(ai,[hero.Id],OrderKind.Attack,enemy.Node,enemy.Id); return true; }
        return false;
    }

    private void AiHeroGuard(AiPlayer ai,SimUnit hero,int cell,int target = -1)
    {
        if (cell < 0 || hero.Order == OrderKind.Guard && hero.OrderTarget == target && (target >= 0 || hero.OrderCell == cell)) return;
        Dispatch(new(Tick,_sequence++,ai.Team,[hero.Id],cell,target,OrderKind.Guard,Patient:true));
    }

    private bool AiHeroAbility(AiPlayer ai,SimUnit hero,bool recovering)
    {
        // Only visible, connected contacts can inform a cast. Bound the scan to the largest ability on this hero.
        var abilities = hero.EnabledAbilities.Where(a => !a.Passive && a.Kind != AbilityKind.Unsimulated && hero.AbilityReadyAt[a.Slot] <= Tick)
            .OrderBy(a => a.Kind == AbilityKind.Heal ? 0 : 1).ThenBy(a => a.Slot).ToArray();
        int scan = Math.Max(AiEngageRadiusMm,abilities.Select(a => Math.Max(a.RangeMm,a.AreaRadiusMm)).DefaultIfEmpty(0).Max());
        var nearby = NearUnits(hero.X,hero.Z,scan).Where(u => u.Present && u.Team >= 0 && Nav(hero).IsConnected(hero.Node,u.Node)).OrderBy(u => u.Id).ToArray();
        var enemies = nearby.Where(u => u.Team != ai.Team && CanSeeUnit(ai.Team,u)).ToArray();
        var allies = nearby.Where(u => u.Team == ai.Team).ToArray();
        bool InRange(SimUnit unit,int range) => SeparationSquared(hero,unit) <= (long)range*range;
        void Cast(AbilityDefinition a,int target = -1,int cell = -1,int? x = null,int? z = null)
            => Dispatch(new(Tick,_sequence++,ai.Team,[hero.Id],cell,target,OrderKind.UseAbility,AbilitySlot:a.Slot,TargetX:x,TargetZ:z));
        foreach (var ability in abilities)
        {
            if (ability.Kind == AbilityKind.Ring)
            {
                bool hide = recovering && hero.HealthMilli*10 > (long)hero.MaxHealth*1000
                    && enemies.Any(e => InRange(e,AiEngageRadiusMm));
                if (hero.RingActive != hide) { Cast(ability); return false; } // The toggle leaves movement available this think.
                continue;
            }
            if (ability.Kind == AbilityKind.Heal)
            {
                var wounded = allies.Where(u => ValidAbilityTarget(hero,ability,u) && InRange(u,ability.RangeMm)
                    && ((long)u.MaxHealth*1000-u.HealthMilli >= ability.HealMilli/2 || u.HealthMilli*100 < (long)u.MaxHealth*1000*40))
                    .OrderBy(u => u.HealthMilli*1_000_000/Math.Max(1,(long)u.MaxHealth*1000)).ThenBy(u => u.Id).FirstOrDefault();
                if (wounded != null) { Cast(ability,wounded.Id); return true; }
            }
            if (recovering || hero.RingActive) continue;
            if (ability.Kind is AbilityKind.Sunder or AbilityKind.BlindingLight)
            {
                var victims = enemies.Where(e => InRange(e,ability.AreaRadiusMm) && (e.Immunities & StatusProtection.Stun) == 0 && e.Dazed == null).ToArray();
                if (victims.Length >= 2 || victims.Any(e => e.Definition.Hero || e.Target == hero.Id)) { Cast(ability,hero.Id); return true; }
            }
            if (ability.Kind == AbilityKind.Ward && allies.Any(u => InRange(u,ability.RangeMm) && u.MissileWardUntil <= Tick)
                && enemies.Any(e => e.Definition.IsRanged && InRange(e,AiEngageRadiusMm)))
            { Cast(ability,hero.Id); return true; }
            if (ability.Kind == AbilityKind.ElvenSpeed && hero.ElvenSpeed == null && enemies.Any(e => InRange(e,AiEngageRadiusMm))
                && allies.Count(u => InRange(u,ability.AreaRadiusMm) && u.ElvenSpeed == null && !IsAiWorker(u)) >= 2)
            { Cast(ability); return true; }
            if (ability.Kind == AbilityKind.Trueshot)
            {
                var victim = enemies.Where(e => InRange(e,ability.RangeMm) && !Rules.Pair(hero.Definition.Id,e.Definition.Id).Invincible)
                    .OrderByDescending(e => e.Definition.Hero).ThenByDescending(e => e.Definition.DpsMilli).ThenBy(e => e.HealthMilli).ThenBy(e => e.Id).FirstOrDefault();
                if (victim != null) { Cast(ability,victim.Id); return true; }
            }
            if (ability.Kind == AbilityKind.FlameShield)
            {
                var front = allies.Where(u => InRange(u,ability.RangeMm) && u.FlameShield == null
                    && enemies.Any(e => SeparationSquared(u,e) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm))
                    .OrderByDescending(u => enemies.Count(e => SeparationSquared(u,e) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm))
                    .ThenByDescending(u => u.HealthMilli).ThenBy(u => u.Id).FirstOrDefault();
                if (front != null) { Cast(ability,front.Id); return true; }
            }
            if (ability.Kind is AbilityKind.NegateAbility or AbilityKind.RainOfFire)
            {
                bool Useful(SimUnit e) => ability.Kind == AbilityKind.RainOfFire ? e.HeroBurns.Count == 0
                    : e.Negated == null && e.EnabledAbilities.Any(a => a.Kind != AbilityKind.Unsimulated);
                var spot = enemies.Where(e => InRange(e,ability.RangeMm) && ValidGroundAbilityTarget(hero,ability,e.Node))
                    .Select(e => new { Unit=e,Count=enemies.Count(v => Useful(v) && DistanceSquared(v.X,v.Z,e.X,e.Z) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm) })
                    .OrderByDescending(s => s.Count).ThenBy(s => s.Unit.Id).FirstOrDefault();
                if (spot != null && (spot.Count >= 2 || spot.Count > 0 && spot.Unit.Definition.Hero))
                { Cast(ability,cell:spot.Unit.Node,x:spot.Unit.X,z:spot.Unit.Z); return true; }
            }
            if (ability.Kind == AbilityKind.PrisonOfIce)
            {
                // Takes the most dangerous single enemy out of the fight: a hero first, then the hardest hitter.
                var victim = enemies.Where(e => InRange(e,ability.RangeMm) && e.Dazed == null && ValidAbilityTarget(hero,ability,e)
                        && !Rules.Pair(hero.Definition.Id,e.Definition.Id).Invincible)
                    .OrderByDescending(e => e.Definition.Hero).ThenByDescending(e => e.Definition.DpsMilli*(long)e.MaxHealth).ThenBy(e => e.Id).FirstOrDefault();
                if (victim != null && (victim.Definition.Hero || victim.Target >= 0 || victim.MaxHealth >= 250)) { Cast(ability,victim.Id); return true; }
            }
            if (ability.Kind == AbilityKind.ShadowBind && Economies.TryGetValue(ai.Team,out var fateBank) && fateBank.Fate >= ability.ActivateFateCost+2)
            {
                // A Wraith beside the King becomes a Black Rider while the bank holds two Fate more than it costs.
                var wraith = allies.Where(u => InRange(u,ability.RangeMm) && ValidAbilityTarget(hero,ability,u)).OrderByDescending(u => u.HealthMilli).ThenBy(u => u.Id).FirstOrDefault();
                if (wraith != null) { Cast(ability,wraith.Id); return true; }
            }
            if (ability.Kind == AbilityKind.BloodMark)
            {
                var wounded = allies.Where(u => InRange(u,ability.RangeMm) && ValidAbilityTarget(hero,ability,u) && u.HealEffects.Count == 0
                        && u.HealthMilli*100 < (long)u.MaxHealth*1000*60)
                    .OrderBy(u => u.HealthMilli*1_000_000/Math.Max(1,(long)u.MaxHealth*1000)).ThenBy(u => u.Id).FirstOrDefault();
                if (wounded != null) { Cast(ability,wounded.Id); return true; }
            }
            if (ability.Kind == AbilityKind.PoisonTrap && Traps.Count(t => t.Team == ai.Team) < ability.Limit
                && enemies.Count(e => InRange(e,ability.AreaRadiusMm+ability.ResponseRangeMm)) >= 2)
            { Cast(ability,hero.Id); return true; }
            if (ability.Kind == AbilityKind.Torch)
            {
                // The nearest enemy building within reach of the throw, while the party is not already fighting units beside the hero.
                var building = ConstructionSites.Values.Where(s => ValidCaptureTarget(hero,ability,s.Id)
                        && DistanceSquared(hero.X,hero.Z,s.X,s.Z) <= ((long)ability.RangeMm+s.RadiusMm)*((long)ability.RangeMm+s.RadiusMm)
                        && s.FirePpm < 300_000 && !enemies.Any(e => InRange(e,6000)))
                    .OrderBy(s => DistanceSquared(hero.X,hero.Z,s.X,s.Z)).ThenBy(s => s.Id).FirstOrDefault(s => AiReaches(hero,s));
                if (building != null) { Cast(ability,building.Id); return true; }
            }
            if (ability.Kind == AbilityKind.SatchelCharge && !Satchels.Any(b => b.Owner == hero.Id) && enemies.Count(e => InRange(e,ability.AreaRadiusMm+2000)) >= 2)
            { Cast(ability,hero.Id); return true; }
            if (ability.Kind == AbilityKind.TwistingInfluence)
            {
                // The strongest ordinary unit in reach: "a tough enemy unit like a Huorn or a Troll Bonecleaver" (the manual).
                var convert = enemies.Where(e => InRange(e,ability.RangeMm) && ValidAbilityTarget(hero,ability,e))
                    .OrderByDescending(e => (long)e.MaxHealth*e.Definition.DpsMilli).ThenBy(e => e.Id).FirstOrDefault();
                if (convert != null && convert.MaxHealth >= 250) { Cast(ability,convert.Id); return true; }
            }
            if (ability.Kind is AbilityKind.InvisibleForce or AbilityKind.Pestilence)
            {
                bool Useful(SimUnit e) => ability.Kind == AbilityKind.InvisibleForce ? !e.Definition.Hero && !e.Definition.KnockbackImmune
                    : e.Poisons.Count == 0 && (e.Immunities & StatusProtection.Poison) == 0;
                var spot = enemies.Where(e => InRange(e,ability.RangeMm) && ValidGroundAbilityTarget(hero,ability,e.Node))
                    .Select(e => new { Unit=e,Count=enemies.Count(v => Useful(v) && DistanceSquared(v.X,v.Z,e.X,e.Z) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm) })
                    .OrderByDescending(s => s.Count).ThenBy(s => s.Unit.Id).FirstOrDefault();
                if (spot != null && spot.Count >= 3) { Cast(ability,cell:spot.Unit.Node,x:spot.Unit.X,z:spot.Unit.Z); return true; }
            }
            if (ability.Kind == AbilityKind.Capture && ai.Party.Any(id => Units[id].Present && SeparationSquared(hero,Units[id]) <= 12000L*12000))
            {
                var tower = ConstructionSites.Values.Where(s => ValidCaptureTarget(hero,ability,s.Id)
                        && DistanceSquared(hero.X,hero.Z,s.X,s.Z) <= 12000L*12000)
                    .OrderBy(s => DistanceSquared(hero.X,hero.Z,s.X,s.Z)).ThenBy(s => s.Id).FirstOrDefault(s => AiReaches(hero,s));
                if (tower != null) { Cast(ability,tower.Id); return true; }
            }
        }
        return false;
    }
}
