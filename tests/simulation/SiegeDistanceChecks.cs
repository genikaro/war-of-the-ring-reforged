using Reforged.Simulation;

static class SiegeDistanceChecks
{
    private const int W=128,H=64,Hall=30000;
    private static readonly UnitDefinition Melee=UnitDefinition.Probe with {BodyRadiusMm=570,BodyHeightMm=1800,
        FriendlyCompressionPpm=500000,ReachMm=1600,AttackGapMm=500};

    private static SimWorld Fixture(int distance,bool detour=false,int count=1,bool forced=false,int wallX=60)
    {
        var ground=Enumerable.Repeat(true,W*H).ToArray();
        if(detour) for(int z=0;z<51;z++) ground[z*W+wallX]=false;
        var footprint=new NavigationFootprint([new(110000,29000),new(116000,29000),new(116000,35000),new(110000,35000)],0,8000);
        var world=new SimWorld(new(W,H,1000,ground,new int[(W+1)*(H+1)],obstacles:[new(Hall,[],[],[footprint])]));
        world.BuildingRules.Add("hall",new("hall","Hall",null,0,0,60,100000,0,0,0,0,[]));
        world.ConstructionSites.Add(Hall,new(Hall,Hall,4500,"hall",113000,32000){Team=forced?0:1,Health=100000,Complete=true});
        for(int i=0;i<count;i++) world.Spawn(0,(24+i%8*2)*W+110-distance-i/8*2,Melee);
        world.Enqueue(0,Enumerable.Range(0,count),OrderKind.AttackStructure,cell:forced?SimWorld.ForcedSiege:-1,target:Hall);
        return world;
    }

    public static void Run(Action<bool,string> check)
    {
        foreach(int distance in new[] {6,14,24,48,100})
        {
            var world=Fixture(distance); bool moved=false,hit=false;
            for(int tick=0;tick<1600 && !hit;tick++)
            {
                world.Step();
                if(tick<20) moved|=world.Units[0].Moving;
                hit=world.ConstructionSites[Hall].Health<100000;
            }
            check(moved,$"a building attack from {distance} m starts moving within one second");
            check(hit,$"a building attack from {distance} m reaches the footprint and deals damage");
        }
        var detour=Fixture(100,detour:true); var replay=Fixture(100,detour:true);
        bool detourHit=false;
        for(int tick=0;tick<2200 && !detourHit;tick++)
        {
            detour.Step(); replay.Step();
            detourHit=detour.ConstructionSites[Hall].Health<100000;
        }
        check(detourHit,"a distant building attack follows a terrain detour rather than exhausting its local search");
        check(detour.StateDigest()==replay.StateDigest(),"long building approaches replay identically");
        var closeDetour=Fixture(8,detour:true,wallX:107);
        for(int tick=0;tick<1800 && closeDetour.ConstructionSites[Hall].Health==100000;tick++) closeDetour.Step();
        check(closeDetour.ConstructionSites[Hall].Health<100000,
            "a nearby building behind a long terrain detour also gets a global approach route");
        var group=Fixture(80,count:24,forced:true); var movedIds=new HashSet<int>(); int maxSearches=0;
        for(int tick=0;tick<2000 && group.ConstructionSites[Hall].Health==100000;tick++)
        {
            group.Step(); maxSearches=Math.Max(maxSearches,group.CombatSearchesLastStep);
            foreach(var unit in group.Units.Where(u=>u.Moving)) movedIds.Add(unit.Id);
            foreach(var a in group.Units) foreach(var b in group.Units.Where(u=>u.Id>a.Id))
                if(SimWorld.BodiesOverlap(a,b)) throw new Exception("distant siege bodies overlap");
        }
        check(movedIds.Count==24 && group.ConstructionSites[Hall].Health<100000,
            "a group forced attack on an own building starts at long range and reaches it");
        check(maxSearches<=SimWorld.CombatSearchesPerTick,"long building routes retain the shared planning budget");
    }
}
