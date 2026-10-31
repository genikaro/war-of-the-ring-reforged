using Reforged.Simulation;
using Reforged.Presentation;

public static class DwarfAbilityChecks
{
    private const int W = 60, Forge = 30000;
    private static readonly AbilityDefinition Shield = new(26,3,"Shield Break","shield",true,AbilityKind.ShieldBreak,
        DurationTicks:400,RequiredTechnique:"shield",ArmorReductionMilli:1000,MaxStacks:5);
    private static readonly AbilityDefinition Edge = new(-1012,2,"Razored Edge","edge",true,AbilityKind.Cleave,
        RangeMm:5000,BonusScale:700000,RequiredTechnique:"edge",ChainTargets:1,ChainStartRangeMm:20000);
    private static readonly AbilityDefinition Fire = Edge with { Id = -1013,Slot = 3,Name = "Flaming Axe",Kind = AbilityKind.FlamingAxe,
        RequiredTechnique = "fire",FirePpmPerSecond = 350000 };
    private static readonly UnitDefinition Breaker = UnitDefinition.Probe with { Name = "breaker",MaxHealth = 10000,Abilities = [Shield] };
    private static readonly UnitDefinition Thrower = UnitDefinition.Probe with { Name = "thrower",MaxHealth = 10000,DpsMilli = 9000,
        ReachMm = 10000,MinimumRangeMm = 500,ProjectileSpeedMmPerSecond = 10000,MeleeDpsMilli = 7000,
        Attacks = [new("throw",1,20,[new(5,1000000)]),new("swing",1,20,[new(5,1000000)],true)],Abilities = [Edge,Fire] };
    private static readonly UnitDefinition Target = UnitDefinition.Probe with { Name = "target",MaxHealth = 10000,Passive = true,DpsMilli = 0 };
    private static int Cell(int x,int z = 10) => z*W+x;
    private static void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private static SimWorld Fixture()
    {
        var rules = new CombatRules(); rules.Units.Add(Breaker.Name,Breaker); rules.Units.Add(Thrower.Name,Thrower);
        var world = new SimWorld(new GridNavigation(W,20,1000,Enumerable.Repeat(true,W*20).ToArray(),new int[(W+1)*21]),rules);
        world.Economies.Add(0,new(5000,5000)); world.Economies.Add(1,new(5000,5000));
        world.BuildingRules.Add("forge",new("forge","Forge",null,0,0,20,10000,750000,750000,100000,50000,[]));
        world.ConstructionSites.Add(Forge,new(Forge,Forge,2000,"forge",40500,10500) { Team = 0,Health = 10000,Complete = true });
        world.Techniques.Add(new("shield","Shield Break","forge",3,100,100,40,"citadel2",null,new Dictionary<string,(int,int)> { [Breaker.Name] = (0,0) }));
        world.Techniques.Add(new("edge","Razored Edge","forge",2,100,100,40,"citadel3",null,new Dictionary<string,(int,int)> { [Thrower.Name] = (0,0) },
            MissileDpsMilli:new Dictionary<string,int> { [Thrower.Name] = 10000 }));
        world.Techniques.Add(new("fire","Flaming Axe","forge",2,125,125,40,"citadel4","edge",new Dictionary<string,(int,int)> { [Thrower.Name] = (0,0) },
            MissileDpsMilli:new Dictionary<string,int> { [Thrower.Name] = 10350 }));
        world.BuildingFireRules.Add("forge",new(7000,0,160000));
        return world;
    }

    public static void Run(Action<bool,string> check)
    {
        Research(check); Armor(check); Axes(check); Burning(check);
    }

