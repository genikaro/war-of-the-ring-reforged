using Reforged.Simulation;
using System.Text.Json;
using Reforged.Presentation;

static class MeleeChecks
{
    private static UnitDefinition Profile => UnitDefinition.Probe with { Id = 100, Name = "timed", DpsMilli = 20000,
        Attacks = [new("swing",1,20,[new(5,1_000_000)])] };
    private static SimWorld Open(CombatRules? rules = null)
        => new(new(16,12,1000,Enumerable.Repeat(true,192).ToArray(),new int[221]),rules);
    private static SimWorld Fixture(UnitDefinition? attacker = null,UnitDefinition? victim = null,CombatRules? rules = null)
    {
        var world = Open(rules); world.Spawn(0,85,attacker ?? Profile); world.Spawn(2,87,victim ?? Profile);
        world.Enqueue(0,[0],OrderKind.Attack,target:1); world.Units[1].Cooldown = 10000;
        return world;
    }
    private static void Advance(SimWorld world,int count) { for (int i = 0; i < count; i++) world.Step(); }
    private static readonly int[,] Ratings = { {100,125,75,100},{75,100,125,100},{125,75,100,100},{100,100,100,100} };
    private static DamageTypeRule[] TypeRules => Enumerable.Range(0,4).SelectMany(from =>
        Enumerable.Range(0,4).Select(to => new DamageTypeRule(from,to,Ratings[from,to]*10000))).ToArray();
    private static CombatRules Rules(params UnitPairRule[] pairs) => CombatRules.Parse(JsonSerializer.Serialize(new {
        FormatVersion = 3,TicksPerSecond = 20,Units = new[] { Profile },TypeMatchups = TypeRules,Pairs = pairs }));

