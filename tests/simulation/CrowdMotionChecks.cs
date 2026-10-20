using Reforged.Simulation;
using Reforged.Presentation;
using System.Text;

static class CrowdMotionChecks
{
    public static SimWorld Crowd(OrderKind kind)
    {
        var world=new SimWorld(new(48,32,1000,Enumerable.Repeat(true,48*32).ToArray(),new int[49*33]));
        var body=UnitDefinition.Probe with {BodyRadiusMm=927,BodyHeightMm=2534,AttackGapMm=500,MaxAttackers=8};
        for (int row=0;row<6;row++) for (int col=0;col<3;col++) world.Spawn(0,(5+row*2)*48+4+col*2,body);
        world.Enqueue(0,world.Units.Select(u=>u.Id),kind,14*48+32);
        return world;
    }

    public sealed record Metrics(string Kind,int Arrived,int ArrivalTick,int Reversals,int MovingTurns,double MaxWidthM,int PostArrivalMotion,bool Safe,string Trajectory);

    public static Metrics Measure(OrderKind kind)
    {
        var world=Crowd(kind); var last=new (int X,int Z,int Goal)[18]; int flips=0,turns=0,firstArrival=-1;
        double maxWidth=0; int movingAfterArrival=0;
        bool safe=true; var trajectory=new StringBuilder();
        for (int tick=0;tick<900;tick++)
        {
            var before=world.Units.Select(u=>(u.X,u.Z,u.Goal)).ToArray(); world.Step();
            foreach (var u in world.Units)
            {
                int dx=u.X-before[u.Id].X,dz=u.Z-before[u.Id].Z; var old=last[u.Id];
                long distance=(long)dx*dx+(long)dz*dz,previous=(long)old.X*old.X+(long)old.Z*old.Z;
                if (u.Goal==old.Goal && distance>2500 && previous>2500)
                {
                    turns++; if ((long)dx*old.X+(long)dz*old.Z<0)
                    {
                        flips++;
                    }
                }
                last[u.Id]=(dx,dz,u.Goal);
                trajectory.Append($"{u.X},{u.Z};");
                foreach (var other in world.Units.Where(o=>o.Id>u.Id)) safe &= !SimWorld.BodiesOverlap(u,other);
            }
            maxWidth=Math.Max(maxWidth,(world.Units.Max(u=>u.Z)-world.Units.Min(u=>u.Z))/1000d);
            if (world.Units.All(u=>u.Goal<0) && firstArrival<0) firstArrival=world.Tick;
            if (firstArrival>=0 && world.Tick>firstArrival) movingAfterArrival+=world.Units.Count(u=>u.Moving);
        }
        return new(kind.ToString(),world.Units.Count(u=>u.Goal<0),firstArrival,flips,turns,maxWidth,movingAfterArrival,safe,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(trajectory.ToString()))));
    }

    public static void Run(Action<bool,string> check)
    {
        var move=Measure(OrderKind.Move); var march=Measure(OrderKind.AttackMove);
        check(move.Arrived==18 && march.Arrived==18 && move.ArrivalTick<400 && march.ArrivalTick<400,"large Move and Attack-move formations both arrive promptly");
        check(move.Trajectory==march.Trajectory,"attack-move without enemies follows the same cohesive trajectory as Move");
        check(move.Reversals<=50 && march.Reversals<=50,"large formation avoidance avoids repeated abrupt direction reversals");
        check(move.Safe && march.Safe,"stable crowd steering preserves full collision sizes");
        check(move.PostArrivalMotion==0 && march.PostArrivalMotion==0,"crowded arrival remains still after settling");
        check(move.Trajectory==Measure(OrderKind.Move).Trajectory,"persistent passing-side decisions replay identically");

        var body=UnitDefinition.Probe with {BodyRadiusMm=927,BodyHeightMm=2534,AttackGapMm=500,MaxAttackers=8};
        SimWorld Pursuit(OrderKind kind)
        {
            var world=new SimWorld(new(48,32,1000,Enumerable.Repeat(true,48*32).ToArray(),new int[49*33]));
            world.Spawn(0,10*48+5,body).Health=10000; world.Spawn(2,10*48+10,body with {BodyRadiusMm=693}).Health=10000;
            world.Enqueue(0,[0],kind,10*48+36,kind==OrderKind.Attack?1:-1);
            world.Enqueue(2,[1],OrderKind.Move,28*48+10); return world;
        }
        var pursuit=Pursuit(OrderKind.AttackMove); int maxZ=0; bool regroup=false;
        for (int tick=0;tick<800;tick++) { pursuit.Step(); maxZ=Math.Max(maxZ,pursuit.Units[0].Z); regroup|=pursuit.Units[0].AttackMoveReturning; }
        check(maxZ<=15000 && regroup,"attack-move drops a fleeing flank target and regroups near its march route");
        check(pursuit.Units[0].Node==10*48+36 && pursuit.Units[0].Order==OrderKind.Idle,"attack-move resumes its destination after regrouping");
        var focus=Pursuit(OrderKind.Attack); for (int tick=0;tick<800;tick++) focus.Step();
        check(focus.Units[0].Z>20000 && focus.Units[0].Target==1,"explicit Attack retains pursuit beyond the attack-move corridor");

        var group=Crowd(OrderKind.AttackMove);
        foreach (var member in group.Units) member.Health=10000;
        var bait=new List<int>();
        foreach (int row in new[] {7,15}) for (int col=14;col<=18;col+=2)
        {
            var enemy=group.Spawn(2,row*48+col,body with {BodyRadiusMm=693}); enemy.Health=10000; bait.Add(enemy.Id);
            group.Enqueue(2,[enemy.Id],OrderKind.Move,(row==7?1:30)*48+col);
        }
        double width=0; bool groupRegrouped=false; int groupHits=0;
        for (int tick=0;tick<900;tick++)
        {
            group.Step(); var members=group.Units.Take(18).ToArray();
            // Keep this controlled fleeing fixture from retaliating after arrival.
            foreach (int id in bait) if (group.Units[id].Goal<0) { group.Units[id].Order=OrderKind.Move; group.Units[id].Target=-1; }
            width=Math.Max(width,(members.Max(u=>u.Z)-members.Min(u=>u.Z))/1000d);
            groupRegrouped |= members.Any(u=>u.AttackMoveReturning);
            groupHits+=group.CombatEvents.Count(e=>e.Kind==CombatEventKind.Hit && e.Unit<18);
            foreach (var a in members) foreach (var b in group.Units.Where(u=>u.Id>a.Id))
                if (SimWorld.BodiesOverlap(a,b)) throw new Exception($"attack-move regrouping overlap tick{tick}: {a.Id} ({a.X},{a.Z}) node{a.Node}/{a.TransitNode} ground{a.GroundSegment} returning{a.AttackMoveReturning} samples {string.Join(';',a.Movement)}, {b.Id} ({b.X},{b.Z}) node{b.Node}/{b.TransitNode} ground{b.GroundSegment} returning{b.AttackMoveReturning} samples {string.Join(';',b.Movement)}, gap{Math.Sqrt(SimWorld.SeparationSquared(a,b))}");
        }
        Console.WriteLine($"CROWD_ATTACK_MOVE width={width:F2}m arrived={group.Units.Take(18).Count(u=>u.Goal<0)} regrouped={groupRegrouped} hits={groupHits}");
        if (group.Units.Take(18).Any(u=>u.Order!=OrderKind.Idle)) Console.WriteLine(string.Join(';',group.Units.Take(18).Where(u=>u.Order!=OrderKind.Idle).Select(u=>$"{u.Id} {u.X},{u.Z} order{u.Order} goal{u.Goal} target{u.Target} origin{u.AttackMoveOrigin} returning{u.AttackMoveReturning} destination{u.OrderDestination}")));
        check(width<=move.MaxWidthM+6.1 && groupRegrouped,"a large attack-move group stays together when enemies flee to both flanks");
        check(group.Units.Take(18).All(u=>u.Order==OrderKind.Idle && u.Goal<0),"a large attack-move group rejoins its destination after flank engagements");

        var completed=new SimWorld(new(30,20,1000,Enumerable.Repeat(true,600).ToArray(),new int[31*21]));
        var veteran=completed.Spawn(0,305,body); completed.Enqueue(0,[0],OrderKind.AttackMove,306); completed.Step(); veteran.AutoEngage=true;
        for (int tick=0;tick<40;tick++) completed.Step(); completed.Spawn(2,318,body);
        for (int tick=0;tick<4;tick++) completed.Step();
        check(veteran.Order==OrderKind.Idle && !veteran.AutoEngage && veteran.Target<0,"completed attack-move does not retain retaliation-driven roaming");

        var facing=new HeadingPlayback(); float original=facing.Update(0,150,150000,1)!.Value;
        check(facing.Update(0,-1,150000,1)==null,"tiny final corrections do not reverse visual facing");
        float maxHeading=0,lastHeading=original;
        for (int i=0;i<30;i++) { lastHeading=facing.Update(i%2==0?70:-70,130,150000,1)??lastHeading; maxHeading=Math.Max(maxHeading,Math.Abs(lastHeading)); }
        check(maxHeading<.22f,"visual facing damps alternating lateral avoidance steps");
        check(Math.Abs(facing.Update(0,-150,150000,2)!.Value)>3,"a replacement order immediately changes the intended visual facing");
    }
}