    private static void Research(Action<bool,string> check)
    {
        Shield.Validate(); Edge.Validate(); Fire.Validate();
        var world = Fixture(); var veteran = world.Spawn(0,Cell(10),Breaker); var enemy = world.Spawn(1,Cell(30),Breaker);
        veteran.Order = enemy.Order = OrderKind.HoldGround;
        check(veteran.Ability(3) == Shield && !veteran.AbilityEnabled(Shield),"a Dwarf lists its locked passive before research");
        check(!world.CanResearch(0,Forge,0,out string missing) && missing == "Requires citadel2","Shield Break respects its Citadel prerequisite");
        world.StartBuildings.Add(0,["citadel2"]); var bank = world.Economies[0]; long food = bank.FoodMilli;
        world.Enqueue(0,[],OrderKind.Research,0,Forge); world.Step();
        check(!veteran.AbilityEnabled(Shield) && bank.FoodMilli == food-100000,"starting Shield Break pays without unlocking the passive early");
        world.Enqueue(0,[],OrderKind.CancelResearch,target:Forge); world.Step();
        check(!veteran.AbilityEnabled(Shield) && bank.FoodMilli == food,"cancelling Shield Break refunds and leaves the passive locked");
        world.Enqueue(0,[],OrderKind.Research,0,Forge); Step(world,40);
        var recruit = world.Spawn(0,Cell(12),Breaker);
        check(veteran.AbilityEnabled(Shield) && recruit.AbilityEnabled(Shield) && !enemy.AbilityEnabled(Shield),"research unlocks veterans and later recruits on its own team");
        var order = veteran.Order; world.EnqueueAbility(0,[veteran.Id],3,enemy.Id); world.Step();
        check(veteran.Order == order && !veteran.Casting && veteran.AbilityReadyAt[3] == 0,"a researched passive cannot be cast or consume cooldown");
        check(!world.CanResearch(0,Forge,2,out _),"Flaming Axe cannot bypass Razored Edge");
        world.StartBuildings[0].Add("citadel3"); world.InstantBuildTeams.Add(0);
        world.Enqueue(0,[],OrderKind.Research,1,Forge); world.Step();
        var axe = world.Spawn(0,Cell(5),Thrower);
        check(axe.AbilityEnabled(Edge) && !axe.AbilityEnabled(Fire) && axe.MeleeDamageRate == 10000,"Razored Edge unlocks independently and sets missile DPS to 10");
        check(!world.CanResearch(0,Forge,2,out string tier) && tier == "Requires citadel4","Flaming Axe waits for Citadel 4");
        world.StartBuildings[0].Add("citadel4"); world.Enqueue(0,[],OrderKind.Research,2,Forge); world.Step();
        check(axe.AbilityEnabled(Edge) && axe.AbilityEnabled(Fire) && axe.MeleeDamageRate == 10350 && axe.AxeAbility == Fire,
            "Flaming Axe replaces the weapon once while retaining Razored Edge");
    }

    private static void Armor(Action<bool,string> check)
    {
        SimWorld Duel(bool researched)
        {
            var world = Fixture(); if (researched) world.Researched.Add(0,["shield"]);
            var unit = world.Spawn(0,Cell(10),Breaker); var victim = world.Spawn(1,Cell(11),Target with { ArmorMilli = 3000 });
            world.Enqueue(0,[unit.Id],OrderKind.Attack,target:victim.Id); return world;
        }
        var locked = Duel(false); Step(locked,140);
        check(locked.Units[1].ShieldBreaks.Count == 0,"unresearched Shieldbreakers never reduce armor");
        var world = Duel(true); Step(world,140); var victim = world.Units[1]; var striker = world.Units[0];
        check(victim.ShieldBreaks.Count == 5 && victim.ArmorMilli == -2000,"Shield Break stacks to five and permits negative armor");
        var status = world.StatusesOf(victim).Single(s => s.Kind == StatusKind.ShieldBreak);
        check(status.Harmful && status.Scale == 5000 && UnitHudStats.From(victim,world.Rules).ArmorTip.Contains("-5 Shield Break"),
            "the target's harmful status and armor breakdown show the actual armor reduction");
        var text = UnitStatusText.From(world,victim,status,_ => "Dwarf");
        check(text.Tooltip.Contains("5 stacks") && text.Tooltip.Contains("s left"),"Shield Break's tooltip explains stacks and remaining duration");
        int firstExpiry = victim.ShieldBreaks.Min(s => s.Until),lastExpiry = victim.ShieldBreaks.Max(s => s.Until);
        string combatDigest = world.StateDigest();
        world.Enqueue(0,[striker.Id],OrderKind.Stop); striker.Health = 0;
        Step(world,firstExpiry-world.Tick+1);
        check(victim.ShieldBreaks.Count == 4 && victim.ArmorMilli == -1000,"each stack expires independently even after its source dies");
        Step(world,lastExpiry-world.Tick+1);
        check(victim.ArmorMilli == 3000 && !world.StatusesOf(victim).Any(s => s.Kind == StatusKind.ShieldBreak),"expired Shield Break restores armor and removes its status");
        var replay = Duel(true); Step(replay,140);
        check(firstExpiry >= 440 && lastExpiry-firstExpiry == 80 && replay.StateDigest() == combatDigest,
            "further hits recycle the oldest stacks and the combat replays identically");
        var digest = replay.StateDigest(); replay.Units[1].ShieldBreaks.Clear();
        check(digest != replay.StateDigest(),"the deterministic digest covers armor break timers");
    }

