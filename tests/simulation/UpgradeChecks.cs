using Reforged.Simulation;

/// <summary>Build-44 in-place upgrades: cost and refund, paused training, promotion and prerequisite lineage.</summary>
public static class UpgradeChecks
{
    public static void Run(Action<bool,string> check)
    {
        SimWorld Fixture()
        {
            var world = new SimWorld(new GridNavigation(30,16,1000,Enumerable.Repeat(true,480).ToArray(),new int[31*17]));
            world.Economies.Add(0,new(500,500)); world.Economies.Add(1,new(500,500));
            ConstructionDefinition Rules(string name,int food,int ore,int ticks,int health,params string[] required)
                => new(name,name,null,food,ore,ticks,health,750000,750000,100000,50000,required);
            world.BuildingRules.Add("keep1",Rules("keep1",250,250,100,750));
            world.BuildingRules.Add("keep2",Rules("keep2",115,105,20,1000,"keep1"));
            world.BuildingRules.Add("forge",Rules("forge",75,100,30,590,"keep2"));
            world.Placements.Add("forge",new([new(-1000,-1000),new(1000,-1000),new(1000,1000),new(-1000,1000)],-400,400,3000,false,false));
            world.Upgrades.Add("keep1","keep2");
            world.ProducedUnits.Add("keep1",["worker"]); world.ProducedUnits.Add("keep2",["worker","hero"]);
            world.PopulationProvided.Add("keep1",10); world.PopulationProvided.Add("keep2",10);
            world.StartBuildings.Add(0,["keep1"]);
            world.ConstructionSites.Add(10000,new(10000,10000,5000,"keep1",5000,5000) { Team = 0,Health = 700,Complete = true,Prebuilt = true });
            world.ProductionRules.Add("worker",new("worker","Worker",35,0,1,400));
            world.Producers.Add(10000,new(10000,0,5000,5000,0,["worker"]));
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }

        var world = Fixture(); var site = world.ConstructionSites[10000]; var producer = world.Producers[10000]; var bank = world.Economies[0];
        check(!world.CanStartBuilding(0,"forge",out string locked) && locked == "Requires keep2","a Tier 2 building names the missing upgrade");
        check(!world.CanUpgrade(1,10000,out _),"another team cannot upgrade the building");
        world.Enqueue(0,[],OrderKind.Train,0,10000); world.Step();
        world.Enqueue(0,[],OrderKind.Upgrade,target: 10000); world.Step();
        int progress = producer.ProgressTicks;
        check(site.Upgrading && bank.FoodMilli == 500_000-35_000-115_000 && bank.OreMilli == 500_000-105_000,"the upgrade pays the promoted building's cost");
        Step(world,5);
        check(producer.ProgressTicks == progress && producer.Queue.Count == 1 && site.UpgradeTicks == 6,"training waits while the building upgrades");
        world.Enqueue(0,[],OrderKind.CancelUpgrade,target: 10000); world.Step();
        check(!site.Upgrading && bank.FoodMilli == 350_000+86_250 && bank.OreMilli == 395_000+78_750 && producer.ProgressTicks == progress+1,
            "cancelling refunds the source share and training resumes");
        world.Enqueue(0,[],OrderKind.Upgrade,target: 10000); Step(world,21);
        check(site is { Building: "keep2",Health: 950,Upgrading: false } && world.StartBuildings[0].SetEquals(["keep2"])
            && producer.Units.SequenceEqual(["worker","hero"]),"promotion keeps damage, follows the start entry and takes the new unit list");
        check(world.Satisfies("keep2","keep1") && !world.Satisfies("keep1","keep2") && world.CanStartBuilding(0,"forge",out _)
            && world.PopulationCapacity(0) == 10,"the upgraded building still counts as what it was built as");
        check(!world.CanUpgrade(0,10000,out string none) && none == "Nothing to upgrade","the last level offers no upgrade");
        var replay = Fixture(); replay.Commands.AddRange(world.Commands); Step(replay,world.Tick);
        check(replay.StateDigest() == world.StateDigest(),"upgrades replay identically");
        replay.ConstructionSites[10000].UpgradeTicks++;
        check(replay.StateDigest() != world.StateDigest(),"digest includes upgrade progress");
    }
}
