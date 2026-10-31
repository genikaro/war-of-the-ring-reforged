using Reforged.Simulation;
using Reforged.Presentation;

public static class WorkerCombatChecks
{
    public static void Run(Action<bool,string> check,string? cache = null)
    {
        var worker = cache == null ? (UnitDefinition.Probe with { Name = "UNIT_G_HUMAN_WORKER" }).WithWorkerCombat()
            : CombatRules.Parse(File.ReadAllText(Path.Combine(cache,"combat/rules.json"))).Units["UNIT_G_HUMAN_WORKER"];
        worker.Validate();
        var attack = worker.Attacks.Single();
        check(attack.Clip == "G_Human_Worker_Attack_Melee_01" && attack.Hits.Length == 1 && attack.Hits[0].Tick == 18,
            "worker uses the original single-punch animation with its impact at 60 percent of the clip");
        check(attack.DurationTicks == 30 && UnitHudStats.SpeedName(UnitHudStats.SecondsPerHit(worker)) == "average",
            "worker attacks at an average speed of one punch every 1.5 seconds");
        check(worker.HitDamage(attack,0,worker.DpsMilli) == 5000 && worker.HitDamage(attack,0,0) == 0
            && worker.HitDamage(attack,0,worker.DpsMilli*2) == 10000,
            "worker deals exactly five base damage, retaining normal rate bonuses and armor");

        // Use small fixture bodies so contact is independent of imported navigation clearance.
        worker = worker with { BodyRadiusMm = 350,BodyHeightMm = 700,AttackGapMm = 0,ReachMm = 2000 };
        var nav = new GridNavigation(30,16,1000,Enumerable.Repeat(true,480).ToArray(),new int[31*17]);
        var world = new SimWorld(nav);
        var attacker = world.Spawn(0,5*30+5,worker);
        world.Spawn(1,5*30+7,UnitDefinition.Probe with { Name = "target",MaxHealth = 1000,Passive = true });
        world.Enqueue(0,[attacker.Id],OrderKind.Attack,target: 1);
        var hits = new List<CombatEvent>();
        for (int tick = 0; tick < 180; tick++)
        {
            world.Step(); hits.AddRange(world.CombatEvents.Where(e => e.Kind == CombatEventKind.Hit && e.Unit == 0));
        }
        check(hits.Count >= 5 && hits.All(h => h.DamageMilli == 5000)
            && hits.Zip(hits.Skip(1)).All(p => p.Second.Tick-p.First.Tick == 30),
            "successive worker punches deal stable five damage with no rapid multi-hit bursts");
        var stats = UnitHudStats.From(attacker,world.Rules);
        check(stats.Attack == "5" && stats.AttackTip.Contains("average (a hit every 1.5 s)"),
            "worker HUD reports five damage and the real average attack interval");
    }
}
