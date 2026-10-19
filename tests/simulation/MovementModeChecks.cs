using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

static class MovementModeChecks
{
    private static SimWorld Open() => new(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));
    private static void Advance(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    public static void Run(Action<bool,string> check)
    {
        var world = Open(); var unit = world.Spawn(0,403);
        world.Enqueue(0,[0],OrderKind.Move,425); world.Step();
        check(unit.X == 3650 && !unit.Walking,"running retains the accepted 150 mm per tick baseline");
        world.Enqueue(0,[0],OrderKind.Move,625,queued:true); world.Step();
        int x = unit.X; var path = unit.Path.ToArray(); int goal = unit.Goal;
        world.Enqueue(0,[0],OrderKind.ToggleWalkRun); world.Step();
        check(unit.Walking && unit.X == x+75 && unit.Order == OrderKind.Move && unit.Goal == goal
            && unit.Path.SequenceEqual(path) && unit.OrderQueue.Count == 1,"walking changes pace next tick while preserving active and queued routes");
        var first = unit.Movement[0]; var last = unit.Movement[^1];
        var mid = MovementPlayback.Sample(unit.Movement,.5);
        check(last.Elapsed == SimWorld.MovementTickSpan && Math.Abs(mid.X-(first.X+last.X)/2d) < .001,
            "walking interpolation spans the full tick instead of arriving at half time");
        world.Enqueue(0,[0],OrderKind.ToggleWalkRun); x = unit.X; world.Step();
        check(!unit.Walking && unit.X == x+150,"second toggle restores baseline running without a position jump");
        world.Enqueue(0,[0],OrderKind.ToggleWalkRun); world.Enqueue(0,[0],OrderKind.ToggleWalkRun); world.Step();
        check(!unit.Walking,"multiple paused toggles apply in recorded sequence");

        var mixed = Open(); var a = mixed.Spawn(0,403); var b = mixed.Spawn(0,405);
        var dead = mixed.Spawn(0,407); dead.Health = 0; var enemy = mixed.Spawn(1,409);
        mixed.Enqueue(0,[a.Id],OrderKind.ToggleWalkRun); mixed.Step();
        mixed.Enqueue(0,[-1,a.Id,a.Id,b.Id,dead.Id,enemy.Id,999],OrderKind.ToggleWalkRun); mixed.Step();
        check(a.Walking && b.Walking && !dead.Walking && !enemy.Walking,"mixed selection walks together and ignores dead foreign invalid duplicate IDs");
        mixed.Enqueue(0,[a.Id,b.Id],OrderKind.ToggleWalkRun); mixed.Step();
        check(!a.Walking && !b.Walking,"an entirely walking selection switches together to running");
        mixed.Enqueue(0,[],OrderKind.ToggleWalkRun); mixed.Step();
        check(!a.Walking && !b.Walking,"empty pace commands change no unit");

        var fight = Open(); var attacker = fight.Spawn(0,403,UnitDefinition.Probe with {
            Attacks = [new("swing",1,20,[new(8,1000000)])] });
        fight.Spawn(1,405).Cooldown = 10000; fight.Enqueue(0,[0],OrderKind.Attack,target:1); fight.Step();
        int start = attacker.AttackStartTick,serial = attacker.AttackSerial;
        fight.Enqueue(0,[0],OrderKind.ToggleWalkRun); fight.Step();
        check(attacker.Walking && attacker.AttackStartTick == start && attacker.AttackSerial == serial
            && fight.CombatEvents.All(e => e.Kind != CombatEventKind.Cancel),"pace change preserves the current attack windup");
        Advance(fight,7); check(fight.Units[1].Health < 100,"pace change cannot delay a pending hit");

        var deck = new SimWorld(new(5,1,1000,[true,false,false,false,true],new int[12],[
            new(0,0,[0,6]),new(1,500,[5,7]),new(2,1000,[6,8]),new(3,500,[7,9]),new(4,0,[8,4])]));
        var bridge = deck.Spawn(0,0); deck.Enqueue(0,[0],OrderKind.Move,4); Advance(deck,5);
        int before = bridge.X; deck.Enqueue(0,[0],OrderKind.ToggleWalkRun); deck.Step();
        check(bridge.Walking && bridge.X-before == 75 && bridge.TransitNode >= 0,"walking preserves an in-flight deck reservation");
        mid = MovementPlayback.Sample(bridge.Movement,.5);
        check(Math.Abs(mid.X-(bridge.Movement[0].X+bridge.Movement[^1].X)/2d) < .001,"deck walking uses the same interpolation clock");
        Advance(deck,130); check(bridge.Node == 4 && bridge.Goal < 0 && bridge.Walking,"walking reaches the far side of a layered route safely");

        var custom = Open(); var slow = custom.Spawn(0,403,UnitDefinition.Probe with { WalkScale = 400000 });
        custom.Enqueue(0,[0],OrderKind.ToggleWalkRun); custom.Enqueue(0,[0],OrderKind.Move,425); custom.Step();
        check(slow.X == 3560,"walk distance uses the unit's imported scale");
        var arrival = Open(); var arriving = arrival.Spawn(0,403,UnitDefinition.Probe with { WalkScale = 400000 });
        arrival.Enqueue(0,[0],OrderKind.ToggleWalkRun); arrival.Enqueue(0,[0],OrderKind.Move,404); Advance(arrival,17);
        check(arriving.X == 4500 && arriving.Movement[^1].Elapsed == 100000 && MovementPlayback.Sample(arriving.Movement,.8).X == 4500,
            "an early walking arrival holds its destination for the rest of the tick");

        var squad = Open(); int[] scales = [364585,469073,432872,469073,432872,364585];
        foreach (int cell in new[] {403,405,407,483,485,487})
            squad.Spawn(0,cell,UnitDefinition.Probe with { WalkScale = scales[squad.Units.Count] });
        squad.Enqueue(0,Enumerable.Range(0,6),OrderKind.ToggleWalkRun); squad.Enqueue(0,Enumerable.Range(0,6),OrderKind.Move,425);
        bool separated = true;
        for (int tick = 0; tick < 600; tick++)
        {
            squad.Step();
            foreach (var left in squad.Units) foreach (var right in squad.Units.Where(u => u.Id > left.Id))
                foreach (double fraction in new[] {.25,.5,.75,1})
                {
                    var p = MovementPlayback.Sample(left.Movement,fraction); var q = MovementPlayback.Sample(right.Movement,fraction);
                    separated &= (p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z) >= 700L*700-2;
                }
        }
        check(separated && squad.Units.All(u => u.Walking && u.Goal < 0 && u.Order == OrderKind.Idle),
            "a squad with different walking ratios arrives without body overlap between ticks");
        var replay = Open(); replay.Spawn(0,403);
        replay.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(world.Commands))!); Advance(replay,world.Tick);
        check(replay.StateDigest() == world.StateDigest(),"pace changes serialize and replay with all orders and queues");
        string digest = custom.StateDigest(); slow.Walking = false;
        check(digest != custom.StateDigest(),"movement mode participates in replay state");
        foreach (int scale in new[] {0,-1,1000001})
        {
            try { (UnitDefinition.Probe with { WalkScale = scale }).Validate(); check(false,"invalid walk scale must fail"); }
            catch (ArgumentException) { check(true,"invalid walk scale rejected"); }
        }
    }
}