    private static void Axes(Action<bool,string> check)
    {
        (SimWorld World,SimProjectile Arrow) Shot(bool researched,bool ward = false,bool flaming = false)
        {
            var world = Fixture(); if (researched) world.Researched.Add(0,flaming ? ["edge","fire"] : ["edge"]);
            var unit = world.Spawn(0,Cell(10),Thrower); world.Spawn(1,Cell(18),Target);
            var behind = world.Spawn(1,Cell(21),Target with { ArmorMilli = 3000 });
            world.Spawn(1,Cell(21,13),Target); world.Spawn(1,Cell(24),Target); world.Spawn(0,Cell(20),Target);
            if (ward) { behind.MissileWardUntil = 10000; behind.MissileWardPpm = 10000; }
            world.Enqueue(0,[unit.Id],OrderKind.Attack,target:1);
            for (int i = 0; i < 100 && world.Projectiles.Count == 0; i++) world.Step();
            var arrow = world.Projectiles.Single(); world.Enqueue(0,[unit.Id],OrderKind.Stop); return (world,arrow);
        }
        var locked = Shot(false); Step(locked.World,40);
        check(locked.World.Units[2].Health == Target.MaxHealth,"ordinary axes hit only the chosen target");
        var upgraded = Shot(true); upgraded.World.Units[0].Health = 0; Step(upgraded.World,40);
        check(upgraded.World.Units[2].HealthMilli == Target.MaxHealth*1000L-4900,"Razored Edge hits one enemy behind the target at 70% after that victim's armor, even after shooter death");
        check(upgraded.World.Units.Skip(3).All(u => u.Health == Target.MaxHealth),"axe continuation excludes allies, off-line units and units beyond 5 m");
        var warded = Shot(true,true); Step(warded.World,40);
        check(warded.World.Units[2].HealthMilli == Target.MaxHealth*1000L-49,"Guardian Wind also protects against the follow-through hit");
        var snapshot = Shot(false); snapshot.World.Researched.Add(0,["edge"]); Step(snapshot.World,40);
        check(snapshot.World.Units[2].Health == Target.MaxHealth,"research cannot upgrade an axe already in flight");
        var flaming = Shot(true,flaming:true);
        check(flaming.Arrow.Style == "" && flaming.Arrow.FirePpm == 0 && flaming.Arrow.ChainTargets == 1,
            "Flaming Axe research keeps normal projectile visuals against units and retains follow-through");
        Step(flaming.World,flaming.Arrow.ImpactTick-flaming.World.Tick+1);
        check(flaming.World.Projectiles.Any(p => p.Target == 2) && flaming.World.Projectiles.All(p => p.Style == "" && p.FirePpm == 0),
            "follow-through hits also retain the normal axe appearance after Flaming Axe research");
        check(flaming.World.Projectiles.Single(p => p.Target == 2).RotationStartTick == flaming.Arrow.ReleaseTick,
            "the same axe keeps its original rotation clock through a follow-through hit");
        var close = Fixture(); close.Researched.Add(0,["edge","fire"]);
        var axeman = close.Spawn(0,Cell(10),Thrower); var near = close.Spawn(1,Cell(11),Target); var behindNear = close.Spawn(1,Cell(14),Target);
        close.Enqueue(0,[axeman.Id],OrderKind.Attack,target:near.Id); Step(close,35);
        check(near.Health < Target.MaxHealth && behindNear.Health == Target.MaxHealth && close.Projectiles.Count == 0,
            "melee axes keep their melee weapon and never gain missile continuation");
    }

