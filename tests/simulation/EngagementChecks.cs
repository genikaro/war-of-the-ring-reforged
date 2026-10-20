using Reforged.Simulation;
using System.Diagnostics;
using System.Text.Json;

static class EngagementChecks
{
    public static SimWorld Crowd()
    {
        var world = new SimWorld(new(48,32,1000,Enumerable.Repeat(true,48*32).ToArray(),new int[49*33]));
        var knight = UnitDefinition.Probe with {BodyRadiusMm=927,BodyHeightMm=2534,AttackGapMm=500,MaxAttackers=8};
        var uruk = knight with {BodyRadiusMm=693,BodyHeightMm=2197};
        for (int team=0;team<2;team++) for (int row=0;row<6;row++) for (int col=0;col<3;col++)
            world.Spawn(team,(5+row*2)*48+(team==0?4:22)+col*2,team==0?knight:uruk).Health=10000;
        world.Enqueue(0,Enumerable.Range(0,18),OrderKind.Attack,target:18);
        return world;
    }

    public static object Measure(string? output = null)
    {
        var world=Crowd(); var hitters=new[] {new HashSet<int>(),new HashSet<int>()};
        int[] active=new int[2],peaks=new int[2]; double[] times=new double[800];
        for (int tick=0;tick<800;tick++)
        {
            long start=Stopwatch.GetTimestamp(); world.Step(); times[tick]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            foreach (var hit in world.CombatEvents.Where(e => e.Kind==CombatEventKind.Hit)) hitters[world.Units[hit.Unit].Team].Add(hit.Unit);
            if (tick>=400) for (int team=0;team<2;team++)
            {
                int count=world.Units.Count(u => u.Team==team && u.Attacking); active[team]+=count; peaks[team]=Math.Max(peaks[team],count);
            }
        }
        var report=new {unique_hitters=hitters.Select(h=>h.Count),average_fighters=active.Select(a=>a/400d),peak_fighters=peaks,
            elapsed_ms=times.Sum(),p95_step_ms=times.Order().ElementAt(759),max_step_ms=times.Max(),digest=world.StateDigest()};
        if (output != null) File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        return report;
    }

    public static void Run(Action<bool,string> check)
    {
        var world=Crowd(); var hitters=new[] {new HashSet<int>(),new HashSet<int>()}; int[] active=new int[2];
        for (int tick=0;tick<800;tick++)
        {
            world.Step();
            foreach (var hit in world.CombatEvents.Where(e => e.Kind==CombatEventKind.Hit)) hitters[world.Units[hit.Unit].Team].Add(hit.Unit);
            foreach (var a in world.Units) foreach (var b in world.Units.Where(u => u.Id > a.Id))
                if (SimWorld.BodiesOverlap(a,b)) throw new Exception("engagement crowd overlapped");
            if (tick>=400) for (int team=0;team<2;team++) active[team]+=world.Units.Count(u=>u.Team==team && u.Attacking);
        }
        check(hitters.All(h=>h.Count>=16),"nearly all eighteen units on both sides join a crowded engagement");
        check(active.All(n=>n>=12*400),"crowded armies maintain a broad melee front rather than a few fighters");
        var replay=Crowd(); for (int tick=0;tick<800;tick++) replay.Step();
        check(world.StateDigest()==replay.StateDigest(),"load-balanced combat and flanking replay identically");

        var rules=UnitDefinition.Probe with {BodyRadiusMm=693,BodyHeightMm=2197,AttackGapMm=500,MaxAttackers=8};
        SimWorld Fixture() => new(new(30,20,1000,Enumerable.Repeat(true,600).ToArray(),new int[31*21]));
        var force=Fixture(); force.Spawn(0,305,rules); force.Spawn(0,309,rules).Health=10000; force.Spawn(1,311,rules).Health=10000;
        force.Enqueue(0,[0],OrderKind.ForceAttack,target:1); for (int tick=0;tick<200;tick++) force.Step();
        check(force.Units[0].Target==1 && force.Units[0].Order==OrderKind.ForceAttack,"crowd distribution preserves explicit force-attack targets");
        var hold=Fixture(); var sentry=hold.Spawn(0,304,rules); hold.Spawn(0,308,rules); hold.Spawn(2,312,rules).Health=10000;
        hold.Enqueue(0,[0],OrderKind.HoldGround); hold.Enqueue(0,[1],OrderKind.Attack,target:2);
        for (int tick=0;tick<200;tick++) hold.Step();
        check(sentry.X==4500 && sentry.Z==10500 && sentry.Target<0,"shared combat awareness never makes Hold Position pursue");
        var queued=Fixture(); var queuedUnit=queued.Spawn(0,305,rules); queued.Spawn(2,312,rules); queued.Spawn(2,316,rules);
        queued.Enqueue(0,[0],OrderKind.Attack,target:1); queued.Step();
        queued.Enqueue(0,[0],OrderKind.Move,310,queued:true); queuedUnit.Target=2; queued.Units[1].Health=0;
        queued.Step(); queued.Step();
        check(queuedUnit.Order==OrderKind.Move && queuedUnit.OrderTarget<0,"primary target death advances a queued order while fighting an overflow target");
    }
}
