using Reforged.Input;
using Reforged.Simulation;

public static class EconomyChecks
{
    public static void Run(Action<bool,string> check)
    {
        SimWorld Fixture(int initial = 20,int fraction = 1_000_000,int rate = 500,bool barrier = false)
        {
            var allowed = Enumerable.Repeat(true,360).ToArray();
            if (barrier) for (int z = 0; z < 12; z++) allowed[z*30+16] = false;
            var nav = new GridNavigation(30,12,1000,allowed,new int[31*13]);
            var world = new SimWorld(nav);
            world.Workers.Add("probe",new(10,9)); world.Economies.Add(0,new(115,325)); world.Economies.Add(1,new(115,325));
            world.Resources.Add(7,new(7,ResourceKind.Ore,23500,5500,initial,rate,fraction,1800,20));
            world.Resources.Add(8,new(8,ResourceKind.Food,23500,8500,30,0,0,1800,20));
            world.DropOffs.Add(new(0,2500,5500,1800));
            world.Spawn(0,5*30+3); world.Spawn(1,11*30+29).Order = OrderKind.HoldGround;
            return world;
        }
        void Step(SimWorld world,int count) { for (int i = 0; i < count; i++) world.Step(); }
        void Until(SimWorld world,Func<bool> done,int limit = 6000) { for (int i = 0; i < limit; i++) { world.Step(); if (done()) return; } }
        var rules = new WorkerEconomy(10,9);

        // Raw ore pile: one ore per pickaxe hit, five per trip, then pick-up, carry and put-down.
        var world = Fixture(); var worker = world.Units[0];
        world.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(world,20);
        check(worker.X > 3500 && worker.CargoMilli == 0 && world.Economies[0].Ore == 325,"gather approaches before harvesting or crediting");
        Until(world,() => worker.GatherPhase == GatherPhase.Mining);
        int start = worker.GatherPhaseTick;
        Until(world,() => world.Tick == start+rules.MineHitTick);
        check(worker.CargoMilli == 0 && world.Resources[7].RemainingMilli == 20000,"no ore is mined before the pickaxe lands");
        world.Step();
        check(worker.CargoMilli == 1000 && worker.CargoKind == ResourceKind.Ore && world.Resources[7].RemainingMilli == 19000,
            "each pickaxe hit mines exactly one ore on the clip's hit tick");
        Until(world,() => worker.GatherPhase != GatherPhase.Mining);
        check(worker.GatherPhase == GatherPhase.PickingUp && worker.GatherPhaseTick == start+5*rules.MineTicks && worker.CargoMilli == 5000,
            "five hits fill a raw load and the fifth swing finishes before the pick-up");
        Until(world,() => worker.GatherPhase != GatherPhase.PickingUp);
        check(world.Tick-1 == start+5*rules.MineTicks+rules.PickUpTicks && worker.ReturningCargo && worker.Goal >= 0,
            "the source pick-up clip plays before the trip home");
        Until(world,() => worker.GatherPhase == GatherPhase.PuttingDown);
        int putDown = worker.GatherPhaseTick;
        check(world.Economies[0].Ore == 325 && worker.CargoMilli == 5000,"carried ore is not banked before the put-down");
        Until(world,() => worker.CargoMilli == 0);
        check(world.Tick-1 == putDown+rules.PutDownTicks && world.Economies[0].Ore == 330,"the load is credited when the put-down clip ends");
        Until(world,() => worker.Order == OrderKind.Idle);
        check(world.Economies[0].Ore == 345 && world.Resources[7].RemainingMilli == 0 && worker.CargoMilli == 0,"full loads return to owned depot without loss");
        check(world.Economies[1].Ore == 325 && worker.Order == OrderKind.Idle,"gather credits only owner and ends on exhaustion");
        var replay = Fixture(); replay.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(replay,world.Tick);
        check(replay.StateDigest() == world.StateDigest(),"economy replay is deterministic");
        replay.Economies[0].OreMilli++;
        check(replay.StateDigest() != world.StateDigest(),"digest includes fractional bank state");

        var capped = Fixture(20,550_000); capped.Enqueue(0,[0],OrderKind.Gather,target: 7); Until(capped,() => capped.Units[0].Order == OrderKind.Idle);
        check(capped.Economies[0].Ore == 336 && capped.Resources[7].RemainingMilli == 9000,"raw ore stops at source fraction and deposits final partial load");
        var cancelled = Fixture(); cancelled.Enqueue(0,[0],OrderKind.Gather,target: 7); Until(cancelled,() => cancelled.Units[0].CargoMilli == 2000);
        cancelled.Enqueue(0,[0],OrderKind.Stop); cancelled.Step();
        long remaining = cancelled.Resources[7].RemainingMilli,carried = cancelled.Units[0].CargoMilli;
        Step(cancelled,80);
        check(cancelled.Units[0].Order == OrderKind.Idle && cancelled.Units[0].GatherPhase == GatherPhase.None && cancelled.Resources[7].RemainingMilli == remaining
            && cancelled.Units[0].CargoMilli == carried,"stop cancels mining and preserves cargo");
        cancelled.Enqueue(0,[0],OrderKind.Gather,target: 7); Until(cancelled,() => cancelled.Units[0].Order == OrderKind.Idle);
        check(cancelled.Economies[0].Ore == 345,"resuming gathering returns preserved cargo");
        var swing = Fixture(); swing.Enqueue(0,[0],OrderKind.Gather,target: 7); Until(swing,() => swing.Units[0].GatherPhase == GatherPhase.Mining);
        Step(swing,rules.MineHitTick-2); swing.Enqueue(0,[0],OrderKind.Stop); Step(swing,40);
        check(swing.Units[0].CargoMilli == 0 && swing.Resources[7].RemainingMilli == 20000,"an interrupted swing mines nothing");

        var invalid = Fixture(); invalid.Enqueue(0,[0],OrderKind.Move,8); invalid.Step();
        invalid.Enqueue(1,[0],OrderKind.Gather,target: 7); invalid.Enqueue(0,[0],OrderKind.Gather,target: 8);
        invalid.Enqueue(0,[0],OrderKind.Gather,target: 999); invalid.Step();
        check(invalid.Units[0].Order == OrderKind.Move && invalid.Resources[7].RemainingMilli == 20000,"invalid, food and foreign gathering leave current order intact");
        invalid.Workers.Clear(); invalid.Enqueue(0,[0],OrderKind.Gather,target: 7); invalid.Step();
        check(invalid.Units[0].Order == OrderKind.Move,"nonworkers cannot gather");

        var queued = Fixture(2); queued.Enqueue(0,[0],OrderKind.Move,5*30+7);
        queued.Enqueue(0,[0],OrderKind.Gather,target: 7,queued: true); queued.Step();
        check(queued.Units[0].Order == OrderKind.Move && queued.Units[0].OrderQueue.Count == 1,"shift gather queues after movement");
        queued.Enqueue(0,[0],OrderKind.Move,5*30+5,queued: true); Step(queued,1400);
        check(queued.Economies[0].Ore == 327 && queued.Units[0].Node == 5*30+5,"queued move follows exhaustion and final delivery");

        var dead = Fixture(); dead.Enqueue(0,[0],OrderKind.Gather,target: 7); Until(dead,() => dead.Units[0].CargoMilli == 2000);
        dead.Units[0].Health = 0; dead.Step(); remaining = dead.Resources[7].RemainingMilli; Step(dead,60);
        check(dead.Units[0].CargoMilli == 0 && dead.Resources[7].RemainingMilli == remaining && dead.Economies[0].Ore == 325,"dead workers stop and never deliver cargo");

        var stranded = Fixture(3); stranded.DropOffs.Clear(); stranded.DropOffs.Add(new(1,23500,5500,5000));
        stranded.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(stranded,700);
        check(stranded.Units[0].CargoMilli == 3000 && stranded.Economies[0].Ore == 325 && stranded.Economies[1].Ore == 325,"enemy depots cannot receive cargo");
        var full = Fixture(3); full.Economies[0] = new(115,325,99999,325);
        full.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(full,700);
        check(full.Units[0].CargoMilli == 3000 && full.Economies[0].Ore == 325,"full bank retains undelivered cargo");

        var crowded = Fixture(12); crowded.Spawn(0,7*30+3); crowded.Spawn(0,3*30+3);
        crowded.Enqueue(0,[0,2,3],OrderKind.Gather,target: 7); Until(crowded,() => crowded.Units.Where(u => u.Team == 0).All(u => u.Order == OrderKind.Idle));
        check(crowded.Economies[0].Ore == 337 && crowded.Resources[7].RemainingMilli == 0
            && crowded.Units.All(u => u.CargoMilli == 0),"multiple workers conserve shared stock and complete deliveries");

        var slow = Fixture(1,1_000_000,10); slow.Enqueue(0,[0],OrderKind.Gather,target: 7);
        Until(slow,() => slow.Units[0].CargoMilli > 0);
        check(slow.Units[0].CargoMilli == 1000,"the source gather rate only gates availability; a hit still mines one ore");

        var interrupted = Fixture(1); interrupted.Enqueue(0,[0],OrderKind.Gather,target: 7);
        Until(interrupted,() => !interrupted.Resources[7].CanGather);
        interrupted.Enqueue(0,[0],OrderKind.Stop); interrupted.Step();
        check(interrupted.Units[0].CargoMilli == 1000 && !interrupted.Resources[7].CanGather,"stop can preserve a final load after exhaustion");
        interrupted.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(interrupted,500);
        check(interrupted.Economies[0].Ore == 326 && interrupted.Units[0].CargoMilli == 0,"exhausted source can resume delivery of preserved final cargo");

        var inaccessible = Fixture(3,barrier: true);
        inaccessible.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(inaccessible,400);
        check(inaccessible.Units[0].CargoMilli == 0 && inaccessible.Resources[7].RemainingMilli == 3000
            && inaccessible.Economies[0].Ore == 325,"unreachable sources cannot remotely generate income");

        // Completed Mill / Foundry: one worker inside at a time for two seconds, then out with a full load.
        SimWorld Improved(ResourceKind kind,bool complete = true)
        {
            var world = Fixture(); int id = kind == ResourceKind.Ore ? 7 : 8;
            world.ConstructionRules.Add(kind,new(kind == ResourceKind.Food ? "mill" : "foundry","Building",kind,0,50,60,100,750000,750000,100000,50000,[]));
            world.ConstructionSites.Add(id,new(id,-1,1800) { Team = 0,Complete = complete });
            world.Spawn(0,6*30+3);
            return world;
        }
        var mill = Improved(ResourceKind.Food);
        mill.Enqueue(0,[0,2],OrderKind.Gather,target: 8);
        Until(mill,() => mill.Units.Any(u => u.InsideSite == 8));
        var inside = mill.Units.First(u => u.InsideSite == 8); var waiting = mill.Units[inside.Id == 0 ? 2 : 0];
        int entered = inside.GatherPhaseTick;
        check(!inside.Present && inside.Health > 0 && inside.CargoMilli == 0 && inside.GatherPhase == GatherPhase.Inside
            && mill.Resources[8].RemainingMilli == 30000,"a worker enters the completed Mill empty and disappears");
        var down = new System.Numerics.Vector3(0,-1,0);
        check(UnitPicker.Pick(mill.Units,new(inside.X/1000f,10,inside.Z/1000f),down) == null,"a worker inside cannot be clicked");
        mill.Enqueue(0,[inside.Id],OrderKind.Stop); mill.Enqueue(1,[1],OrderKind.Attack,target: inside.Id); mill.Step();
        check(inside.InsideSite == 8 && inside.Order == OrderKind.Gather && mill.Units[1].Order == OrderKind.HoldGround,
            "a worker inside receives no orders and cannot be targeted");
        var blocker = mill.Spawn(0,inside.Node);
        check(blocker.Node == inside.Node,"a worker inside has no body blocking its cell");
        Until(mill,() => mill.Tick == entered+rules.InsideTicks);
        check(!inside.Present && waiting.InsideSite < 0 && waiting.Present && waiting.Goal < 0
            && SimWorld.DistanceSquared(waiting.X,waiting.Z,23500,8500) <= 1800L*1800,"only one worker is inside at a time; the next waits at the door");
        mill.Step();
        check(inside.Present && inside.CargoMilli == 10000 && inside.CargoKind == ResourceKind.Food && inside.ReturningCargo
            && mill.Resources[8].RemainingMilli == 20000,"after two seconds the worker comes out carrying a full food load");
        check(inside.Node != blocker.Node && !SimWorld.BodiesOverlap(inside,blocker),"a worker leaving a building steps to the nearest free cell");
        mill.Step();
        check(waiting.InsideSite == 8,"the waiting worker enters once the building is free");
        Until(mill,() => mill.Economies[0].Food > 115);
        check(mill.Economies[0].Food == 125 && inside.GatherPhase == GatherPhase.None,"a Mill trip credits ten food after the put-down");

        var foundry = Improved(ResourceKind.Ore); foundry.Enqueue(0,[0],OrderKind.Gather,target: 7);
        Until(foundry,() => foundry.Units[0].CargoMilli > 0);
        check(foundry.Units[0].Present && foundry.Units[0].CargoMilli == 9000 && foundry.Units[0].CargoKind == ResourceKind.Ore,
            "a Foundry trip carries nine ore after two seconds inside");
        Until(foundry,() => foundry.Economies[0].Ore > 325);
        check(foundry.Economies[0].Ore == 334,"a Foundry trip credits nine ore after the put-down");
        Until(foundry,() => foundry.Units[0].InsideSite == 7);
        var foundryReplay = Improved(ResourceKind.Ore); foundryReplay.Commands.AddRange(foundry.Commands); Step(foundryReplay,foundry.Tick);
        check(foundryReplay.StateDigest() == foundry.StateDigest(),"building visits replay identically");
        foundryReplay.Units[0].GatherPhaseTick++;
        check(foundryReplay.StateDigest() != foundry.StateDigest(),"digest includes gathering phases");

        var upgrade = Improved(ResourceKind.Ore,complete: false); upgrade.Enqueue(0,[0],OrderKind.Gather,target: 7);
        Until(upgrade,() => upgrade.Units[0].CargoMilli == 2000);
        upgrade.ConstructionSites[7].Complete = true;
        Until(upgrade,() => upgrade.Units[0].GatherPhase != GatherPhase.Mining);
        check(upgrade.Units[0].GatherPhase == GatherPhase.PickingUp && upgrade.Units[0].CargoMilli == 2000,"a completed Foundry ends raw mining after the current swing");
        Until(upgrade,() => upgrade.Units[0].InsideSite == 7);
        check(upgrade.Economies[0].Ore == 327 && upgrade.Units[0].CargoMilli == 0,"the partial raw load is delivered before entering the Foundry");

        // Debug cheats.
        var cheat = Fixture(); cheat.Economies[1] = new(115,325,99999,1000);
        cheat.GrantResources(0,1000); cheat.GrantResources(1,1000);
        check(cheat.Economies[0].Food == 1115 && cheat.Economies[0].Ore == 1325 && cheat.Economies[1].Ore == 1000,"the resource cheat credits one bank up to its storage");
    }
}