    private static void Burning(Action<bool,string> check)
    {
        SimWorld Siege(bool flame)
        {
            var world = Fixture(); if (flame) world.Researched.Add(0,["edge","fire"]); else world.Researched.Add(0,["edge"]);
            var site = world.ConstructionSites[Forge]; site.Team = 1;
            var shooter = world.Spawn(0,Cell(33),Thrower);
            world.Enqueue(0,[shooter.Id],OrderKind.AttackStructure,target:Forge);
            for (int i = 0; i < 100 && world.Projectiles.Count == 0; i++) world.Step();
            var arrow = world.Projectiles.Single();
            check(arrow.Style == (flame ? "dwarf-flaming-axe" : ""),"projectile appearance snapshots the researched axe");
            world.Enqueue(0,[shooter.Id],OrderKind.Stop); Step(world,arrow.ImpactTick-world.Tick+1);
            check(world.CombatEvents.Any(e => e.Kind == CombatEventKind.Hit && e.Hit == 0),"an axe impact remains a weapon hit for sound playback");
            return world;
        }
        var normal = Siege(false); check(normal.ConstructionSites[Forge].FirePpm == 0,"Razored Edge does not set buildings alight");
        var fire = Siege(true); var building = fire.ConstructionSites[Forge]; int health = building.Health;
        check(building.FirePpm > 0,"Flaming Axe ignites an enemy building at impact");
        fire.Units[0].Health = 0;
        var damageEvents = new List<CombatEvent>();
        for (int i = 0; i < 20; i++) { fire.Step(); damageEvents.AddRange(fire.CombatEvents); }
        check(building.Health < health && building.FirePpm > 0,"building fire keeps damaging after the shooter dies");
        check(damageEvents.Any(e => e.Kind == CombatEventKind.Burn && e.DamageMilli > 0)
            && !damageEvents.Any(e => e.Kind == CombatEventKind.Hit),"ongoing fire emits Burn events without weapon impact sounds");
        Step(fire,40); health = building.Health; Step(fire,40);
        check(building.FirePpm == 0 && building.Health == health,"source extinguish rate ends fire and its damage");
        var replay = Siege(true); replay.Units[0].Health = 0; Step(replay,100);
        check(replay.StateDigest() == fire.StateDigest(),"Dwarf fire replays deterministically, including fractional damage");
    }

    public static void Cached(Action<bool,string> check,string cache)
    {
        var manifest = CacheManifest.Load(cache); manifest.Verify();
        var rules = CombatRules.Parse(File.ReadAllText(manifest.Resolve(manifest.Root.GetProperty("combat_rules").GetString()!)));
        var shield = rules.Units["UNIT_G_DWARVEN_SHIELDBREAKER"].Abilities!.Single();
        var axe = rules.Units["UNIT_G_DWARVEN_AXETHROWER"].Abilities!;
        check(shield is { Kind:AbilityKind.ShieldBreak,Slot:3,DurationTicks:400,MaxStacks:5,ArmorReductionMilli:1000 },"cached Shield Break retains source gear, stacking and duration");
        check(axe.Length == 2 && axe.All(a => a.Passive && a.RequiredTechnique != null) && axe.Single(a => a.Kind == AbilityKind.FlamingAxe).FirePpmPerSecond == 350000,
            "both cached axe passives retain research gates and source fire");
    }
}
