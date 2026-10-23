using Reforged.Simulation;

static class RangedChecks
{
    private static UnitDefinition Archer => UnitDefinition.Probe with {
        Id = 2,Name = "archer",DpsMilli = 20000,ReachMm = 13000,MinimumRangeMm = 1000,
        ProjectileSpeedMmPerSecond = 10000,BodyRadiusMm = 535,BodyHeightMm = 2000,
        Attacks = [new("shoot",1,20,[new(5,1000000)])] };
    private static SimWorld Fixture(int distance = 8,OrderKind order = OrderKind.Attack)
    {
        var world = new SimWorld(new(32,10,1000,Enumerable.Repeat(true,320).ToArray(),new int[363]));
        world.Spawn(0,99,Archer); world.Spawn(2,99+distance,UnitDefinition.Probe with {BodyHeightMm = 2000});
        world.Units[0].Facing = UnitFacing.FromDirection(distance*1000,0);
        world.Enqueue(2,[1],OrderKind.HoldGround); world.Units[1].Cooldown = 10000;
        world.Enqueue(0,[0],order,target:order == OrderKind.Attack ? 1 : -1);
        return world;
    }
    private static void Advance(SimWorld world,int count) { for (int i = 0; i < count; i++) world.Step(); }
    private static SimWorld Dual(int distance = 8)
    {
        var world = Fixture(distance);
        world.Units[0].Definition = Archer with {MeleeDpsMilli = 30000,MeleeGapMm = 500,
            Attacks = [Archer.Attacks[0],new("melee",1,12,[new(3,1000000)],Melee:true)]};
        return world;
    }
    private static void PlaceTarget(SimWorld world,int node)
    {
        var target = world.Units[1]; target.Node = node;
        (target.X,target.Z) = world.Navigation.Center(node); target.Y = world.Navigation.NodeHeight(node);
    }

    /// <summary>The skirmish cache's Dwarf Axethrower throws through its decoded source release only.</summary>
    public static void Cached(Action<bool,string> check,string cache)
    {
        var rules = CombatRules.Parse(File.ReadAllText(CacheManifest.Load(cache).Resolve("combat/rules.json")));
        if (!rules.Units.TryGetValue("UNIT_G_DWARVEN_AXETHROWER",out var axe)) return;
        var world = new SimWorld(new(40,10,1000,Enumerable.Repeat(true,400).ToArray(),new int[41*11]),rules);
        world.Spawn(0,4*40+5,axe); world.Spawn(2,4*40+13,UnitDefinition.Probe with {BodyHeightMm = 2000});
        world.Units[0].Facing = UnitFacing.FromDirection(8000,0);
        world.Enqueue(2,[1],OrderKind.HoldGround); world.Units[1].Cooldown = 10000;
        world.Enqueue(0,[0],OrderKind.Attack,target: 1);
        CombatEvent? release = null; long health = world.Units[1].HealthMilli;
        for (int i = 0; i < 200 && release == null; i++) { world.Step(); release = world.CombatEvents.FirstOrDefault(e => e.Kind == CombatEventKind.Release); }
        var attack = release == null ? null : axe.Attacks[release.Attack];
        check(release != null && attack is { Clip: "G_Dwarven_Axethrower_Attack_Missile_01",Melee: false } && release.Tick-release.StartTick == attack.Hits[0].Tick
            && world.Projectiles.Count == 1,"the cached Axethrower releases an axe at its decoded source throw event");
        Advance(world,40);
        check(world.Units[1].HealthMilli < health && world.Projectiles.Count <= 1,"the thrown axe damages its target on arrival");
        check(axe.Attacks.All(a => a.Clip != "G_Dwarven_Axethrower_Attack_Missile_02"),"the throw with an undecoded release event is not used");
    }

