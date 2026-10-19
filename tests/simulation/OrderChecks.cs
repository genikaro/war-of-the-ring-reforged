using Reforged.Input;
using Reforged.Simulation;
using System.Text.Json;

static class OrderChecks
{
    private static SimWorld Open()
        => new(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));

    private static void Advance(SimWorld world,int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Step();
            foreach (var unit in world.Units.Where(u => u.Health > 0))
            foreach (var other in world.Units.Where(u => u.Health > 0 && u.Id > unit.Id))
                if (SimWorld.SeparationSquared(unit,other) < 700L*700) throw new Exception("orders overlapped units");
        }
    }

    private static (SimWorld World,SimUnit Unit,SimUnit Enemy) Fight()
    {
        var world = Open(); var unit = world.Spawn(0,10*40+4); var enemy = world.Spawn(2,10*40+12);
        unit.Health = enemy.Health = 10000; enemy.Cooldown = 10000;
        world.Enqueue(0,[unit.Id],OrderKind.Attack,enemy.Node,enemy.Id); Advance(world,80);
        return (world,unit,enemy);
    }

    public static void Run(Action<bool,string> check)
    {
        var (world,unit,enemy) = Fight();
        check(unit.Attacking && unit.Order == OrderKind.Attack,"explicit attack owns pursuit independently from the route");
        world.Enqueue(0,[unit.Id],OrderKind.Stop); Advance(world,1);
        check(unit.Order == OrderKind.Stop && unit.Target < 0 && unit.Goal < 0 && unit.Path.Count == 0 && !unit.Attacking,
            "stop clears combat and pursuit at the next fixed tick");
        var stopped = (unit.X,unit.Y,unit.Z); int health = enemy.Health;
        Advance(world,40);
        check((unit.X,unit.Y,unit.Z) == stopped && enemy.Health == health,"stop stays still and does not proactively restart combat");

        world.Enqueue(0,[unit.Id],OrderKind.HoldGround); Advance(world,1);
        check(unit.Attacking && unit.Order == OrderKind.HoldGround,"hold ground engages an enemy already within reach");
        world.Enqueue(2,[enemy.Id],OrderKind.Move,10*40+25); Advance(world,140);
        check((unit.X,unit.Y,unit.Z) == stopped && unit.Target < 0 && !unit.Moving && unit.Order == OrderKind.HoldGround,
            "hold ground releases an escaping enemy without chasing");
        world.Enqueue(0,[unit.Id],OrderKind.Attack,enemy.Node,enemy.Id); Advance(world,1);
        check(unit.Order == OrderKind.Attack && unit.Target == enemy.Id && unit.Goal >= 0,"attack replaces hold and pursues a distant enemy");

        var force = Open(); var runner = force.Spawn(0,10*40+5); var foe = force.Spawn(2,11*40+7);
        runner.Health = foe.Health = 10000;
        force.Enqueue(0,[runner.Id],OrderKind.Move,10*40+30);
        force.Enqueue(2,[foe.Id],OrderKind.Attack,runner.Node,runner.Id); Advance(force,45);
        check(runner.Health < 10000 && runner.Order == OrderKind.Move && runner.Target < 0 && runner.OrderDestination == 10*40+30,
            "move ignores damage and retains its player destination while travelling");

        var march = Open(); var marcher = march.Spawn(0,10*40+4); marcher.Health = 10000;
        var first = march.Spawn(2,10*40+12); first.Health = 12; first.Cooldown = 10000;
        var second = march.Spawn(2,11*40+14); second.Health = 12; second.Cooldown = 10000;
        int destination = 10*40+29;
        march.Enqueue(0,[marcher.Id],OrderKind.AttackMove,destination); Advance(march,1);
        check(marcher.Target == first.Id && marcher.Order == OrderKind.AttackMove && marcher.OrderDestination == destination,
            "attack move keeps a destination distinct from its combat approach");
        Advance(march,400);
        check(first.Health == 0 && second.Health == 0,"attack move engages multiple nearby enemies");
        check(marcher.Node == destination && marcher.Goal < 0 && marcher.Order == OrderKind.Idle && marcher.OrderDestination < 0,
            "attack move resumes after the last enemy dies and completes its destination");

        var replaced = Open(); var replacement = replaced.Spawn(0,10*40+4); replacement.Health = 10000;
        var target = replaced.Spawn(2,10*40+12); target.Health = 10000; target.Cooldown = 10000;
        replaced.Enqueue(0,[0],OrderKind.AttackMove,destination); Advance(replaced,20);
        replaced.Enqueue(0,[0],OrderKind.Move,2*40+4); Advance(replaced,250);
        check(replacement.Node == 2*40+4 && replacement.Target < 0 && replacement.OrderDestination < 0,
            "replacement move discards the suspended attack move destination");

        var dead = Open(); var alive = dead.Spawn(0,81); var fallen = dead.Spawn(0,83); var foreign = dead.Spawn(2,85);
        fallen.Health = 0;
        dead.Enqueue(0,[-1,alive.Id,alive.Id,fallen.Id,foreign.Id,999],OrderKind.HoldGround); Advance(dead,1);
        check(alive.Order == OrderKind.HoldGround && fallen.Order == OrderKind.Idle && foreign.Order == OrderKind.Idle,
            "mixed selections ignore dead foreign invalid and duplicate IDs");
        dead.Enqueue(0,[alive.Id],OrderKind.Attack,foreign.Node,fallen.Id); Advance(dead,1);
        check(alive.Order == OrderKind.HoldGround,"a dead target cannot replace a valid order");
        dead.Enqueue(0,[alive.Id],OrderKind.Attack,alive.Node,alive.Id); Advance(dead,1);
        check(alive.Order == OrderKind.HoldGround,"a friendly attack target cannot replace a valid order");
        dead.Enqueue(0,[alive.Id],OrderKind.Move,-1); Advance(dead,1);
        check(alive.Order == OrderKind.Idle && alive.Goal < 0,"unreachable move clears its intent cleanly");

        var moving = Open(); var walker = moving.Spawn(0,81);
        moving.Enqueue(0,[0],OrderKind.Move,110); Advance(moving,3);
        var beforeStop = (walker.X,walker.Z);
        moving.Enqueue(0,[0],OrderKind.Stop); Advance(moving,10);
        check((walker.X,walker.Z) == beforeStop && !walker.Moving,"stop freezes continuous movement without a return-to-center jump");

        var deck = new SimWorld(new(5,1,1000,[true,false,false,false,true],new int[12],[
            new(0,0,[0,6]),new(1,500,[5,7]),new(2,1000,[6,8]),new(3,500,[7,9]),new(4,0,[8,4])]));
        var bridge = deck.Spawn(0,0); deck.Enqueue(0,[0],OrderKind.Move,4); Advance(deck,5);
        var bridgeStop = (bridge.X,bridge.Y,bridge.Z);
        deck.Enqueue(0,[0],OrderKind.HoldGround); Advance(deck,20);
        check((bridge.X,bridge.Y,bridge.Z) == bridgeStop,"hold ground stops safely during a constrained deck segment");
        deck.Enqueue(0,[0],OrderKind.Move,4); Advance(deck,120);
        check(bridge.Node == 4 && bridge.Goal < 0,"a replacement move safely resumes after a mid-deck hold");

        var input = new OrderInput();
        input.Begin(OrderKind.Attack);
        check(!input.Resolve(-1,-1,out _) && input.Pending == OrderKind.Attack,"invalid attack clicks retain targeting");
        input.Cancel(); check(input.Pending == null,"targeting cancellation changes no simulation orders");
        input.Begin(OrderKind.Attack); input.Resolve(20,7,out var attackEnemy);
        check(attackEnemy == new PlayerOrder(OrderKind.Attack,20,7),"unified Attack targets an enemy with a normal pursuit order");
        input.Begin(OrderKind.Attack); input.Resolve(20,-1,out var attackOther,4);
        check(attackOther == new PlayerOrder(OrderKind.ForceAttack,20,4),"unified Attack force attacks a friendly or neutral target");
        input.Begin(OrderKind.Attack); input.Resolve(20,-1,out var attackGround,keepTargeting:true);
        check(attackGround == new PlayerOrder(OrderKind.AttackMove,20) && input.Pending == OrderKind.Attack,
            "unified Attack on ground attack moves and Shift keeps unified targeting armed");
        var neutralFight = Open(); neutralFight.Spawn(0,403); var neutral = neutralFight.Spawn(-1,410); neutral.Cooldown = 10000;
        input.Resolve(neutral.Node,-1,out var attackNeutral,neutral.Id);
        neutralFight.Enqueue(0,[0],attackNeutral.Kind,attackNeutral.Cell,attackNeutral.Target); Advance(neutralFight,80);
        check(neutral.Health < 100,"unified Attack delivers damage to an explicitly targeted neutral unit");
        input.Begin(OrderKind.Move); input.Resolve(20,7,out var forced);
        check(forced == new PlayerOrder(OrderKind.Move,20),"move targeting over an enemy remains a move");

        SimWorld ViaInput(bool armed,bool explicitMove = false)
        {
            var replay = Open(); replay.Spawn(0,81); replay.Spawn(2,92).Cooldown = 10000;
            var controls = new OrderInput();
            if (armed) controls.Begin(explicitMove ? OrderKind.Move : OrderKind.Attack);
            controls.Resolve(explicitMove ? 100 : 92,explicitMove ? -1 : 1,out var order);
            replay.Enqueue(0,[0],order.Kind,order.Cell,order.Target); Advance(replay,50); return replay;
        }
        var panelAttack = ViaInput(true); var contextualAttack = ViaInput(false);
        check(panelAttack.StateDigest() == contextualAttack.StateDigest()
            && JsonSerializer.Serialize(panelAttack.Commands) == JsonSerializer.Serialize(contextualAttack.Commands),
            "armed panel/hotkey and contextual attack produce identical command logs and replay state");
        var panelMove = ViaInput(true,true); var contextualMove = ViaInput(false,true);
        check(panelMove.StateDigest() == contextualMove.StateDigest(),"armed and contextual moves replay identically");

        var playback = Open(); playback.Spawn(0,10*40+4).Health = 10000;
        playback.Spawn(2,10*40+12).Health = 12; playback.Units[1].Cooldown = 10000;
        playback.Spawn(2,11*40+14).Health = 12; playback.Units[2].Cooldown = 10000;
        var serialized = JsonSerializer.Serialize(march.Commands);
        playback.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(serialized)!); Advance(playback,march.Tick);
        check(playback.StateDigest() == march.StateDigest(),"serialized explicit order logs replay identically including suspended destinations");
        playback.Enqueue(0,[0],OrderKind.HoldGround);
        check(playback.Commands.Last().Sequence > playback.Commands[^2].Sequence,"new orders retain sequence ordering after log playback");
    }
}
