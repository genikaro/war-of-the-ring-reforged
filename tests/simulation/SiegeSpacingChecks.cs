using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

static class SiegeSpacingChecks
{
    private const int W=64,H=48,Hall=30000;
    private static readonly UnitDefinition Melee=UnitDefinition.Probe with {
        BodyRadiusMm=570,BodyHeightMm=1800,FriendlyCompressionPpm=500_000,ReachMm=1600,AttackGapMm=500 };

    private static SimWorld Small(int count=64,bool packed=false,bool ranged=false,bool march=false)
    {
        var world=new SimWorld(new(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
            obstacles:[new(Hall,[],[],[new([new(26000,17000),new(30000,17000),new(30000,21000),new(26000,21000)],0,8000)])]));
        world.BuildingRules.Add("hall",new("hall","Hall",null,0,0,60,1_000_000,0,0,0,0,[]));
        world.ConstructionSites.Add(Hall,new(Hall,Hall,3000,"hall",28000,19000) {Team=1,Complete=true,Health=1_000_000});
        var body=ranged ? Melee with { BodyRadiusMm=535,ReachMm=13000,MinimumRangeMm=1000,
            ProjectileSpeedMmPerSecond=25000,Attacks=[new("shoot",1,20,[new(5,1_000_000)])] } : Melee;
        for(int i=0;i<count;i++)
        {
            var unit=world.Spawn(0,(12+i/8*2)*W+4+i%8*2,body);
            if(packed)
            {
                unit.X=ranged ? 15000+i%3*700 : 24500;
                unit.Z=17500+(ranged ? i/3 : i)*700;
                unit.Node=world.Navigation.Cell(unit.X,unit.Z);
            }
        }
        world.Enqueue(0,Enumerable.Range(0,count),march ? OrderKind.AttackMove : OrderKind.AttackStructure,
            cell:march ? 19*W+45 : -1,target:march ? -1 : Hall);
        return world;
    }

    private static void Safety(SimWorld world)
    {
        foreach(var a in world.Units.Where(u=>u.Present))
        foreach(var b in world.Units.Where(u=>u.Present && u.Id>a.Id))
        {
            if(SimWorld.BodiesOverlap(a,b)) throw new Exception("siege spacing bodies overlap");
            var p=MovementPlayback.Sample(a.Movement,.5); var q=MovementPlayback.Sample(b.Movement,.5);
            if((p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)<Math.Pow(SimWorld.BodySeparation(a,b),2)-4)
                throw new Exception("siege spacing bodies cross during playback");
        }
    }

    private static JsonElement Measure(SimWorld world)
        =>JsonSerializer.SerializeToElement(MeleeSpacingChecks.Measure(world));

    private static SimWorld MixedSizes()
    {
        var world=Small(24);
        foreach(var unit in world.Units)
        {
            unit.Definition=Melee with {BodyRadiusMm=(unit.Id%3) switch {0=>570,1=>927,_=>1200}};
            unit.Definition.Validate();
        }
        return world;
    }

    public static void Benchmark(string output)
    {
        var results=new[] {
            new {scenario="large-footprint-48",result=Measure(FlankingChecks.Wall(building:true))},
            new {scenario="small-footprint-64",result=Measure(Small())},
            new {scenario="packed-melee",result=Measure(Small(6,packed:true))},
            new {scenario="packed-ranged",result=Measure(Small(18,packed:true,ranged:true))} };
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
        foreach(var result in results) Console.WriteLine(JsonSerializer.Serialize(result));
    }

    public static void Run(Action<bool,string> check)
    {
        foreach(var (name,world,minimum) in new[] {
            ("large footprint",FlankingChecks.Wall(building:true),32),
            ("small footprint",Small(),12),
            ("mixed body sizes",MixedSizes(),8),
            ("packed melee",Small(6,packed:true),6),
            ("packed ranged",Small(18,packed:true,ranged:true),18),
            ("attack-move siege",Small(24,march:true),12) })
        {
            var result=Measure(world);
            Console.WriteLine($"SIEGE_SPACING {name} {result}");
            check(result.GetProperty("crowded_percent").GetDouble()<=2
                && result.GetProperty("nearest_body_ratio_p10").GetDouble()>=.82,
                name+": building attackers and reserves keep visible allied spacing");
            check(result.GetProperty("hitters").GetInt32()>=minimum,name+": spaced attackers keep damaging the building");
            check(world.Units.All(u=>SimWorld.SiegeTarget(u)>=0),name+": a full perimeter retains the siege order");
            check(result.GetProperty("max_searches").GetInt32()<=SimWorld.CombatSearchesPerTick,name+": approaches share the combat search cap");
            Safety(world);
        }
        var line=Small(); var replay=Small();
        for(int tick=0;tick<1000;tick++) { line.Step(); replay.Step(); if(tick%10==0) Safety(line); }
        check(line.StateDigest()==replay.StateDigest(),"spaced siege positions replay identically");
        var waiting=line.Units.Where(u=>u.Present && !u.Attacking).Select(u=>u.Id).ToHashSet();
        check(waiting.Count>0,"a small building keeps excess attackers in reserve");
        var reserve=line.Units.First(u=>waiting.Contains(u.Id) && u.CombatWaiting);
        check(UnitOrderReadout.From(line,reserve,_=>"Soldier").Activity.StartsWith("Waiting for room at Hall"),
            "the selection readout explains a siege reserve's waiting state");
        foreach(var front in line.Units.Where(u=>u.Attacking)) front.Health=0;
        bool replaced=false;
        for(int tick=0;tick<300;tick++)
        {
            line.Step(); if(tick%10==0) Safety(line);
            replaced|=line.CombatEvents.Any(e=>e.Kind==CombatEventKind.Hit && waiting.Contains(e.Unit));
        }
        check(replaced,"siege reserves take over attack positions after front-rank casualties");
        var marcher=line.Units.First(u=>u.Present);
        line.Enqueue(0,[marcher.Id],OrderKind.Move,8*W+8); line.Step();
        check(marcher.Order==OrderKind.Move && SimWorld.SiegeTarget(marcher)<0 && !marcher.StrikeActive && !marcher.CombatWaiting,
            "Move immediately releases a spaced siege attacker");
        line.ConstructionSites[Hall].Health=1;
        for(int tick=0;tick<100 && line.ConstructionSites.ContainsKey(Hall);tick++) line.Step();
        line.Step();
        check(!line.ConstructionSites.ContainsKey(Hall) && line.Units.All(u=>SimWorld.SiegeTarget(u)<0 && !u.CombatWaiting),
            "destroying a building releases both its attackers and waiting ranks");
        var sealedWorld=Small(1);
        sealedWorld.Navigation.AddObstacle(Hall+1,[new([new(22000,0),new(23000,0),new(23000,48000),new(22000,48000)],0,8000)]);
        sealedWorld.Navigation.SetObstacle(Hall+1,true);
        for(int tick=0;tick<40;tick++) sealedWorld.Step();
        check(SimWorld.SiegeTarget(sealedWorld.Units[0])<0 && sealedWorld.ConstructionSites[Hall].Health==1_000_000,
            "a statically unreachable building is still distinguished from a temporarily full attack perimeter");
    }
}
