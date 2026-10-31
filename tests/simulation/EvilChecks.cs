using Reforged.Simulation;

/// <summary>The Evil side's simulation rules (build-133): corruption, palettes, the Slavemaster's population, the
/// per-side resource buildings and the Fate War Posts earn.</summary>
public static class EvilChecks
{
    public static void Run(Action<bool,string> check)
    {
        const string Hovel = "hovel",Post = "post",Mound = "mound",Master = "master",Slave = "slave";
        SimWorld Fixture(bool both = false)
        {
            var nav = new GridNavigation(80,60,1000,Enumerable.Repeat(true,80*60).ToArray(),new int[81*61]);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe);
            var world = new SimWorld(nav,rules);
            world.TeamFactions[0] = SimWorld.Evil; world.TeamFactions[1] = both ? SimWorld.Evil : SimWorld.Good;
            world.Economies.Add(0,new(2000,2000)); world.Economies.Add(1,new(2000,2000));
            ConstructionDefinition Rules(string name,int food,int ore,int ticks,int health,params string[] required)
                => new(name,name,null,food,ore,ticks,health,750000,750000,100000,50000,required);
            world.BuildingRules.Add(Hovel,Rules(Hovel,225,225,70,700));
            world.BuildingRules.Add(Post,Rules(Post,0,65,1,500));
            world.BuildingRules.Add(Mound,Rules(Mound,55,110,30,675,Hovel));
            foreach (string building in new[] {Hovel,Post,Mound})
                world.Placements.Add(building,new([new(-1000,-1000),new(1000,-1000),new(1000,1000),new(-1000,1000)],-400,400,3000,false,building == Hovel));
            world.PlaceableBuildings.AddRange([Hovel,Post,Mound]);
            world.Workers.Add(Slave,new(10,9));
            world.Palettes[Slave] = [Hovel,Mound]; world.Palettes[Master] = [Post];
            world.Corruption = new(new() { [Post] = 25000 },new() { [Hovel] = 25000 },[Hovel,Mound]);
            world.StartBuildings.Add(0,[Hovel]);
            world.ConstructionSites.Add(10000,new(10000,10000,5000,Hovel,20000,30000) { Team = 0,Health = 700,Complete = true,Prebuilt = true });
            world.ProductionRules.Add(Slave,new(Slave,"Slave",35,0,1,240)); world.ProductionRules.Add(Master,new(Master,"Slavemaster",0,95,0,600));
            world.PopulationByUnit[Master] = (12,2);
            world.Producers.Add(10000,new(10000,0,20000,30000,0,[Slave,Master]));
            return world;
        }
        int At(SimWorld world,int x,int z) => world.Navigation.Cell(x,z);
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        var definition = UnitDefinition.Probe;

        var world = Fixture();
        check(world.IsCorrupted(0,20000,30000) && world.IsCorrupted(0,44000,30000) && !world.IsCorrupted(0,46000,30000),
            "the team's start Hovel corrupts the ground within its radius and no further");
        check(!world.IsCorrupted(1,20000,30000),"corruption belongs to the team whose building spreads it");
        check(world.CanPlaceBuilding(0,Mound,At(world,30000,30000),out _) && !world.CanPlaceBuilding(0,Mound,At(world,60000,30000),out string bare) && bare == SimWorld.NeedsCorruption,
            "a building that needs corruption is refused on bare ground, with the reason");
        check(world.CanPlaceBuilding(0,Post,At(world,60000,30000),out _),"a War Post needs no corrupted ground");
        check(!world.CanPlaceBuilding(1,Mound,At(world,30000,30000),out _),"another team's ground is its own (no Hovel of theirs near)");

        // A finished War Post widens the ground; a destroyed one takes it back; one under construction does nothing yet.
        var site = new ConstructionSite(10001,10001,1000,Post,60000,30000) { Team = 0,Health = 500 };
        world.ConstructionSites.Add(10001,site);
        check(!world.IsCorrupted(0,60000,30000),"a War Post still being raised corrupts nothing");
        site.Complete = true;
        check(world.IsCorrupted(0,60000,30000) && world.IsCorrupted(0,80000-1,30000) && world.CanPlaceBuilding(0,Mound,At(world,70000,30000),out _),
            "a finished War Post corrupts its radius and opens it to building");
        site.Health = 0;
        check(!world.IsCorrupted(0,60000,30000),"a fallen War Post takes its ground back");
        site.Health = 500;
        world.ConstructionSites[10000].Health = 0;
        check(world.IsCorrupted(0,60000,30000) && !world.IsCorrupted(0,20000,30000),"the start Hovel's ground goes with the Hovel; a post's stays");