    public static void Run(Action<bool,string> check)
    {
        var world = Fixture(); world.Step();
        check(world.Units[0].StrikeActive && world.CombatEvents.Single().Kind == CombatEventKind.Start,"melee starts a source timeline");
        Advance(world,4);
        check(world.Units[1].HealthMilli == 100000,"windup never damages early");
        world.Step();
        check(world.CombatEvents.Single(e => e.Kind == CombatEventKind.Hit).Tick == 5 && world.Units[1].HealthMilli == 80000,
            "damage lands exactly on fixed hit tick");
        Advance(world,14);
        check(world.Units[1].HealthMilli == 80000 && world.Units[0].AttackSerial == 1,"recovery cannot repeat damage");
        world.Step();
        check(world.Units[0].AttackStartTick == 20 && world.Units[0].AttackSerial == 2,"next swing starts at full cycle boundary");

        var cancelled = Fixture(); Advance(cancelled,3); cancelled.Enqueue(0,[0],OrderKind.Stop); cancelled.Step();
        check(!cancelled.Units[0].StrikeActive && cancelled.CombatEvents.Any(e => e.Kind == CombatEventKind.Cancel),"Stop cancels windup");
        cancelled.Enqueue(0,[0],OrderKind.Attack,target:1); Advance(cancelled,16);
        check(cancelled.Units[1].HealthMilli == 100000 && cancelled.Units[0].AttackSerial == 1,"cancel and reissue cannot bypass recovery");
        Advance(cancelled,6);
        check(cancelled.Units[1].HealthMilli == 80000,"replacement attack hits after remaining recovery and new windup");

        var replaced = Fixture(); replaced.Spawn(2,69,Profile); Advance(replaced,3);
        replaced.Enqueue(0,[0],OrderKind.Attack,target:2); Advance(replaced,4);
        check(replaced.Units[1].HealthMilli == 100000 && replaced.Units[2].HealthMilli == 100000,"retargeting cancels the previous pending hit");
        var dead = Fixture(); Advance(dead,3); dead.Units[1].Health = 0; Advance(dead,5);
        check(!dead.Units[0].StrikeActive && dead.Units[1].HealthMilli == 0,"dead target cancels pending hit");
        var moving = Fixture(); Advance(moving,2); moving.Enqueue(2,[1],OrderKind.Move,94); Advance(moving,6);
        check(moving.Units[1].HealthMilli == 100000,"target leaving reach cancels windup");
        var queued = Fixture(); Advance(queued,2); queued.Enqueue(0,[0],OrderKind.Move,90,queued:true); queued.Step();
        check(queued.Units[0].StrikeActive && queued.Units[0].OrderQueue.Count == 1,"queued orders preserve a current swing");

        var multi = Fixture(Profile with { Attacks = [new("double",1,20,[new(3,1000000),new(8,1000000)])] });
        Advance(multi,4); check(multi.Units[1].HealthMilli == 90000,"first multi-hit event gets its normalized share");
        Advance(multi,5); check(multi.Units[1].HealthMilli == 80000,"multi-hit clip preserves cycle DPS");
        var armored = Fixture(victim:Profile with { ArmorMilli = 3000 }); Advance(armored,6);
        check(armored.Units[1].HealthMilli == 83000,"documented armor policy applies once to cycle DPS");
        var fractional = Fixture(Profile with { DpsMilli = 3333 }); Advance(fractional,6);
        check(fractional.Units[1].HealthMilli == 96667 && fractional.Units[1].Health == 97,"fractional damage is retained in integer milli-health");
        var start = Fixture(victim:Profile with { MaxHealth = 350,StartingHealthPercent = 50 });
        check(start.Units[1].Health == 175 && start.Units[1].MaxHealth == 350,"spawn uses source health and starting percentage");
        var partial = Fixture(victim:Profile with { MaxHealth = 1,StartingHealthPercent = 50 });
        check(partial.Units[1].HealthMilli == 500,"starting percentage retains fractional health");
        var lethal = Open(); lethal.Spawn(0,85,Profile with { MaxHealth = 1 });
        lethal.Spawn(1,87,Profile with { Attacks = [new("early",1,20,[new(2,1000000)])] });
        lethal.Enqueue(0,[0],OrderKind.Attack,target:1); Advance(lethal,3);
        check(lethal.Units[0].Health == 0 && !lethal.Units[0].StrikeActive,"later unit's lethal hit immediately cancels earlier unit's pending swing");

        CombatRules PairRules(bool invincible) => Rules(new UnitPairRule(100,100,1500000,invincible));
        var pair = Fixture(victim:Profile with { ArmorMilli = 3000 },rules:PairRules(false)); Advance(pair,6);
        check(pair.Units[1].HealthMilli == 73000,"explicit pair multiplier is applied once before armor");
        var immune = Fixture(rules:PairRules(true)); Advance(immune,6);
        check(immune.Units[1].HealthMilli == 100000 && immune.CombatEvents.Any(e => e.Kind == CombatEventKind.Hit && e.DamageMilli == 0),"pair invincibility retains hit event without damage");

        for (int from = 0; from < 4; from++) for (int to = 0; to < 4; to++)
        {
            var rated = Fixture(Profile with { DamageType = from },Profile with { DamageType = to },Rules());
            Advance(rated,6);
            check(rated.Units[1].HealthMilli == 100000-20000L*Ratings[from,to]/100,$"type matchup {from} -> {to} applies once at the hit");
        }
        check(Rules().TypeScale(-1,0) == 1000000 && Rules().TypeScale(0,99) == 1000000,"unclassified types remain neutral");
        var stacked = Fixture(Profile with { DamageType = 0 },Profile with { DamageType = 1,ArmorMilli = 3000 },PairRules(false));
        Advance(stacked,6); check(stacked.Units[1].HealthMilli == 65500,"type and explicit pair scales apply once each before armor");
        var typedImmune = Fixture(Profile with { DamageType = 0 },Profile with { DamageType = 1 },PairRules(true));
        Advance(typedImmune,6); check(typedImmune.Units[1].HealthMilli == 100000,"invincibility overrides type advantage");

        var killing = Fixture(Profile with { Attacks = [new("double",1,20,[new(3,1000000),new(8,1000000)])] },Profile with { MaxHealth = 5 });
        Advance(killing,30);
        check(killing.Units[0].KillCount == 1 && killing.Units[1].Health == 0 && killing.Units[1].KillCount == 0,"only the lethal attacker gains one victory despite later hit events");
        var rival = Open(); rival.Spawn(0,85,Profile); rival.Spawn(0,89,Profile); rival.Spawn(1,87,Profile with { MaxHealth = 5 });
        rival.Units[2].Cooldown = 10000; rival.Enqueue(0,[0,1],OrderKind.Attack,target:2); Advance(rival,30);
        check(rival.Units[0].KillCount == 1 && rival.Units[1].KillCount == 0,"simultaneous attackers credit only the first lethal hit");
        var friendly = Open(); friendly.Spawn(0,85,Profile); friendly.Spawn(0,87,Profile with { MaxHealth = 5 });
        friendly.Units[1].Cooldown = 10000; friendly.Enqueue(0,[0],OrderKind.ForceAttack,target:1); Advance(friendly,30);
        check(friendly.Units[1].Health == 0 && friendly.Units[0].KillCount == 0,"friendly force attacks do not award victories");
        string beforeKillChange = killing.StateDigest(); killing.Units[0].KillCount++;
        check(beforeKillChange != killing.StateDigest(),"victories are included in replay state"); killing.Units[0].KillCount--;
        var stats = UnitHudStats.From(killing.Units[0],Rules());
        check(stats.Attack == "20" && stats.Armor == "0" && stats.Victories == "1" && stats.Health == "100/100","unit HUD reflects live simulation stats and kills");
        check(UnitHudStats.From(world.Units[0],Rules()).Icon == "","unclassified profiles never borrow a weapon icon");
        for (int type = 0; type < 4; type++)
        {
            var unit = Fixture(Profile with { DamageType = type },rules:Rules()).Units[0];
            var typedStats = UnitHudStats.From(unit,Rules());
            check(typedStats.Icon == "counter_marker_G_"+new[] {"slashing","piercing","blunt","hero"}[type]
                && typedStats.Matchups.Contains("100% vs Hero"),"HUD uses original type art and neutral hero target: "+type);
        }
        foreach (var invalid in new[] { TypeRules.Skip(1).ToArray(),TypeRules.Append(TypeRules[0]).ToArray() })
        {
            try { CombatRules.Parse(JsonSerializer.Serialize(new { FormatVersion = 3,TicksPerSecond = 20,Units = new[] {Profile},TypeMatchups = invalid,Pairs = Array.Empty<UnitPairRule>() }));
                check(false,"invalid matchup table must fail"); }
            catch (ArgumentException) { check(true,"incomplete or duplicate matchup table rejected"); }
        }

        var capped = Open(); capped.Spawn(0,85,Profile); capped.Spawn(0,89,Profile);
        capped.Spawn(2,87,Profile with { MaxAttackers = 1 }); capped.Units[2].Cooldown = 10000;
        capped.Enqueue(0,[0,1],OrderKind.Attack,target:2); capped.Step();
        check(capped.Units.Count(u => u.StrikeActive) == 1 && capped.Units[0].StrikeActive,"source attacker cap has stable unit-ID priority");
        capped.Units[0].Health = 0; Advance(capped,6);
        check(capped.Units[1].StrikeActive && capped.Units[2].HealthMilli == 80000,"waiting attacker takes released slot");
        var crowded = Open(); crowded.Spawn(0,85,Profile); crowded.Spawn(0,86,Profile);
        crowded.Spawn(2,87,Profile with { MaxAttackers = 8 }); crowded.Units[2].Cooldown = 10000;
        crowded.Enqueue(0,[0,1],OrderKind.Attack,target:2); Advance(crowded,80);
        check(crowded.Units[2].HealthMilli < 100000,"attacker cap still lets crowded allies reposition into eligible slots");

        var a = Fixture(); var b = Fixture(); var traceA = new List<CombatEvent>(); var traceB = new List<CombatEvent>();
        for (int i = 0; i < 50; i++) { a.Step(); b.Step(); traceA.AddRange(a.CombatEvents); traceB.AddRange(b.CombatEvents); }
        check(a.StateDigest() == b.StateDigest() && traceA.SequenceEqual(traceB),"timelines and precise health replay identically");
        var valid = Profile with { Attacks = [new("invalid",1,20,[new(20,1)])] };
        try { valid.Validate(); check(false,"invalid timeline must fail"); } catch (ArgumentException) { check(true,"invalid timeline rejected"); }
    }

