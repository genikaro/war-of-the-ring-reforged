using Reforged.Simulation;
using Reforged.Presentation;

static class RangerAbilityChecks
{
    private const int W = 80, Outpost = 2000;
    private static readonly AbilityDefinition Camo = new(14,0,"Camouflage","camo",false,AbilityKind.Camouflage,
        RechargeTicks:60,CastClip:"idle3",AlphaPpm:500000,RequiredTechnique:"camouflage");
    private static readonly AbilityDefinition Eagle = new(-1020,2,"Eagle Eye","eagle",true,AbilityKind.EagleEye,RequiredTechnique:"eagle");
    private static readonly AbilityDefinition Detection = new(-1053,3,"Detection","detection",true,AbilityKind.Detection);
    private static readonly UnitDefinition Ranger = UnitDefinition.Probe with { Name = "ranger",MaxHealth = 10000,
        Abilities = [Camo,Eagle,Detection],SightRangeMm = 20000,DetectionMask = 1 };
    private static readonly UnitDefinition Enemy = UnitDefinition.Probe with { Name = "enemy",MaxHealth = 10000 };
    private static int Cell(int x,int z = 10) => z*W+x;
    private static void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private static SimWorld Fixture(bool researched = false)
    {
        var world = new SimWorld(new GridNavigation(W,20,1000,Enumerable.Repeat(true,W*20).ToArray(),new int[(W+1)*21]));
        world.Economies.Add(0,new(1000,1000)); world.Economies.Add(1,new(1000,1000));
        world.BuildingRules.Add("outpost",new("outpost","Outpost",null,0,0,20,10000,750000,750000,100000,50000,[]));
        world.ConstructionSites.Add(Outpost,new(Outpost,Outpost,2000,"outpost",70500,10500) { Team = 0,Health = 10000,Complete = true });
        var bonus = new Dictionary<string,(int,int)> { [Ranger.Name] = (0,0) };
        world.Techniques.Add(new("camouflage","Camouflage","outpost",2,75,75,40,"citadel2",null,bonus));
        world.Techniques.Add(new("eagle","Eagle Eye","outpost",3,75,75,40,"citadel2",null,bonus,
            SightPpm:new Dictionary<string,int> { [Ranger.Name] = 2000000 }));
        if (researched) world.Researched.Add(0,["camouflage"]);
        return world;
    }

    public static void Run(Action<bool,string> check)
    {
        Ranger.Validate(); Research(check); Hiding(check); DetectionRange(check); Attack(check); Replay(check);
    }

    private static void Research(Action<bool,string> check)
    {
        var world = Fixture(); var ranger = world.Spawn(0,Cell(10),Ranger);
        var enemy = world.Spawn(1,Cell(60),Ranger);
        check(!ranger.AbilityEnabled(Camo) && !ranger.AbilityEnabled(Eagle) && ranger.AbilityEnabled(Detection),"Rangers list both locked upgrades and innate Detection");
        world.EnqueueAbility(0,[ranger.Id],0,-1); world.Step();
        check(!ranger.Camouflaged,"Camouflage cannot activate before research");
        check(!world.CanResearch(0,Outpost,0,out var reason) && reason == "Requires citadel2","Ranger tech requires Citadel 2");
        world.StartBuildings.Add(0,["citadel2"]); long food = world.Economies[0].FoodMilli;
        world.Enqueue(0,[],OrderKind.Research,0,Outpost); world.Step();
        check(!ranger.AbilityEnabled(Camo) && world.Economies[0].FoodMilli == food-75000,"Camouflage pays up front and stays locked while researching");
        world.Enqueue(0,[],OrderKind.CancelResearch,target:Outpost); world.Step();
        check(!ranger.AbilityEnabled(Camo) && world.Economies[0].FoodMilli == food,"cancelled Ranger research refunds and leaves the ability locked");
        world.Enqueue(0,[],OrderKind.Research,0,Outpost); Step(world,40);
        var recruit = world.Spawn(0,Cell(12),Ranger);
        check(ranger.AbilityEnabled(Camo) && recruit.AbilityEnabled(Camo) && !enemy.AbilityEnabled(Camo),"Camouflage unlocks existing and future Rangers only on the researching team");
        world.Enqueue(0,[],OrderKind.Research,1,Outpost); Step(world,40);
        var scout = world.Spawn(0,Cell(14),Ranger);
        check(ranger.SightRangeMm == 40000 && scout.SightRangeMm == 40000 && enemy.SightRangeMm == 20000,"Eagle Eye doubles veteran and recruit sight without upgrading opponents");
        world.EnqueueAbility(0,[ranger.Id,recruit.Id],0,-1); world.Step();
        check(ranger.Camouflaged && recruit.Camouflaged && ranger.AbilityReadyAt[0] == 0,"Camouflage group activation hides all selected Rangers without starting recharge");
    }