        var builders = Fixture();
        builders.Spawn(0,At(builders,20000,40000),definition with { Name = Slave });
        builders.Spawn(0,At(builders,22000,40000),definition with { Name = Master });
        check(builders.IsBuilder(builders.Units[0]) && builders.IsBuilder(builders.Units[1]),"a worker and a unit with a palette are builders");
        check(builders.MayPlace(builders.Units[0],Mound) && !builders.MayPlace(builders.Units[0],Post) && builders.MayPlace(builders.Units[1],Post) && !builders.MayPlace(builders.Units[1],Mound),
            "a palette limits what each builder may place (the Slavemaster raises War Posts only)");
        builders.Enqueue(0,[builders.Units[1].Id],OrderKind.PlaceBuilding,At(builders,24000,40000),builders.PlaceableBuildings.IndexOf(Mound));
        Step(builders,5);
        check(!builders.ConstructionSites.Values.Any(s => s.Building == Mound),"a Slavemaster's order to raise a Mound is ignored");
        builders.Enqueue(0,[builders.Units[1].Id],OrderKind.PlaceBuilding,At(builders,24000,40000),builders.PlaceableBuildings.IndexOf(Post));
        Step(builders,200);
        check(builders.ConstructionSites.Values.Any(s => s.Building == Post && s.Team == 0 && s.Complete),"a Slavemaster raises a War Post, at once (one tick of work)");
        check(builders.Economies[0].OreMilli == 2_000_000-65_000,"the War Post costs its source 65 ore");

        // The Slavemaster supplies the only population there is.
        var population = Fixture();
        population.InitialPopulationCapacity = 0;
        check(population.PopulationCapacity(0) == 0,"an Evil team with no Slavemaster has no population capacity");
        population.Spawn(0,At(population,22000,40000),definition with { Name = Master });
        population.Spawn(0,At(population,24000,40000),definition with { Name = Slave });
        check(population.PopulationCapacity(0) == 12 && population.PopulationUsed(0) == 1,"a living Slavemaster adds 12 population and costs none");
        population.Spawn(0,At(population,26000,40000),definition with { Name = Master });
        check(population.PopulationCapacity(0) == 24,"each Slavemaster adds 12");
        population.Units[0].HealthMilli = 0;
        check(population.PopulationCapacity(0) == 12,"a dead Slavemaster takes its 12 with it");
        population.Enqueue(0,[],OrderKind.Train,1,10000); Step(population,2);
        check(population.Producers[10000].Queue.Count == 1,"a Slavemaster can be trained while the capacity is full");
        population.Spawn(0,At(population,28000,40000),definition with { Name = Master });
        check(!population.CanTrain(0,10000,1,out string limit) && limit.Contains("only 2"),"a team may not have more Slavemasters than the source limit");
        population.PopulationByUnit[Master] = (12,20);
        population.MaxPopulation = 24;
        check(population.PopulationCapacity(0) == 24,"the world population cap still limits what Slavemasters add");

        // Resource buildings follow the side that raises them; the first side loaded stays the default.
        var sides = Fixture();
        ConstructionDefinition Resource(string name,int health) => new(name,name,ResourceKind.Food,0,50,25,health,750000,750000,100000,50000,[]);
        sides.ConstructionRules[ResourceKind.Food] = Resource("mill",570);
        sides.FactionConstructionRules[(SimWorld.Good,ResourceKind.Food)] = sides.ConstructionRules[ResourceKind.Food];
        sides.FactionConstructionRules[(SimWorld.Evil,ResourceKind.Food)] = Resource("slaughterhouse",530);
        check(sides.ResourceRules(0,ResourceKind.Food)!.Name == "slaughterhouse" && sides.ResourceRules(1,ResourceKind.Food)!.Name == "mill",
            "each team raises its own side's food building over a well");
        check(sides.ResourceRulesNamed("slaughterhouse")?.Kind == ResourceKind.Food && sides.ResourceRulesOf(0).Single().Value.Name == "slaughterhouse"
            && sides.ResourceRulesOf(1).Single().Value.Name == "mill","the planner finds resource buildings by name and by team");
        check(SimWorld.FactionOfName("BUILDING_E_TOWER") == SimWorld.Evil && SimWorld.FactionOfName("UNIT_E_ORC_SLASHER") == SimWorld.Evil
            && SimWorld.FactionOfName("BUILDING_G_TOWER") == SimWorld.Good && SimWorld.FactionOfName("OBJECT_RESOURCE_ORE_PILE") == SimWorld.Good,
            "names carry their side");
        check(sides.Seated(SimWorld.Evil) && sides.Seated(SimWorld.Good),"both sides are seated in a mixed match");
        var evilOnly = Fixture(both: true);
        check(evilOnly.Seated(SimWorld.Evil) && !evilOnly.Seated(SimWorld.Good),"a match of two Evil teams seats no Good side");

