using Reforged.Input;
using Reforged.Simulation;
using System.Text.Json;
using Reforged.Presentation;

static class ExtendedOrderChecks
{
    private static SimWorld Open() => new(new(40,24,1000,Enumerable.Repeat(true,960).ToArray(),new int[1025]));
    private static void Advance(SimWorld world,int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Step();
            foreach (var a in world.Units.Where(u => u.Health > 0))
            foreach (var b in world.Units.Where(u => u.Id > a.Id && u.Health > 0))
                if (SimWorld.SeparationSquared(a,b) < 700L*700) throw new Exception("extended orders overlapped units");
        }
    }

    public static void Run(Action<bool,string> check)
    {
        var groups = new ControlGroups(); var living = new HashSet<int> { 1,2,3,4 };
        groups.Assign(3,[1,2,2,8,-1],living.Contains);
        check(groups.Recall(3,living.Contains).SequenceEqual([1,2]),"control groups filter invalid foreign and duplicate IDs");
        groups.Assign(3,[3],living.Contains,append:true);
        check(groups.Recall(3,living.Contains).SequenceEqual([1,2,3]),"shift assignment extends the stored group");
        groups.Assign(9,[4],living.Contains);
        check(groups.Recall(9,living.Contains).SequenceEqual([4]),"the zero key has its own tenth group");
        living.Remove(2);
        check(groups.Recall(3,living.Contains).SequenceEqual([1,3]),"recalling a group prunes dead units");
        var copy = groups.Recall(3,living.Contains); copy[0] = 99;
        check(groups.Recall(3,living.Contains).SequenceEqual([1,3]),"selection cannot mutate stored group membership");
        groups.Assign(3,[],living.Contains);
        check(groups.Recall(3,living.Contains).Length == 0 && groups.Recall(9,living.Contains).Length == 1,"empty assignment clears only that group");

        var queue = Open(); var marcher = queue.Spawn(0,10*40+3);
        queue.Enqueue(0,[0],OrderKind.Move,10*40+12);
        queue.Enqueue(0,[0],OrderKind.Move,18*40+12,queued:true);
        queue.Enqueue(0,[0],OrderKind.Move,18*40+25,queued:true); Advance(queue,1);
        check(marcher.OrderDestination == 412 && marcher.OrderQueue.Count == 2,"queued waypoints preserve the active destination");
        var marching = UnitOrderReadout.From(queue,marcher,_ => "Probe");
        check(marching is { Order: "Move",Distance: "8.9 m",Activity: "Running",Queue: "Then Move → Move" },$"the console reads a move, its distance, the pace and the queue ({marching})");
        bool first = false,second = false;
        for (int tick = 0; tick < 350; tick++) { Advance(queue,1); first |= marcher.Node == 412; second |= marcher.Node == 732; }
        check(first && second && marcher.Node == 745 && marcher.OrderQueue.Count == 0 && marcher.Order == OrderKind.Idle,"queued waypoints execute in order and finish");
        var replay = Open(); replay.Spawn(0,403);
        replay.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(queue.Commands))!); Advance(replay,queue.Tick);
        check(replay.StateDigest() == queue.StateDigest(),"queued command logs serialize and replay identically");

        var squad = Open(); foreach (int node in new[] {403,405,483,485}) squad.Spawn(0,node);
        squad.Enqueue(0,[0,1,2,3],OrderKind.Move,415); squad.Enqueue(0,[0,1,2,3],OrderKind.Move,735,queued:true); Advance(squad,1);
        check(squad.Units.Select(u => u.OrderQueue.Single().Cell).Distinct().Count() == 4,"queued squad orders reserve distinct formation destinations");
        Advance(squad,350);
        check(squad.Units.All(u => u.Order == OrderKind.Idle && u.OrderQueue.Count == 0),"a queued squad move completes without piling onto one cell");
        squad.Enqueue(0,[0,1,2,3],OrderKind.Move,410); squad.Enqueue(0,[0,1,2,3],OrderKind.Move,710,queued:true); Advance(squad,1);
        squad.Enqueue(0,[0,1,2,3],OrderKind.Stop); Advance(squad,1);
        check(squad.Units.All(u => u.OrderQueue.Count == 0 && u.Order == OrderKind.Idle),"stop cancels the entire queued route and returns to idle");
        squad.Enqueue(0,[0],OrderKind.Move,733,queued:true); Advance(squad,1);
        check(squad.Units[0].Order == OrderKind.Move && squad.Units[0].OrderQueue.Count == 0,"shift move from a stopped unit starts immediately");

        var patrol = Open(); var scout = patrol.Spawn(0,10*40+3);
        patrol.Enqueue(0,[0],OrderKind.Patrol,10*40+18); Advance(patrol,1);
        int start = scout.PatrolStart,end = scout.PatrolEnd; bool outward = false,home = false;
        for (int tick = 0; tick < 320; tick++) { Advance(patrol,1); outward |= scout.Node == end; home |= outward && scout.Node == start; }
        check(outward && home && scout.Order == OrderKind.Patrol,"patrol reverses between both endpoints and remains active");
        var enemy = patrol.Spawn(2,11*40+12); enemy.Health = 12; enemy.Cooldown = 10000; scout.Health = 10000;
        Advance(patrol,250);
        check(enemy.Health == 0 && scout.Order == OrderKind.Patrol && scout.PatrolStart == start && scout.PatrolEnd == end,"patrol fights and resumes its saved route");
        patrol.Enqueue(0,[0],OrderKind.HoldGround); Advance(patrol,1); var held = (scout.X,scout.Z); Advance(patrol,30);
        check((scout.X,scout.Z) == held && scout.Order == OrderKind.HoldGround,"hold replaces and cancels a repeating patrol");

        var guard = Open(); var sentry = guard.Spawn(0,403); sentry.Health = 10000;
        var ward = guard.Spawn(0,410); ward.Health = 10000;
        guard.Enqueue(0,[sentry.Id],OrderKind.Guard,ward.Node,ward.Id); Advance(guard,90);
        check(sentry.Order == OrderKind.Guard && sentry.OrderTarget == ward.Id && SimWorld.SeparationSquared(sentry,ward) <= 4000L*4000,"guard approaches its friendly ward without occupying its cell");
        check(UnitOrderReadout.From(guard,sentry,_ => "Probe") is { Order: "Guard → Probe #2",Queue: "" } guarding && guarding.Distance.EndsWith(" m")
            && UnitOrderReadout.From(guard,ward,_ => "Probe") is { Order: "Idle",Activity: "Standing",Distance: "" },
            $"the console names a guard's ward by its number ({UnitOrderReadout.From(guard,sentry,_ => "Probe")})");
        guard.Enqueue(0,[ward.Id],OrderKind.Move,425); Advance(guard,220);
        check(sentry.Order == OrderKind.Guard && SimWorld.SeparationSquared(sentry,ward) <= 4000L*4000,"guard follows a moving friendly unit");
        var intruder = guard.Spawn(2,11*40+26); intruder.Health = 12; intruder.Cooldown = 10000;
        Advance(guard,150);
        check(intruder.Health == 0 && sentry.Order == OrderKind.Guard && sentry.OrderTarget == ward.Id,"guard defends its ward and retains the guard intent");
        ward.Health = 0; Advance(guard,1);
        check(sentry.Order == OrderKind.Idle && sentry.Target < 0,"guard ends safely when its ward dies");

        var fixedGuard = Open(); var watch = fixedGuard.Spawn(0,403); watch.Health = 10000;
        fixedGuard.Enqueue(0,[0],OrderKind.Guard,415); Advance(fixedGuard,150);
        int anchor = watch.GuardOrigin;
        var runner = fixedGuard.Spawn(2,457); runner.Health = 10000; runner.Cooldown = 10000;
        Advance(fixedGuard,5); fixedGuard.Enqueue(2,[runner.Id],OrderKind.Move,438); Advance(fixedGuard,200);
        var (ax,az) = fixedGuard.Navigation.Center(anchor);
        check(watch.Order == OrderKind.Guard && watch.GuardOrigin == anchor && SimWorld.DistanceSquared(watch.X,watch.Z,ax,az) <= 4000L*4000,
            "a location guard respects its leash and returns to the original location");

        var forced = Open(); var attacker = forced.Spawn(0,403); var ally = forced.Spawn(0,410);
        attacker.Health = 10000; ally.Cooldown = 10000;
        forced.Enqueue(0,[0],OrderKind.ForceAttack,ally.Node,ally.Id); Advance(forced,120);
        check(ally.Health < 100,"force attack can damage an explicitly targeted friendly unit");
        check(UnitOrderReadout.From(forced,attacker,_ => "Probe") is { Order: "Force Attack → Probe #2" } striking
            && (striking.Activity.StartsWith("Striking Probe #2") || striking.Activity.StartsWith("Fighting Probe #2")),
            $"the console shows whom a force attack is hitting ({UnitOrderReadout.From(forced,attacker,_ => "Probe")})");
        check(ally.Target < 0 && ally.Order == OrderKind.Idle,"a force-attacked friendly unit does not turn on its attacker");
        forced.Enqueue(0,[0],OrderKind.Stop); Advance(forced,1);
        check(attacker.Target < 0 && attacker.Order == OrderKind.Idle,"stop cancels a friendly force attack and returns to idle");
        forced.Enqueue(0,[0],OrderKind.ForceAttack,420); Advance(forced,150);
        check(attacker.Node == 420 && attacker.Order == OrderKind.ForceAttack && attacker.Target < 0 && attacker.Goal < 0,"force attack at a location advances and holds without inventing ground damage");

        var controls = new OrderInput(); controls.Begin(OrderKind.Guard);
        check(!controls.Resolve(7,1,out _) && controls.Pending == OrderKind.Guard,"guard targeting rejects enemies and stays armed");
        controls.Resolve(7,-1,out var guarded,3);
        check(guarded == new PlayerOrder(OrderKind.Guard,7,3),"guard targeting records a friendly unit");
        controls.Begin(OrderKind.Move); controls.Resolve(10,-1,out _,keepTargeting:true);
        check(controls.Pending == OrderKind.Move,"shift targeted waypoints keep the command armed");
        controls.Begin(OrderKind.ForceAttack); controls.Resolve(7,-1,out var forceAlly,3);
        check(forceAlly.Target == 3,"force attack targeting accepts friendly units");
        foreach (var (width,height) in new[] { (1280,800),(1920,1080),(2560,1440),(3840,2160),(3440,1440),(5120,1440),(800,1000) })
        {
            var layout = HudLayout.Fit(width,height);
            check(layout.Left >= 0 && layout.Top >= 0 && layout.Left+layout.Width <= width+.001
                && Math.Abs(layout.Top+layout.Height-height) < .001 && Math.Abs(layout.Width/layout.Height-1024.0/212) < .001,
                $"HUD fits {width}x{height} without distorting source artwork");
        }
        check(HudLayout.Fit(5120,1440).Scale == HudLayout.Fit(2560,1440).Scale,"ultrawide HUD size follows height instead of growing with screen width");
        check(HudLayout.Fit(3840,2160,75).Scale < HudLayout.Fit(3840,2160).Scale
            && HudLayout.Fit(3840,2160,150).Scale > HudLayout.Fit(3840,2160).Scale,"large screen HUD supports user size adjustment");
    }
}