    public static void Run(Action<bool,string> check)
    {
        var contact = Dual(2); contact.Step();
        check(contact.Units[0].Definition.Attacks[contact.Units[0].AttackVariant].Melee,
            "archer selects a melee clip at physical melee reach");
        Advance(contact,3);
        check(contact.Units[1].HealthMilli == 82000 && contact.Projectiles.Count == 0,
            "close combat uses melee DPS and exact melee hit timing without an arrow");
        PlaceTarget(contact,107); contact.Step();
        check(!contact.Units[0].StrikeActive && contact.Units[0].Cooldown > 0,
            "leaving melee cancels its pending timeline and preserves recovery");
        Advance(contact,8);
        check(contact.Units[0].StrikeActive && contact.Units[0].Definition.FiresProjectile(contact.Units[0].AttackVariant),
            "archer returns to its bow after melee contact ends");
        Advance(contact,5);
        check(contact.Projectiles.Count == 1,"returning to the bow releases on its own source event");

        var interrupted = Dual(); Advance(interrupted,3); PlaceTarget(interrupted,101); interrupted.Step();
        check(!interrupted.Units[0].StrikeActive && interrupted.CombatEvents.Any(e => e.Kind == CombatEventKind.Cancel),
            "melee contact interrupts an unreleased bow shot");
        Advance(interrupted,20);
        check(interrupted.Units[1].HealthMilli == 82000 && interrupted.Projectiles.Count == 0,
            "weapon switch retains cooldown and deals melee damage without the canceled arrow");
        var airborne = Dual(); Advance(airborne,6); PlaceTarget(airborne,101); Advance(airborne,18);
        check(airborne.Units[1].HealthMilli == 62000 && airborne.Projectiles.Count == 0,
            "switching to melee preserves released arrows and resolves both damage timelines");

        var meleeCap = Dual(2); meleeCap.Units[1].Definition = meleeCap.Units[1].Definition with {MaxAttackers = 1};
        meleeCap.Spawn(0,103,meleeCap.Units[0].Definition); meleeCap.Enqueue(0,[2],OrderKind.Attack,target:1); meleeCap.Step();
        check(meleeCap.Units[0].StrikeActive && !meleeCap.Units[2].StrikeActive,
            "archers in melee consume the target's normal melee attacker slots");
        var mixed = Dual(); mixed.Units[1].Definition = mixed.Units[1].Definition with {MaxAttackers = 1};
        mixed.Spawn(0,109); mixed.Enqueue(0,[2],OrderKind.Attack,target:1); mixed.Step();
        check(mixed.Units[0].StrikeActive && mixed.Units[2].StrikeActive,
            "a distant bow user does not consume an infantry melee slot");
        var threat = Dual(); Advance(threat,3); threat.Spawn(2,101); threat.Units[2].Health = 10;
        threat.Units[2].Cooldown = 10000; threat.Enqueue(2,[2],OrderKind.HoldGround); threat.Step();
        check(threat.Units[0].Target == 2 && !threat.Units[0].StrikeActive,
            "a close melee threat interrupts shooting at a different distant target");
        Advance(threat,50);
        check(threat.Units[2].Health == 0 && threat.Units[0].Target == 1,
            "archer returns to its explicit distant target after defeating the melee threat");
        var longReach = Dual(3);
        longReach.Units[1].Definition = longReach.Units[1].Definition with {ReachMm = 3500};
        longReach.Enqueue(2,[1],OrderKind.Attack,target:0); Advance(longReach,6);
        check(longReach.Projectiles.Count == 0 && longReach.Units[0].X > 3500,
            "enemy melee engagement beyond the archer's own reach makes it close for melee instead of firing");

        var world = Fixture(); Advance(world,5);
        check(world.Projectiles.Count == 0 && world.Units[1].Health == 100,"bow windup has no arrow or damage");
        world.Step(); var arrow = world.Projectiles.Single();
        check(world.CombatEvents.Any(e => e.Kind == CombatEventKind.Release && e.Tick == 5)
            && world.Units[1].Health == 100,"source release tick launches without instant damage");
        check(world.Units[0].X == 3500 && !world.Units[0].Moving,"archer fires from range without closing to melee");
        Advance(world,arrow.ImpactTick-world.Tick);
        check(world.Units[1].Health == 100,"arrow never damages before arrival");
        world.Step();
        check(world.Units[1].Health == 80 && world.CombatEvents.Any(e => e.Kind == CombatEventKind.Hit && e.Tick == arrow.ImpactTick),
            "arrival applies snapshot cycle damage exactly once");

        var cancel = Fixture(); Advance(cancel,3); cancel.Enqueue(0,[0],OrderKind.Move,289); Advance(cancel,30);
        check(cancel.Projectiles.Count == 0 && cancel.Units[1].Health == 100,"movement before release cancels the arrow");
        var released = Fixture(); Advance(released,6); released.Enqueue(0,[0],OrderKind.Move,97); Advance(released,25);
        check(released.Units[1].Health == 80,"movement after release preserves the airborne arrow");
        var shooterDead = Fixture(); Advance(shooterDead,6); shooterDead.Units[0].Health = 0; Advance(shooterDead,25);
        check(shooterDead.Units[1].Health == 80 && shooterDead.Projectiles.Count == 0,"airborne arrow survives shooter death");
        var victimDead = Fixture(); Advance(victimDead,6); victimDead.Units[1].Health = 0; Advance(victimDead,25);
        check(victimDead.Units[0].KillCount == 0 && victimDead.Projectiles.Count == 0,"dead targets consume arrows without duplicate victories");
        var lethal = Fixture(); lethal.Units[1].Health = 10; Advance(lethal,24);
        check(lethal.Units[0].KillCount == 1 && lethal.Units[1].Health == 0,"arrow kill credits its shooter once");
        var queued = Fixture(); queued.Units[1].Health = 10;
        queued.Enqueue(0,[0],OrderKind.Move,97,queued:true); Advance(queued,65);
        check(queued.Units[1].Health == 0 && queued.Units[0].Node == 97 && queued.Units[0].OrderQueue.Count == 0,
            "arrow kill releases a queued move after an explicit attack");
        var march = Fixture(); march.Units[1].Health = 10;
        march.Enqueue(0,[0],OrderKind.AttackMove,115); Advance(march,180);
        check(march.Units[1].Health == 0 && march.Units[0].Node == 115 && march.Units[0].Goal < 0,
            "attack-move resumes its destination after a ranged kill");
        var friendly = new SimWorld(Fixture().Navigation); friendly.Spawn(0,99,Archer);
        friendly.Spawn(0,107); friendly.Units[1].Health = 10;
        friendly.Units[0].Facing = UnitFacing.FromDirection(8000,0);
        friendly.Enqueue(0,[0],OrderKind.ForceAttack,target:1); Advance(friendly,24);
        check(friendly.Units[1].Health == 0 && friendly.Units[0].KillCount == 0,"friendly force-fire resolves arrows without a victory");

        var hold = Fixture(16,OrderKind.HoldGround); Advance(hold,60);
        check(hold.Units[0].X == 3500 && hold.Projectiles.Count == 0 && hold.Units[1].Health == 100,"Hold never chases beyond bow range");
        var acquire = Fixture(12,OrderKind.HoldGround); Advance(acquire,32);
        check(acquire.Units[1].Health < 100 && acquire.Units[0].X == 3500,"Hold acquires throughout the full thirteen-meter range");
        var approach = Fixture(16); Advance(approach,65);
        check(approach.Units[0].X > 3500 && approach.Units[0].X < 10000 && approach.Units[1].Health < 100,
            "explicit attack approaches only until a firing position is available");
        var fleeing = Fixture(); Advance(fleeing,6); fleeing.Enqueue(2,[1],OrderKind.Move,126); Advance(fleeing,20);
        check(fleeing.Units[1].Health == 80,"released arrow follows a moving target");

        var cap = Fixture(); cap.Units[1].Definition = cap.Units[1].Definition with {MaxAttackers = 1};
        cap.Spawn(0,35,Archer); cap.Units[2].Facing = UnitFacing.FromDirection(8000,2000);
        cap.Enqueue(0,[2],OrderKind.Attack,target:1); Advance(cap,6);
        check(cap.Projectiles.Count == 2,"melee attacker cap does not reserve or limit bow shots");
        var stop = Fixture(); Advance(stop,3); stop.Enqueue(0,[0],OrderKind.Stop); stop.Step();
        check(!stop.Units[0].StrikeActive && stop.Units[0].Cooldown > 0,"Stop cancels bow windup without resetting cadence");

        var cells = Enumerable.Repeat(true,320).ToArray(); cells[103] = false;
        var wall = new SimWorld(new(32,10,1000,cells,new int[363]));
        wall.Spawn(0,99,Archer); wall.Spawn(2,107); wall.Enqueue(0,[0],OrderKind.HoldGround); Advance(wall,30);
        check(wall.Projectiles.Count == 0 && wall.Units[1].Health == 100,"blocked terrain prevents shooting through walls");
        var nav = new GridNavigation(3,1,1000,[true,true,true],new int[8], [new(1,2000,[])]);
        check(!nav.HasLineOfFire(500,1000,500,2500,3000,500),"arrows cannot pass through a deck plane");
        check(nav.HasLineOfFire(500,3000,500,2500,3000,500),"clear sight above a deck is allowed");
        var obstacleNav = new GridNavigation(3,1,1000,[true,true,true],new int[8],null,[new(1,[1],[])]);
        check(!obstacleNav.HasLineOfFire(500,1000,500,2500,1000,500),"active footprints block arrows");
        obstacleNav.SetObstacle(1,false);
        check(obstacleNav.HasLineOfFire(500,1000,500,2500,1000,500),"removing cover opens line of fire");

        var a = Fixture(); var b = Fixture(); var traceA = new List<CombatEvent>(); var traceB = new List<CombatEvent>();
        for (int tick = 0; tick < 70; tick++) { a.Step(); b.Step(); traceA.AddRange(a.CombatEvents); traceB.AddRange(b.CombatEvents); }
        check(a.StateDigest() == b.StateDigest() && traceA.SequenceEqual(traceB),"projectile travel and combat events replay deterministically");
        var flight = Fixture(); Advance(flight,6); string before = flight.StateDigest(); flight.Projectiles.Clear();
        check(before != flight.StateDigest(),"airborne projectiles are part of deterministic state");
        foreach (var invalid in new[] {Archer with {MinimumRangeMm = 13000},Archer with {ProjectileSpeedMmPerSecond = -1}})
        {
            try { invalid.Validate(); check(false,"invalid ranged profile must fail"); }
            catch (ArgumentException) { check(true,"invalid ranged profile rejected"); }
        }
    }
}