        // Evil heroes (build-135): Aura of Terror takes armour off the enemies near a living carrier; Speed of the Wolf speeds its caster only.
        var terrorWorld = Fixture();
        var terror = new AbilityDefinition(8,2,"Aura of Terror","icon",true,AbilityKind.TerrorAura,RangeMm: 8000,ArmorReductionMilli: 2000);
        terror.Validate();
        var king = terrorWorld.Spawn(0,At(terrorWorld,30000,40000),definition with { Name = "king",Hero = true,Abilities = [terror],MaxHealth = 450,ArmorMilli = 3000 });
        var near = terrorWorld.Spawn(1,At(terrorWorld,34000,40000),definition with { Name = "near",ArmorMilli = 1000 });
        var far = terrorWorld.Spawn(1,At(terrorWorld,50000,40000),definition with { Name = "far",ArmorMilli = 1000 });
        var friend2 = terrorWorld.Spawn(0,At(terrorWorld,32000,40000),definition with { Name = "friend",ArmorMilli = 1000 });
        Step(terrorWorld,2);
        check(near.ArmorMilli == -1000 && far.ArmorMilli == 1000 && friend2.ArmorMilli == 1000 && king.ArmorMilli == 3000,
            "Aura of Terror takes 2 armor off the enemies within 8 m and nobody else");
        king.HealthMilli = 0; Step(terrorWorld,2);
        check(near.ArmorMilli == 1000,"the aura ends with its carrier");
        check(terrorWorld.StateDigest() != Fixture().StateDigest(),"the digest follows the terror state");
        var wolfWorld = Fixture();
        var wolf = new AbilityDefinition(13,1,"Speed of the Wolf","icon",false,AbilityKind.ElvenSpeed,RechargeTicks: 360,BonusScale: 1_500_000,DurationTicks: 200,AreaRadiusMm: 500);
        wolf.Validate();
        var runner = wolfWorld.Spawn(0,At(wolfWorld,30000,40000),definition with { Name = "runner",Hero = true,Abilities = [wolf] });
        var mate = wolfWorld.Spawn(0,At(wolfWorld,32000,40000),definition with { Name = "mate" });
        wolfWorld.EnqueueAbility(0,[runner.Id],1,-1); Step(wolfWorld,3);
        check(runner.TimeScalePpm == 1_500_000 && mate.TimeScalePpm == 1_000_000,"Speed of the Wolf speeds only its caster by the source 1.5");

        // Fate: ground a War Post corrupts earns power each second (the start Hovel's ground earns none).
        var fate = Fixture();
        fate.FateRules = new(500,800_000,1_100_000,1_450_000,1_000_000,[Hovel]) { WarpostPpm = 100_000_000 };
        fate.ConstructionSites.Add(10001,new(10001,10001,1000,Post,60000,30000) { Team = 0,Health = 500,Complete = true });
        Step(fate,fate.TicksToSecond());
        check(fate.Economies[0].Fate >= 1 && fate.Economies[0].FateEarned >= 1,"a War Post's corrupted ground earns Fate");
        var idle = Fixture(); idle.FateRules = fate.FateRules; Step(idle,60);
        check(idle.Economies[0].Fate == 0 && idle.Economies[0].FateProgressMicro == 0,"the start Hovel's ground alone earns no Fate");
        var good = Fixture(); good.TeamFactions[0] = SimWorld.Good; good.FateRules = fate.FateRules;
        good.ConstructionSites.Add(10001,new(10001,10001,1000,Post,60000,30000) { Team = 0,Health = 500,Complete = true }); Step(good,60);
        check(good.Economies[0].Fate == 0,"only an Evil team earns Fate from corruption");
        var replay = Fixture(); replay.FateRules = fate.FateRules;
        replay.ConstructionSites.Add(10001,new(10001,10001,1000,Post,60000,30000) { Team = 0,Health = 500,Complete = true });
        Step(replay,fate.Tick);
        check(replay.StateDigest() == fate.StateDigest(),"corruption Fate replays identically");
    }

    private static int TicksToSecond(this SimWorld world) => SimWorld.TicksPerSecond+1;
}
