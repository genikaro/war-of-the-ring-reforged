using Reforged.Simulation;

/// <summary>Build-46 research: cost, one at a time, cancel refund, bonuses on living and later units, damage and armor.</summary>
public static class ResearchChecks
{
    public static void Run(Action<bool,string> check)
    {
        SimWorld Fixture()
        {
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe);
            var world = new SimWorld(new GridNavigation(30,16,1000,Enumerable.Repeat(true,480).ToArray(),new int[31*17]),rules);
            world.Economies.Add(0,new(500,500)); world.Economies.Add(1,new(500,500));
            world.BuildingRules.Add("forge",new("forge","Forge",null,75,100,30,590,750000,750000,100000,50000,[]));
            world.ConstructionSites.Add(30000,new(30000,30000,3000,"forge",20500,8500) { Team = 0,Health = 590,Complete = true });
            world.Techniques.Add(new("blades","Blades","forge",0,75,75,40,null,null,new Dictionary<string,(int,int)> { ["probe"] = (1000,0) }));
            world.Techniques.Add(new("plating","Plating","forge",1,75,75,40,null,null,new Dictionary<string,(int,int)> { ["probe"] = (0,1000) }));
            world.Techniques.Add(new("steel","Steel","forge",0,125,125,40,"keep3","blades",new Dictionary<string,(int,int)> { ["probe"] = (2000,0) }));
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }

        var world = Fixture(); var bank = world.Economies[0]; var forge = world.ConstructionSites[30000];
        var veteran = world.Spawn(0,2*30+2);
        check(world.ResearchOptions(forge).Select(t => t.Name).SequenceEqual(["blades","plating"]),"a chain offers its first step only");
        check(!world.CanResearch(1,30000,0,out _),"another team cannot research at the building");
        world.Enqueue(0,[],OrderKind.Research,0,30000); world.Step();
        check(forge.Research == "blades" && bank.FoodMilli == 425_000 && bank.OreMilli == 425_000,"research pays its source cost on request");
        check(!world.CanResearch(0,30000,1,out string busy) && busy == "Research in progress","a building researches one technique at a time");
        world.Enqueue(0,[],OrderKind.CancelResearch,target: 30000); world.Step();
        check(forge.Research == null && bank.FoodMilli == 500_000 && bank.OreMilli == 500_000,"cancelling refunds the full cost");
        world.Enqueue(0,[],OrderKind.Research,0,30000); Step(world,39);
        check(forge.Research == "blades" && veteran.TechniqueDpsMilli == 0,"the bonus waits for the research to finish");
        world.Step();
        check(world.HasResearched(0,"blades") && forge.Research == null && veteran.TechniqueDpsMilli == 1000
            && veteran.MeleeDamageRate == UnitDefinition.Probe.DpsMilli+1000,"finished research raises the attack of units already trained");
        int recruitTick = world.Tick; var recruit = world.Spawn(0,4*30+2);
        check(recruit.TechniqueDpsMilli == 1000 && world.Spawn(1,6*30+2).TechniqueDpsMilli == 0,"later units of the team get it, other teams do not");
        check(world.ResearchOptions(forge).Select(t => t.Name).SequenceEqual(["steel","plating"])
            && !world.CanResearch(0,30000,2,out string locked) && locked == "Requires keep3","the next chain step appears with its own prerequisite");
        world.Enqueue(0,[],OrderKind.Research,1,30000); Step(world,40);
        check(veteran.ArmorMilli == UnitDefinition.Probe.ArmorMilli+1000 && recruit.ArmorMilli == veteran.ArmorMilli,"armor research adds armor");
        var replay = Fixture(); replay.Spawn(0,2*30+2); replay.Commands.AddRange(world.Commands);
        Step(replay,recruitTick); replay.Spawn(0,4*30+2); replay.Spawn(1,6*30+2); Step(replay,world.Tick-recruitTick);
        check(replay.StateDigest() == world.StateDigest(),"research replays identically");
        replay.Units[0].TechniqueArmorMilli++;
        check(replay.StateDigest() != world.StateDigest(),"digest includes research bonuses");

        // A researched attack bonus reaches the damage formula: the same strike against armor 0 deals more.
        long Strike(bool researched)
        {
            var duel = Fixture(); if (researched) duel.Researched.Add(0,["blades"]);
            var attacker = duel.Spawn(0,8*30+10); var target = duel.Spawn(1,8*30+11); target.Order = OrderKind.HoldGround;
            duel.Enqueue(0,[attacker.Id],OrderKind.Attack,-1,target.Id);
            for (int i = 0; i < 400 && target.HealthMilli == target.MaxHealth*1000L; i++) duel.Step();
            return target.MaxHealth*1000L-target.HealthMilli;
        }
        long plain = Strike(false),sharpened = Strike(true);
        check(plain > 0 && sharpened > plain,"a researched attack bonus raises melee damage");
    }
}
