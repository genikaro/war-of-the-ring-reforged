using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

static class FlankingChecks
{
    private const int W=72,H=48,Hall=30000,Front=12;
    private static readonly UnitDefinition Melee=UnitDefinition.Probe with {BodyRadiusMm=570,BodyHeightMm=1800,
        FriendlyCompressionPpm=500000,AttackGapMm=500,ReachMm=1600,MaxAttackers=8,MaxHealth=100000};

    public static SimWorld Wall(bool building=false,bool enclosed=false)
    {
        var ground=Enumerable.Repeat(true,W*H).ToArray();
        if (enclosed) for(int x=0;x<W;x++) foreach(int z in new[] {10,25}) ground[z*W+x]=false;
        NavigationObstacle[] obstacles=building ? [new(Hall,[],[],[new([new(30000,12000),new(40000,12000),new(40000,36000),new(30000,36000)],0,8000)])] : [];
        var world=new SimWorld(new(W,H,1000,ground,new int[(W+1)*(H+1)],obstacles:obstacles));
        if (building)
        {
            world.BuildingRules.Add("hall",new("hall","Hall",null,0,0,60,1000000,0,0,0,0,[]));
            world.ConstructionSites.Add(Hall,new(Hall,Hall,14000,"hall",35000,24000) {Team=1,Complete=true,Health=1000000});
        }
        int spacing=building ? 2 : 1;
        for(int i=0;i<Front;i++) world.Spawn(0,(12+i*spacing)*W+28,Melee);
        for(int row=0;row<3;row++) for(int i=0;i<Front;i++) world.Spawn(0,(12+i*spacing)*W+26-row*2,Melee);
        if (building) world.Enqueue(0,Enumerable.Range(0,48),OrderKind.AttackStructure,target:Hall);
        else
        {
            world.Enqueue(0,Enumerable.Range(0,Front),OrderKind.HoldGround);
            for(int col=0;col<3;col++) for(int i=0;i<Front;i++)
            {
                var enemy=world.Spawn(1,(12+i*spacing)*W+30+col*2,Melee); enemy.X-=500;
                world.Enqueue(1,[enemy.Id],OrderKind.HoldGround);
            }
            world.Enqueue(0,Enumerable.Range(Front,36),OrderKind.AttackMove,24*W+44);
        }
        return world;
    }

    public static object Measure(bool building,bool enclosed=false,int ticks=1000,bool allowFlank=true)
    {
        var world=Wall(building,enclosed); var rear=new HashSet<int>(); int maxSearches=0,sideSamples=0;
        for(int i=0;i<ticks;i++)
        {
            world.Step(); maxSearches=Math.Max(maxSearches,world.CombatSearchesLastStep);
            if(!allowFlank) foreach(var unit in world.Units) unit.CombatFlankTried=true;
            foreach(var hit in world.CombatEvents.Where(e=>e.Kind==CombatEventKind.Hit && e.Unit>=Front && e.Unit<48)) rear.Add(hit.Unit);
            sideSamples+=world.Units.Take(48).Skip(Front).Count(u=>u.Z<12000 || u.Z>(building ? 36000 : 24000) || building && u.X>40000);
            if(i%20==0) Safety(world);
        }
        int frontMoved=building ? 0 : world.Units.Take(Front).Count(u=>u.X!=28500 || u.Z!=12500+u.Id*1000);
        return new {building,enclosed,allowFlank,ticks,rear_hitters=rear.Count,side_samples=sideSamples,max_searches=maxSearches,front_moved=frontMoved,digest=world.StateDigest(),
            waiting=world.Units.Take(48).Count(u=>!u.Attacking),positions=world.Units.Take(48).Skip(Front).Select(u=>new{u.Id,u.X,u.Z,u.Target,u.Goal,u.CombatWaiting}).ToArray()};
    }