    public static void Cached(Action<bool,string> check,string cache)
    {
        var rules = CombatRules.Parse(File.ReadAllText(Path.Combine(cache,"combat/rules.json")));
        var hero = rules.Units["UNIT_G_HERO_ARAGORN"]; var knight = rules.Units["UNIT_G_ELF_KNIGHT"]; var orc = rules.Units["UNIT_E_URUK_HAI"];
        var abilities = hero.Abilities ?? throw new Exception("Cached Aragorn abilities missing");
        var heal = abilities.Single(a => a.Kind == AbilityKind.Heal);
        check((heal.Slot,heal.HealMilli,heal.RechargeTicks,heal.RangeMm,heal.CastTicks,heal.EffectTick) == (0,250000,340,10000,21,8)
            && heal.CastClip == "G_Aragorn_Greater_Kingsfoil","cached Greater Kingsfoil follows the source spell and toolscript timeline");
        var fury = abilities.Single(a => a.Kind == AbilityKind.Fury);
        check(fury.Passive && fury.Slot == 1 && fury.DpsMilli == 30000 && fury.BonusScale == 1500000
            && fury.BonusTargets!.SequenceEqual(new[] {52,33}),"cached Fury imports its replacement weapon and exact target-type bonus");
        check(abilities.Single(a => a.Kind == AbilityKind.Immunity) is {Passive:true,Slot:2,Immunities:(StatusProtection)15}
            && knight.Abilities!.Length == 0,"cached Immunity remains passive and infantry gain no hero abilities");
        var archer = rules.Units["UNIT_G_ELVEN_ARCHER"];
        check(archer.IsRanged && (archer.MaxHealth,archer.DpsMilli,archer.ArmorMilli,archer.DamageType) == (180,14000,0,1),
            "Elven Archer imports source health, missile DPS, armor and piercing classification");
        check((archer.ReachMm,archer.MinimumRangeMm,archer.BodyRadiusMm,archer.BodyHeightMm) == (13000,1000,535,2351)
            && archer.Attacks.Single(a => !a.Melee).DurationTicks == 41 && archer.Attacks.Single(a => !a.Melee).Hits.Single().Tick == 22,
            "Elven Archer range, body and bow release are source-derived");
        check(archer.HasMelee && archer.MeleeDpsMilli == 14000 && archer.MeleeGapMm == 500
            && archer.Attacks.Count(a => a.Melee) == 3,"Elven Archer imports its distinct melee weapon and all three original melee clips");
        var archerDuel = Open(rules); archerDuel.Spawn(0,85,archer); archerDuel.Spawn(2,87,orc);
        archerDuel.Units[1].Health = 10000; archerDuel.Units[1].Cooldown = 10000;
        archerDuel.Enqueue(2,[1],OrderKind.HoldGround); archerDuel.Enqueue(0,[0],OrderKind.HoldGround);
        var archerHits = new List<CombatEvent>();
        for (int tick = 0; tick < 3000; tick++) { archerDuel.Step(); archerHits.AddRange(archerDuel.CombatEvents.Where(e => e.Kind == CombatEventKind.Hit && e.Unit == 0)); }
        check(archerHits.Select(e => e.Attack).Distinct().Count() == 3 && archerHits.All(e => archer.Attacks[e.Attack].Melee
            && e.Tick-e.StartTick == archer.Attacks[e.Attack].Hits[e.Hit].Tick),
            "all three original archer melee variants deal damage on their exact source hit events");
        check(archerDuel.Projectiles.Count == 0 && archerDuel.Units[1].Health < 10000,
            "source archer fights in melee without launching arrows");
        check((hero.BodyRadiusMm,knight.BodyRadiusMm,orc.BodyRadiusMm) == (800,927,693)
            && hero.ModelScalePpm == 1_120_000 && rules.Units.Values.All(u => u.FriendlyCompressionPpm == 500_000),
            "cache applies explicit hero sizing and compressible friendly personal space");
        check((hero.BodyHeightMm,knight.BodyHeightMm,orc.BodyHeightMm) == (2347,2534,2197),"cached body heights include the explicit hero presentation scale");
        check((hero.WalkScale,knight.WalkScale,orc.WalkScale) == (364585,469073,432872),"cached walk/run ratios derive from source root-track displacement and duration");
        check(new[] {hero,knight,orc}.All(u => u.WalkClip.EndsWith("_Walk") && u.RunClip.EndsWith("_Run")),"cached locomotion states resolve separate original walk/run clips");
        for (int from = 0; from < 4; from++) for (int to = 0; to < 4; to++)
            check(rules.TypeScale(from,to) == Ratings[from,to]*10000,$"cached type matrix agrees with requested ratings {from}/{to}");
        check((hero.MaxHealth,knight.MaxHealth,orc.MaxHealth) == (485,350,240),"cached base health is traced to three source units");
        check((hero.DpsMilli,knight.DpsMilli,orc.DpsMilli) == (20000,10000,14000),"cached melee weapons have distinct source DPS");
        check(hero.Attacks[2].Hits.Select(h => h.Tick).SequenceEqual(new[] {13,27}) && orc.Attacks[0].Hits[0].Tick == 15,
            "cached clip durations and multi-hit events quantize as expected");
        var reports = new List<object>();
        foreach (var pair in new[] {(hero,orc),(orc,hero),(knight,orc),(orc,knight)})
        {
            (SimWorld World,List<CombatEvent> Events) Duel()
            {
                var world = Open(rules); world.Spawn(0,85,pair.Item1); world.Spawn(1,87,pair.Item2);
                world.Enqueue(0,[0],OrderKind.Attack,target:1); var events = new List<CombatEvent>();
                for (int tick = 0; tick < 2400 && world.Units.All(u => u.Health > 0); tick++)
                { world.Step(); events.AddRange(world.CombatEvents); }
                return (world,events);
            }
            var first = Duel(); var second = Duel();
            check(first.World.StateDigest() == second.World.StateDigest() && first.Events.SequenceEqual(second.Events),"source duel replays: "+pair.Item1.DisplayName+" / "+pair.Item2.DisplayName);
            check(first.World.Units.Count(u => u.Health > 0) == 1 && first.Events.Where(e => e.Kind == CombatEventKind.Hit).All(e =>
                e.Tick-e.StartTick == first.World.Units[e.Unit].Definition.Attacks[e.Attack].Hits[e.Hit].Tick),"source duel ends with exact hit timing: "+pair.Item1.DisplayName);
            reports.Add(new { attacker = pair.Item1.Name,defender = pair.Item2.Name,ticks = first.World.Tick,
                winner = first.World.Units.Single(u => u.Health > 0).Definition.Name,
                remaining_millihealth = first.World.Units.Select(u => u.HealthMilli),digest = first.World.StateDigest(),events = first.Events });
        }
        File.WriteAllText(Path.Combine(cache,"melee-duels-report.json"),JsonSerializer.Serialize(new { rules_sha256 = rules.Hash,
            original_formula_verified = false,duels = reports },new JsonSerializerOptions { WriteIndented = true }));
    }
}
