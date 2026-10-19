using Reforged.Simulation;
using Reforged.Presentation;

static class CombatChecks
{
    private static SimWorld Open(int width = 30, int height = 18)
        => new(new(width,height,1000,Enumerable.Repeat(true,width*height).ToArray(),new int[(width+1)*(height+1)]));

    private static void Safe(SimWorld world)
    {
        foreach (var unit in world.Units.Where(u => u.Health > 0))
        {
            if (!world.Navigation.CanStand(unit.Node)) throw new Exception("combat entered blocked navigation");
            foreach (var other in world.Units.Where(u => u.Id > unit.Id && u.Health > 0))
            {
                if (SimWorld.SeparationSquared(unit,other) < 700L*700) throw new Exception("combat bodies overlapped");
                foreach (double fraction in new[] { .25,.5,.75 })
                {
                    var a = MovementPlayback.Sample(unit.Movement,fraction); var b = MovementPlayback.Sample(other.Movement,fraction);
                    if ((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z) < 700L*700-2)
                        throw new Exception("combat bodies overlapped between ticks");
                }
            }
        }
    }

    private static SimWorld Surround(int count)
    {
        var world = Open();
        for (int i = 0; i < count; i++) world.Spawn(0,(3+i/4*2)*30+3+i%4*2).Health = 10000;
        var target = world.Spawn(2,8*30+17); target.Health = 10000;
        world.Enqueue(0,Enumerable.Range(0,count),target.Node,target.Id);
        return world;
    }

    public static void Run(Action<bool,string> check)
    {
        var ring = Surround(6); int settledMoves = 0;
        for (int tick = 0; tick < 500; tick++)
        {
            ring.Step(); Safe(ring);
            if (tick >= 400) settledMoves += ring.Units.Take(6).Count(u => u.Moving);
        }
        if (ring.Units.Take(6).Any(u => !u.Attacking))
            Console.WriteLine("COMBAT_RING " + string.Join(";",ring.Units.Select(u => $"{u.Id} ({u.X},{u.Z}) target{u.Target} slot{u.AttackNode} waiting{u.CombatWaiting} attacking{u.Attacking} blocked{u.BlockedTicks}")));
        check(ring.Units.Take(6).All(u => u.Attacking),"six attackers spread into reachable melee positions");
        check(ring.Units[6].Health < 10000,"assigned combat positions still deliver damage");
        check(settledMoves == 0,"settled attackers hold their positions instead of circling or bumping");
        check(ring.Units.Take(6).All(u => ring.Units.Take(6).Where(o => o.Id != u.Id)
            .All(o => SimWorld.SeparationSquared(u,o) >= 1100L*1100)),"settled allies keep combat spacing");
        var replay = Surround(6);
        for (int tick = 0; tick < 500; tick++) replay.Step();
        check(ring.StateDigest() == replay.StateDigest(),"combat position selection replays deterministically");

        var mobile = Surround(6);
        var runner = mobile.Units[6]; runner.Cooldown = 10000;
        mobile.Enqueue(2,[runner.Id],8*30+24);
        for (int tick = 0; tick < 400; tick++)
        {
            mobile.Step(); Safe(mobile);
        }
        if (runner.X != 24500 || mobile.Units.Take(6).Any(u => !u.Attacking))
            Console.WriteLine("MOBILE " + string.Join(";",mobile.Units.Select(u => $"{u.Id} ({u.X},{u.Z}) node{u.Node}/{u.TransitNode} goal{u.Goal} target{u.Target} slot{u.AttackNode} wait{u.CombatWaiting} attacking{u.Attacking} path{string.Join(',',u.Path)}:{u.Waypoint} blocked{u.BlockedTicks}")));
        check(runner.X == 24500 && mobile.Units.Take(6).All(u => u.Attacking),"attack positions follow a moving target and settle again");

        var changing = new SimWorld(new(30,18,1000,Enumerable.Repeat(true,540).ToArray(),new int[589],null,[new(12,[255],[])]));
        changing.EnqueueObstacle(12,false); changing.Step();
        changing.Spawn(0,8*30+3).Health = 10000; changing.Spawn(2,8*30+17).Health = 10000;
        changing.Enqueue(0,[0],257,1); changing.Step();
        changing.EnqueueObstacle(12,true);
        for (int tick = 0; tick < 250; tick++) { changing.Step(); Safe(changing); }
        check(changing.Navigation.IsObstacleActive(12) && changing.Units[0].Attacking && changing.Units[0].AttackNode != 255,
            "an obstacle invalidates an approach position and combat selects another reachable slot");

        var crowded = Surround(14);
        int waitingMoves = 0;
        for (int tick = 0; tick < 500; tick++)
        {
            crowded.Step(); Safe(crowded);
            if (tick >= 400) waitingMoves += crowded.Units.Take(14).Count(u => u.CombatWaiting && u.Moving);
        }
        check(crowded.Units.Take(14).Count(u => u.Attacking) >= 4,"a crowded target still has active attackers");
        check(crowded.Units.Take(14).Any(u => u.CombatWaiting),"extra attackers wait outside the occupied melee ring");
        check(waitingMoves == 0,"waiting attackers settle outside a full melee ring without shuffling");
        check(crowded.Units.Take(14).All(u => u.Goal != crowded.Units[14].Node),"attackers never route into the target's occupied cell");

        var wall = Enumerable.Repeat(true,25).ToArray();
        for (int z = 0; z < 5; z++) wall[z*5+2] = false;
        var sealedFight = new SimWorld(new(5,5,1000,wall,new int[36]));
        sealedFight.Spawn(0,6); sealedFight.Spawn(2,8); sealedFight.Enqueue(0,[0],8,1);
        for (int tick = 0; tick < 100; tick++) { sealedFight.Step(); Safe(sealedFight); }
        check(sealedFight.Units.All(u => u.Health == 100),"melee cannot strike through a sealed wall");

        var floors = new SimWorld(new(1,1,1000,[true],new int[4],[new(0,1000,[])]));
        floors.Spawn(0,0); floors.Spawn(1,1); floors.Enqueue(0,[0],1,1);
        for (int tick = 0; tick < 80; tick++) floors.Step();
        check(floors.Units.All(u => u.Health == 100),"nearby disconnected floors cannot melee through the deck");

        var switching = Open(); var attacker = switching.Spawn(0,8*30+3);
        var first = switching.Spawn(2,8*30+10); first.Health = 12;
        var second = switching.Spawn(2,9*30+12);
        switching.Enqueue(0,[0],first.Node,first.Id);
        for (int tick = 0; tick < 400; tick++) { switching.Step(); Safe(switching); }
        check(first.Health == 0 && second.Health < 100,"an attack order continues onto a nearby enemy after its target dies");

        ring.Units[6].Cooldown = 10000;
        ring.Enqueue(0,Enumerable.Range(0,6),2*30+2); ring.Step(); Safe(ring);
        check(ring.Units.Take(6).All(u => u.Target < 0 && u.AttackNode < 0 && !u.Attacking),"a move order releases combat positions immediately");
    }
}