    private static void Hiding(Action<bool,string> check)
    {
        var world = Fixture(true); var ranger = world.Spawn(0,Cell(10),Ranger);
        var enemy = world.Spawn(1,Cell(11),Enemy);
        world.Enqueue(1,[enemy.Id],OrderKind.Attack,target:ranger.Id); world.Step();
        world.EnqueueAbility(0,[ranger.Id],0,-1); world.Step();
        check(ranger.Camouflaged && enemy.Target < 0 && !world.CanSeeUnit(1,ranger),"Camouflage drops an undetecting enemy's existing explicit attack");
        check(world.CanSeeUnit(0,ranger) && world.CanSeeUnit(-1,ranger),"the owner and observer can see a camouflaged Ranger");
        world.Enqueue(1,[enemy.Id],OrderKind.HoldGround);
        world.Enqueue(1,[enemy.Id],OrderKind.Attack,target:ranger.Id); Step(world,20);
        check(enemy.Target < 0 && ranger.Target < 0 && !ranger.StrikeActive,"a hidden Ranger cannot be acquired or retaliate automatically");
        var status = world.StatusesOf(ranger).Single(s => s.Kind == StatusKind.Camouflage);
        check(UnitStatusText.From(world,ranger,status,_ => "Ranger").Icon == Camo.Icon,"Camouflage has its own source status icon");
        world.Enqueue(0,[ranger.Id],OrderKind.Stop); world.Step();
        check(ranger.Camouflaged,"stopping a stationary Ranger preserves Camouflage");
        world.Enqueue(0,[ranger.Id],OrderKind.Attack,target:9999); world.Step();
        check(ranger.Camouflaged,"an invalid attack does not break Camouflage");
        world.Enqueue(0,[ranger.Id],OrderKind.Move,Cell(4)); world.Step();
        check(!ranger.Camouflaged && ranger.Moving && ranger.AbilityReadyAt[0] > world.Tick,"movement ends Camouflage and starts its recharge");
        int ready = ranger.AbilityReadyAt[0];
        check(ready == world.Tick-1+60 && world.StatusesOf(ranger).All(s => s.Kind != StatusKind.Camouflage),"Camouflage recharges for three seconds from deactivation and loses its status");
        world.EnqueueAbility(0,[ranger.Id],0,-1); world.Step();
        check(!ranger.Camouflaged,"Camouflage cannot bypass its recharge");
        world.Enqueue(0,[ranger.Id],OrderKind.Stop); Step(world,ready-world.Tick);
        world.EnqueueAbility(0,[ranger.Id],0,-1); world.Step();
        check(ranger.Camouflaged,"Camouflage is usable after its recharge");
        ranger.Health = 0; world.Step();
        check(!ranger.Camouflaged && world.StatusesOf(ranger).Count == 0,"death clears Camouflage");
    }

