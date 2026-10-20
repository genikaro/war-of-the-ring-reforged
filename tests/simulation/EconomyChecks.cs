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
            world.Resources.Add(7,new(7,ResourceKind.Ore,23500,5500,initial,rate,fraction,1800));
            world.Resources.Add(8,new(8,ResourceKind.Food,23500,8500,20,0,0,1800));
            world.DropOffs.Add(new(0,2500,5500,1800));
            world.Spawn(0,5*30+3); world.Spawn(1,11*30+29).Order = OrderKind.HoldGround;
            return world;
        }
        void Step(SimWorld world,int count) { for (int i = 0; i < count; i++) world.Step(); }
        var world = Fixture(); var worker = world.Units[0];
        world.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(world,20);
        check(worker.X > 3500 && worker.CargoMilli == 0 && world.Economies[0].Ore == 325,"gather approaches before harvesting or crediting");
        for (int i = 0; i < 400 && worker.CargoMilli == 0; i++) world.Step();
        check(worker.CargoMilli > 0 && world.Economies[0].Ore == 325,"carried ore is not banked at the source");
        Step(world,1600);
        check(world.Economies[0].Ore == 345 && world.Resources[7].RemainingMilli == 0 && worker.CargoMilli == 0,"full and partial loads return to owned depot without loss");
        check(world.Economies[1].Ore == 325 && worker.Order == OrderKind.Idle,"gather credits only owner and ends on exhaustion");
        var replay = Fixture(); replay.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(replay,world.Tick);
        check(replay.StateDigest() == world.StateDigest(),"economy replay is deterministic");
        replay.Economies[0].OreMilli++;
        check(replay.StateDigest() != world.StateDigest(),"digest includes fractional bank state");

        var capped = Fixture(20,550_000); capped.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(capped,1800);
        check(capped.Economies[0].Ore == 336 && capped.Resources[7].RemainingMilli == 9000,"raw ore stops at source fraction and deposits final partial load");
        var cancelled = Fixture(); cancelled.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(cancelled,220);
        cancelled.Enqueue(0,[0],OrderKind.Stop); cancelled.Step();
        long remaining = cancelled.Resources[7].RemainingMilli,carried = cancelled.Units[0].CargoMilli;
        Step(cancelled,40);
        check(cancelled.Units[0].Order == OrderKind.Idle && cancelled.Resources[7].RemainingMilli == remaining
            && cancelled.Units[0].CargoMilli == carried,"stop cancels harvesting and preserves cargo");
        cancelled.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(cancelled,1800);
        check(cancelled.Economies[0].Ore == 345,"resuming gathering returns preserved cargo");

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

        var dead = Fixture(); dead.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(dead,220);
        dead.Units[0].Health = 0; dead.Step(); remaining = dead.Resources[7].RemainingMilli; Step(dead,60);
        check(dead.Units[0].CargoMilli == 0 && dead.Resources[7].RemainingMilli == remaining && dead.Economies[0].Ore == 325,"dead workers stop and never deliver cargo");

        var stranded = Fixture(3); stranded.DropOffs.Clear(); stranded.DropOffs.Add(new(1,23500,5500,5000));
        stranded.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(stranded,700);
        check(stranded.Units[0].CargoMilli == 3000 && stranded.Economies[0].Ore == 325 && stranded.Economies[1].Ore == 325,"enemy depots cannot receive cargo");
        var full = Fixture(3); full.Economies[0] = new(115,325,99999,325);
        full.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(full,700);
        check(full.Units[0].CargoMilli == 3000 && full.Economies[0].Ore == 325,"full bank retains undelivered cargo");

        var crowded = Fixture(12); crowded.Spawn(0,7*30+3); crowded.Spawn(0,3*30+3);
        crowded.Enqueue(0,[0,2,3],OrderKind.Gather,target: 7); Step(crowded,2000);
        check(crowded.Economies[0].Ore == 337 && crowded.Resources[7].RemainingMilli == 0
            && crowded.Units.All(u => u.CargoMilli == 0),"multiple workers conserve shared stock and complete deliveries");

        var slow = Fixture(1,1_000_000,10); slow.DropOffs[0] = new(0,23500,5500,5000);
        slow.Enqueue(0,[0],OrderKind.Gather,target: 7);
        for (int i = 0; i < 500 && slow.Units[0].CargoMilli == 0; i++) slow.Step();
        check(slow.Units[0].CargoMilli == 10,"source raw rate preserves subunit progress at 20 Hz");
        Step(slow,100); check(slow.Economies[0].Ore == 326,"fractional harvest accumulates into a delivered unit");

        var interrupted = Fixture(1); interrupted.Enqueue(0,[0],OrderKind.Gather,target: 7);
        for (int i = 0; i < 500 && interrupted.Resources[7].CanGather; i++) interrupted.Step();
        interrupted.Enqueue(0,[0],OrderKind.Stop); interrupted.Step();
        check(interrupted.Units[0].CargoMilli == 1000 && !interrupted.Resources[7].CanGather,"stop can preserve a final load after exhaustion");
        interrupted.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(interrupted,500);
        check(interrupted.Economies[0].Ore == 326 && interrupted.Units[0].CargoMilli == 0,"exhausted source can resume delivery of preserved final cargo");

        var inaccessible = Fixture(3,barrier: true);
        inaccessible.Enqueue(0,[0],OrderKind.Gather,target: 7); Step(inaccessible,400);
        check(inaccessible.Units[0].CargoMilli == 0 && inaccessible.Resources[7].RemainingMilli == 3000
            && inaccessible.Economies[0].Ore == 325,"unreachable sources cannot remotely generate income");
    }
}
