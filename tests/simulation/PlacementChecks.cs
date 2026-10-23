using Reforged.Simulation;

public static class PlacementChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int W = 40,H = 30;
        int Cell(int x,int z) => z*W+x;
        FootprintPoint[] Box(int hx,int hz) => [new(-hx,-hz),new(hx,-hz),new(hx,hz),new(-hx,hz)];
        SimWorld Fixture(int ore = 500)
        {
            int[] citadel = [.. from z in Enumerable.Range(13,6) from x in Enumerable.Range(8,6) select Cell(x,z)];
            int[] mill = [.. from z in Enumerable.Range(19,3) from x in Enumerable.Range(29,3) select Cell(x,z)];
            var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
                obstacles: [new NavigationObstacle(10000,citadel,[]),new NavigationObstacle(20007,mill,[])]);
            nav.SetObstacle(20007,false);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe);
            var world = new SimWorld(nav,rules);
            world.Workers.Add("probe",new(10,9)); world.Economies.Add(0,new(500,ore)); world.Economies.Add(1,new(500,500));
            world.Resources.Add(7,new(7,ResourceKind.Food,30500,20500,4500,0,0,3000,20,1_000_000));
            world.ConstructionRules.Add(ResourceKind.Food,new("mill","Mill",ResourceKind.Food,0,50,60,570,750000,750000,100000,50000,["citadel"]));
            world.ConstructionSites.Add(7,new(7,20007,3000));
            world.StartBuildings.Add(0,["citadel"]); world.StartBuildings.Add(1,["citadel"]);
            world.StartStructures.Add(new(0,"citadel",10500,15500));
            world.BuildingGround = Enumerable.Range(0,W*H).Select(c => c%W != 5).ToArray();
            foreach (var (name,radius) in new[] {("citadel",25000),("camp",15000),("barracks",15000),("keep",25000)}) world.PlacementRadiusMm.Add(name,radius);
            void Building(string name,int food,int ore,int hx,int hz,bool near,bool dropOff,string[] prerequisites)
            {
                world.PlaceableBuildings.Add(name);
                world.BuildingRules.Add(name,new(name,name,null,food,ore,60,400,750000,750000,100000,50000,prerequisites));
                world.Placements.Add(name,new(Box(hx,hz),-400,400,(int)Math.Ceiling(Math.Sqrt((double)hx*hx+(double)hz*hz))+1350,near,dropOff));
            }
            Building("keep",250,250,3000,3000,false,true,[]);
            Building("camp",0,80,2000,2000,true,false,["citadel"]);
            Building("barracks",60,105,2500,2000,true,false,["camp"]);
            world.ProducedUnits.Add("keep",["probe"]); world.ProducedUnits.Add("barracks",["probe"]);
            world.ProductionRules.Add("probe",new("probe","Probe",35,0,1,40));
            world.PopulationProvided.Add("citadel",10); world.PopulationProvided.Add("camp",11); world.PopulationProvided.Add("keep",10);
            world.Navigation.ForBody(1100,700); // a body graph that exists before any runtime placement
            world.Spawn(0,Cell(15,15)); world.Spawn(0,Cell(15,16));
            world.Spawn(1,Cell(36,4)).Order = OrderKind.HoldGround;
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        void Place(SimWorld world,string name,int cell,bool queued = false,int team = 0,int[]? units = null)
            => world.Enqueue(team,units ?? [0,1],OrderKind.PlaceBuilding,cell,world.PlaceableBuildings.IndexOf(name),queued);

        void StepUntil(SimWorld world,Func<bool> done,int limit) { for (int i = 0; i < limit && !done(); i++) world.Step(); }
        var camp = Fixture();
        check(camp.CanPlaceBuilding(0,"camp",Cell(22,15),out _),"open ground near the Citadel accepts a Camp");
        Place(camp,"camp",Cell(22,15)); camp.Step();
        check(!camp.ConstructionSites.Values.Any(s => s.Building == "camp") && camp.Economies[0].OreMilli == 500_000
            && camp.Units.Take(2).All(u => u.Order == OrderKind.PlaceBuilding && u.PlacementCell == Cell(22,15) && u.Goal >= 0),
            "a distant placement raises and pays nothing until a worker gets there");
        StepUntil(camp,() => camp.ConstructionSites.Values.Any(s => s.Building == "camp"),100);
        var site = camp.ConstructionSites.Values.SingleOrDefault(s => s.Building == "camp");
        var layer = camp.Units.Take(2).FirstOrDefault(u => u.Order == OrderKind.Build);
        check(site is { Id: SimWorld.PlacedObstacleBase,Team: 0,Health: 20 } && camp.Economies[0].OreMilli == 420_000 && camp.Tick > 10
            && camp.Navigation.IsObstacleActive(site.Obstacle) && layer != null && !camp.Navigation.ObstacleCoversGroundBody(site.Obstacle,layer.X,layer.Z),
            "the first worker to arrive lays the foundation beside it, pays once and activates a runtime footprint");
        camp.Step();
        check(camp.Units.Take(2).All(u => u.Order == OrderKind.Build && u.OrderTarget == site!.Id && u.PlacementCell == -1),
            "the other worker bound for the same spot joins the foundation");
        check(!camp.Navigation.CanStand(Cell(22,15)) && camp.Navigation.CanStand(Cell(25,15)) && !camp.Navigation.ForBody(1100,700).CanStand(Cell(25,15))
            && !camp.Navigation.ForBody(1200,700).CanStand(Cell(25,15)),"the runtime footprint is rasterized for each body, including graphs created later");
        check(!camp.CanPlaceBuilding(0,"barracks",Cell(28,9),out string reason) && reason == "Requires camp","Barracks require a completed Camp, named in the refusal");
        Step(camp,200);
        check(site!.Complete && site.Health == 400 && camp.Units.Take(2).All(u => u.Order == OrderKind.Idle) && camp.PopulationCapacity(0) == 21,
            "a completed Camp adds its population and releases the workers");
        check(camp.CanPlaceBuilding(0,"barracks",Cell(28,9),out _),"a completed Camp satisfies the Barracks prerequisite");
        check(!camp.CanPlaceBuilding(0,"camp",Cell(37,26),out reason) && reason == "Too far from your buildings"
            && camp.CanPlaceBuilding(0,"camp",Cell(36,15),out _),"a completed building extends the source placement radius");
        var replay = Fixture(); replay.Commands.AddRange(camp.Commands); Step(replay,camp.Tick);
        check(replay.StateDigest() == camp.StateDigest(),"placement and construction replay identically");

        var invalid = Fixture();
        check(!invalid.CanPlaceBuilding(0,"camp",Cell(37,26),out reason) && reason == "Too far from your buildings","new buildings must be near an existing one");
        check(!invalid.CanPlaceBuilding(0,"camp",Cell(6,22),out reason) && reason == "Ground unsuitable for building","source building passability is required under the footprint");
        check(!invalid.CanPlaceBuilding(0,"camp",Cell(14,15),out reason) && reason == "Too close to another structure or resource","footprints cannot overlap a structure");
        check(!invalid.CanPlaceBuilding(0,"camp",Cell(30,20),out reason) && reason == "Too close to another structure or resource","footprints cannot cover a resource-building site");
        check(!invalid.CanPlaceBuilding(0,"keep",Cell(36,4),out reason) && reason == "Placement blocked by a unit","units inside the footprint block placement");
        check(!invalid.CanPlaceBuilding(0,"camp",Cell(0,15),out reason) && reason == "Choose open ground inside the map","footprints stay inside the map");
        // The well stands at (30.5, 20.5) m; a Camp is 4 m wide. Its source clearance is 7 m from the well's centre.
        var clear = Fixture(); clear.ResourceClearanceMm.Add(ResourceKind.Food,7000);
        check(invalid.CanPlaceBuilding(0,"camp",Cell(22,20),out _) && !clear.CanPlaceBuilding(0,"camp",Cell(22,20),out reason)
            && reason == SimWorld.TooCloseToResource && clear.CanPlaceBuilding(0,"camp",Cell(20,20),out _),
            "no footprint within a resource's source MinBuildingPlacementRadius");
        var poor = Fixture(79); Place(poor,"camp",Cell(22,15)); poor.Step();
        check(!poor.ConstructionSites.Values.Any(s => s.Building != null) && poor.Economies[0].OreMilli == 79_000 && poor.Navigation.ObstacleIds.Max() == 20007,
            "an unaffordable placement creates no footprint");
        var stolen = Fixture(); Place(stolen,"camp",Cell(22,15),team: 1); stolen.Step();
        check(!stolen.ConstructionSites.Values.Any(s => s.Building != null) && stolen.Economies[1].Ore == 500,"another team cannot place with these workers");
        var spent = Fixture(); Place(spent,"camp",Cell(22,15)); spent.Step(); spent.Economies[0] = new(500,10);
        var failures = new List<(int Team,int Unit,string Reason)>();
        for (int i = 0; i < 100 && spent.Units.Take(2).Any(u => u.Order == OrderKind.PlaceBuilding); i++) { spent.Step(); failures.AddRange(spent.FoundationFailures); }
        check(!spent.ConstructionSites.Values.Any(s => s.Building != null) && spent.Units.Take(2).All(u => u.Order == OrderKind.Idle)
            && spent.Economies[0].OreMilli == 10_000 && failures.Any(f => f is { Team: 0,Reason: "Insufficient resources" }),
            "a worker that arrives after the money was spent gives the foundation up and says why");
        var crowded = Fixture(); var idler = crowded.Spawn(0,Cell(22,15));
        check(!crowded.CanPlaceBuilding(0,"camp",Cell(22,15),out _) && crowded.CanPlaceBuilding(0,"camp",Cell(22,15),out _,ignoreUnits: true),
            "a unit on the spot blocks laying a foundation but not ordering one");
        Place(crowded,"camp",Cell(22,15)); StepUntil(crowded,() => crowded.ConstructionSites.Values.Any(s => s.Building == "camp"),200);
        var cleared = crowded.ConstructionSites.Values.SingleOrDefault(s => s.Building == "camp");
        check(cleared != null && !crowded.Navigation.ObstacleCoversGroundBody(cleared.Obstacle,idler.X,idler.Z) && idler.Order is OrderKind.Idle or OrderKind.Move,
            "an idle unit of the same side steps off the spot so the foundation can go down");
        var mill = Fixture(); mill.Enqueue(0,[0],OrderKind.Build,target: 7); mill.Step();
        check(mill.ConstructionSites[7].Team == -1 && mill.Economies[0].OreMilli == 500_000 && mill.Units[0].Order == OrderKind.Build,
            "a distant Mill site is claimed and paid for only on arrival");
        StepUntil(mill,() => mill.ConstructionSites[7].Team == 0,200);
        check(mill.ConstructionSites[7].Team == 0 && mill.Economies[0].OreMilli == 450_000 && mill.Navigation.IsObstacleActive(20007)
            && !mill.Navigation.ObstacleCoversGroundBody(20007,mill.Units[0].X,mill.Units[0].Z),"the arriving worker claims the Mill site from outside its footprint");

        var cancelled = Fixture(); Place(cancelled,"camp",Cell(22,15)); Step(cancelled,60);
        int obstacle = cancelled.ConstructionSites.Values.Single(s => s.Building != null).Obstacle;
        cancelled.Enqueue(0,[],OrderKind.CancelConstruction,target: obstacle); cancelled.Step();
        check(!cancelled.ConstructionSites.ContainsKey(obstacle) && !cancelled.Navigation.IsObstacleActive(obstacle) && cancelled.Navigation.CanStand(Cell(22,15))
            && cancelled.Economies[0].OreMilli == 480_000 && cancelled.Units.Take(2).All(u => u.Order == OrderKind.Idle),
            "cancelling removes the foundation and footprint and refunds seventy five percent");
        cancelled.Enqueue(0,[],OrderKind.CancelConstruction,target: obstacle); cancelled.Step();
        check(cancelled.Economies[0].OreMilli == 480_000,"repeated cancellation cannot mint refunds");
        check(cancelled.CanPlaceBuilding(0,"camp",Cell(22,15),out _),"the cleared ground accepts a new placement");

        var queued = Fixture(); queued.Enqueue(0,[0,1],OrderKind.Move,Cell(15,25)); Place(queued,"camp",Cell(22,15),queued: true); queued.Step();
        check(!queued.ConstructionSites.Values.Any(s => s.Building != null) && queued.Economies[0].OreMilli == 500_000,"queued placement reserves nothing before activation");
        Step(queued,400);
        check(queued.ConstructionSites.Values.Count(s => s.Building == "camp") == 1 && queued.Economies[0].OreMilli == 420_000
            && queued.ConstructionSites.Values.Single(s => s.Building == "camp").Complete,"both queued workers join one foundation and pay once");

        var keep = Fixture(); Place(keep,"keep",Cell(30,8)); Step(keep,300);
        var keepSite = keep.ConstructionSites.Values.Single(s => s.Building == "keep");
        check(keepSite.Complete && keep.DropOffs.Any(d => d.Team == 0 && d.X == 30500 && d.Z == 8500) && keep.Producers.ContainsKey(keepSite.Id)
            && keep.PopulationCapacity(0) == 20,"a completed Citadel accepts deliveries, trains units and adds population");
        keep.Enqueue(0,[],OrderKind.Train,0,keepSite.Id); Step(keep,41);
        check(keep.Units.Count == 4 && keep.Units[^1].Team == 0 && !keep.Navigation.ObstacleCovers(keepSite.Obstacle,keep.Units[^1].Node),
            "a placed Citadel trains a worker outside its footprint");
    }
}