    private static void DetectionRange(Action<bool,string> check)
    {
        var world = Fixture(true); var hidden = world.Spawn(0,Cell(40),Ranger);
        var detector = world.Spawn(1,Cell(10),Ranger); detector.Order = OrderKind.HoldGround;
        world.EnqueueAbility(0,[hidden.Id],0,-1); world.Step();
        check(!world.CanSeeUnit(1,hidden),"normal Ranger Detection cannot reach a hidden enemy 30 m away");
        detector.TechniqueSightPpm = 2000000;
        check(world.CanSeeUnit(1,hidden) && hidden.Camouflaged,"Eagle Eye reveals a hidden enemy 30 m away without removing Camouflage");
        detector.TechniqueSightPpm = 1000000; detector.X = hidden.X-20000; detector.Z = hidden.Z;
        check(world.CanSeeUnit(1,hidden),"normal Detection includes its 20 m boundary");
        detector.X--;
        check(!world.CanSeeUnit(1,hidden),"a Ranger becomes hidden again when outside Detection");
        detector.X = hidden.X-2000;
        world.Enqueue(1,[detector.Id],OrderKind.Attack,target:hidden.Id); world.Step();
        check(detector.Target == hidden.Id && hidden.Camouflaged,"a detecting enemy can explicitly attack a camouflaged Ranger");
        detector.X = hidden.X-30000; world.Step();
        check(detector.Target < 0,"combat releases a hidden target when its detector leaves range");
        hidden.RingActive = true; detector.TechniqueSightPpm = 2000000;
        check(!world.CanSeeUnit(1,hidden),"Eagle Eye and Detection never reveal the One Ring");
        hidden.RingActive = false; detector.Health = 0;
        check(!world.CanSeeUnit(1,hidden),"a dead Ranger cannot detect hidden enemies");
    }

    private static void Attack(Action<bool,string> check)
    {
        var world = Fixture(true); var ranger = world.Spawn(0,Cell(10),Ranger);
        var enemy = world.Spawn(1,Cell(11),Enemy with { Passive = true });
        world.EnqueueAbility(0,[ranger.Id],0,-1); world.Step();
        world.Enqueue(0,[ranger.Id],OrderKind.Attack,target:enemy.Id);
        for (int i = 0; i < 30 && ranger.Camouflaged; i++) world.Step();
        check(!ranger.Camouflaged && ranger.Target == enemy.Id && ranger.AbilityReadyAt[0] > world.Tick,"a valid attack breaks Camouflage and starts recharge");
        var fresh = world.Spawn(0,Cell(20),Ranger);
        world.EnqueueAbility(1,[fresh.Id],0,-1); world.Step();
        check(!fresh.Camouflaged,"another team cannot activate a Ranger's Camouflage");
        world.EnqueueAbility(0,[fresh.Id],2,-1); world.Step();
        check(!fresh.Camouflaged && fresh.AbilityReadyAt[2] == 0,"Eagle Eye stays passive and cannot be cast");
    }

    private static void Replay(Action<bool,string> check)
    {
        string Play()
        {
            var world = Fixture(true); var ranger = world.Spawn(0,Cell(10),Ranger);
            world.EnqueueAbility(0,[ranger.Id],0,-1); world.Step();
            world.Enqueue(0,[ranger.Id],OrderKind.Move,Cell(20)); Step(world,80);
            world.EnqueueAbility(0,[ranger.Id],0,-1); Step(world,20);
            return world.StateDigest();
        }
        check(Play() == Play(),"Ranger abilities replay deterministically");
    }

    public static void Cached(Action<bool,string> check,string cache)
    {
        var manifest = CacheManifest.Load(cache); manifest.Verify();
        var rules = CombatRules.Parse(File.ReadAllText(manifest.Resolve(manifest.Root.GetProperty("combat_rules").GetString()!)));
        var ranger = rules.Units["UNIT_G_RANGER"];
        check(ranger.SightRangeMm == 20000 && ranger.DetectionMask == 1,"cached Rangers retain source sight and normal-invisibility detection");
        check(ranger.Abilities is { Length:3 } && ranger.Abilities.Single(a => a.Kind == AbilityKind.Camouflage) is
            { Slot:0,Passive:false,RechargeTicks:60,CastClip:"G_Ranger_Idle_03",RequiredTechnique:"TECHNIQUE_CAMOUFLAGE" },"cached Camouflage retains its research gate, slot, animation and cooldown");
        check(ranger.Abilities!.Single(a => a.Kind == AbilityKind.EagleEye) is { Slot:2,Passive:true,RequiredTechnique:"TECHNIQUE_EAGLE_EYE" }
            && ranger.Abilities!.Single(a => a.Kind == AbilityKind.Detection) is { Slot:3,Passive:true,RequiredTechnique:null },"cached Eagle Eye and innate Detection retain their passive command-panel slots");
    }
}