    private static void Safety(SimWorld world)
    {
        foreach(var a in world.Units.Where(u=>u.Present)) foreach(var b in world.Units.Where(u=>u.Present && u.Id>a.Id))
        {
            if(SimWorld.BodiesOverlap(a,b)) throw new Exception("flanking bodies overlap");
            foreach(double f in new[] {.25,.5,.75})
            {
                var p=MovementPlayback.Sample(a.Movement,f); var q=MovementPlayback.Sample(b.Movement,f);
                if((p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)<Math.Pow(SimWorld.BodySeparation(a,b),2)-4)
                    throw new Exception("flanking bodies cross during playback");
            }
        }
    }

    public static void Benchmark(string output)
    {
        var results=new[] {Measure(false,allowFlank:false),Measure(false),Measure(true),Measure(false,true)};
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var result in results)
        {
            var r=JsonSerializer.SerializeToElement(result);
            Console.WriteLine($"FLANK building={r.GetProperty("building")} enabled={r.GetProperty("allowFlank")} enclosed={r.GetProperty("enclosed")} rear_hitters={r.GetProperty("rear_hitters")} sides={r.GetProperty("side_samples")} searches={r.GetProperty("max_searches")}");
        }
    }

    public static void Run(Action<bool,string> check)
    {
        var without=JsonSerializer.SerializeToElement(Measure(false,allowFlank:false));
        var battle=JsonSerializer.SerializeToElement(Measure(false));
        var siege=JsonSerializer.SerializeToElement(Measure(true));
        var closed=JsonSerializer.SerializeToElement(Measure(false,true));
        int Contributors(JsonElement value)=>value.GetProperty("rear_hitters").GetInt32();
        Console.WriteLine($"FLANKING rear={Contributors(without)}->{Contributors(battle)} siege={Contributors(siege)} sealed={Contributors(closed)}");
        check(Contributors(battle)>=16 && Contributors(battle)>=Contributors(without)+10,
            "waiting melee routes around a held friendly line to enemy ranks beyond it");
        check(battle.GetProperty("side_samples").GetInt32()>0 && battle.GetProperty("front_moved").GetInt32()==0,
            "flankers use the sides without moving Hold Position's front rank");
        check(Contributors(siege)>=32 && siege.GetProperty("side_samples").GetInt32()>0,
            "a crowded melee siege fills free positions around a large building");
        check(Contributors(closed)==0 && closed.GetProperty("side_samples").GetInt32()==0,
            "a sealed corridor waits rather than inventing a flank through terrain or bodies");
        check(new[]{battle,siege,closed}.All(r=>r.GetProperty("max_searches").GetInt32()<=SimWorld.CombatSearchesPerTick),
            "unit flanking and building approach share six path searches per tick");
        check(JsonSerializer.SerializeToElement(Measure(false)).GetProperty("digest").GetString()==battle.GetProperty("digest").GetString(),
            "flank selection, route commitment and detours replay identically");
        var interrupted=Wall();
        for(int tick=0;tick<300 && !interrupted.Units.Any(u=>u.Team==0 && u.CombatFlankTarget>=0);tick++) interrupted.Step();
        var active=interrupted.Units.FirstOrDefault(u=>u.Team==0 && u.CombatFlankTarget>=0);
        check(active!=null,"an open side produces an actual committed flank route");
        if(active!=null)
        {
            interrupted.Enqueue(0,[active.Id],OrderKind.Move,30*W+10); interrupted.Step();
            check(active.Order==OrderKind.Move && active.Target<0 && active.CombatFlankTarget<0 && !active.CombatFlankTried,
                "a player Move cancels the flank and its extra pursuit allowance immediately");
        }
        var ruined=Wall(true); for(int tick=0;tick<120;tick++) ruined.Step();
        ruined.ConstructionSites[Hall].Health=1;
        for(int tick=0;tick<60;tick++) ruined.Step();
        check(!ruined.ConstructionSites.ContainsKey(Hall) && ruined.Units.All(u=>SimWorld.SiegeTarget(u)!=Hall),
            "a building's destruction releases flanking and queued besiegers");
    }
}
