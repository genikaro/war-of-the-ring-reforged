using Reforged.Simulation;
using Reforged.Presentation;
using Reforged.Input;
using System.Numerics;
using System.Text.Json;

static class BodyCollisionChecks
{
    private static UnitDefinition Body(int radius,int height = 2200) => UnitDefinition.Probe with {
        BodyRadiusMm = radius,BodyHeightMm = height,SelectionRadiusMm = 800,AttackGapMm = 500 };
    private static SimWorld Open() => new(new(40,24,1000,Enumerable.Repeat(true,960).ToArray(),new int[1025]));
    private static void Advance(SimWorld world,int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.Step();
            foreach (var a in world.Units) foreach (var b in world.Units.Where(u => u.Id > a.Id && u.Health > 0))
            {
                if (a.Health <= 0) continue;
                if (SimWorld.BodiesOverlap(a,b)) throw new Exception("variable bodies overlapped");
                foreach (double f in new[] {.25,.5,.75})
                {
                    var p = MovementPlayback.Sample(a.Movement,f); var q = MovementPlayback.Sample(b.Movement,f);
                    if (p.Y+a.Definition.BodyHeightMm <= q.Y || q.Y+b.Definition.BodyHeightMm <= p.Y) continue;
                    int gap = SimWorld.BodySeparation(a,b);
                    if ((p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z) < (long)gap*gap-4) throw new Exception("variable bodies overlapped during playback");
                }
            }
        }
    }
    public static void Run(Action<bool,string> check)
    {
        var spawn = Open(); spawn.Spawn(0,405,Body(927));
        try { spawn.Spawn(0,406,Body(693)); check(false,"overlapping unequal bodies must reject spawn"); }
        catch (ArgumentException) { check(true,"spawn uses the sum of unequal body radii"); }
        var small = spawn.Spawn(0,407,Body(350));
        check(!SimWorld.BodiesOverlap(spawn.Units[0],small),"unequal bodies can stand at a valid separation");
        try { Open().Spawn(0,0,Body(927)); check(false,"large body must fit map boundary"); }
        catch (ArgumentException) { check(true,"map boundary clearance uses body radius"); }

        var squad = Open(); foreach (int cell in new[] {403,405,407,483,485,487})
            squad.Spawn(0,cell,Body(squad.Units.Count == 0 ? 561 : 927));
        squad.Enqueue(0,Enumerable.Range(0,6),OrderKind.Move,425); Advance(squad,500);
        check(squad.Units.All(u => u.Goal < 0 && u.Order == OrderKind.Idle),"a mixed-size squad reaches separated formation slots");
        check(squad.Units.Select(u => u.Node).Distinct().Count() == 6,"large formations retain distinct arrival slots");
        var replay = Open(); foreach (int cell in new[] {403,405,407,483,485,487}) replay.Spawn(0,cell,Body(replay.Units.Count == 0 ? 561 : 927));
        replay.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(squad.Commands))!); Advance(replay,squad.Tick);
        check(squad.StateDigest() == replay.StateDigest(),"variable-size movement replays identically");

        var crowd = Open();
        for (int row=0;row<6;row++) for (int col=0;col<3;col++) crowd.Spawn(0,(3+row*2)*40+3+col*2,Body(927,2534));
        crowd.Enqueue(0,crowd.Units.Select(u => u.Id),OrderKind.Move,12*40+27);
        Advance(crowd,900);
        check(crowd.Units.All(u => u.Goal < 0),"eighteen large units complete a combined formation move");
        var settled = crowd.Units.Select(u => (u.X,u.Z)).ToArray(); Advance(crowd,80);
        check(settled.SequenceEqual(crowd.Units.Select(u => (u.X,u.Z))),"combined formation stays still after arrival");
        crowd.Enqueue(0,crowd.Units.Select(u => u.Id),OrderKind.Move,8*40+5); Advance(crowd,50);
        crowd.Enqueue(0,crowd.Units.Select(u => u.Id),OrderKind.Move,16*40+20); Advance(crowd,900);
        check(crowd.Units.All(u => u.Goal < 0),"eighteen large units finish after a mid-route redirect");

        var terrain = Enumerable.Repeat(false,100).ToArray();
        for (int z = 1; z < 9; z++) for (int x = 1; x < 9; x++) terrain[z*10+x] = x != 5 || z == 4;
        var door = new GridNavigation(10,10,1000,terrain,new int[121]);
        check(door.ForBody(350,1800).FindPath(42,47).Count > 0,"small body can use a one-cell door");
        check(door.ForBody(700,2200).FindPath(42,47).Count == 0,"large body cannot squeeze through a one-cell door");
        check(!door.ForBody(700,2200).CanTraverseGroundPoints(4500,4500,6500,4500),"ground smoothing cannot bypass body clearance");
        var decks = new GridNavigation(1,1,4000,[true],new int[4],[new(0,4000,[],ClearanceRadiusMm:500)]);
        check(decks.ForBody(450,1800).CanStand(1) && !decks.ForBody(700,2200).CanStand(1),"unsupported larger mesh bodies are rejected");

        var fight = Open(); foreach (int cell in new[] {403,405,407,483,485,487}) fight.Spawn(0,cell,Body(927));
        var enemy = fight.Spawn(1,410,Body(693)); enemy.Health = 10000; enemy.Cooldown = 10000;
        fight.Enqueue(0,Enumerable.Range(0,6),OrderKind.Attack,target:enemy.Id); Advance(fight,550);
        check(enemy.Health < 10000 && fight.Units.Take(6).Any(u => u.Attacking),"large melee attackers spread and deliver hits");
        var meleeCrowd = Open();
        for (int row=0;row<6;row++) for (int col=0;col<3;col++) meleeCrowd.Spawn(0,(3+row*2)*40+3+col*2,Body(927,2534));
        var surrounded = meleeCrowd.Spawn(1,10*40+23,Body(693,2197)); surrounded.Health=10000; surrounded.Cooldown=10000;
        meleeCrowd.Enqueue(0,Enumerable.Range(0,18),OrderKind.Attack,target:surrounded.Id); Advance(meleeCrowd,650);
        int crowdMoving=0; for (int tick=0;tick<80;tick++) { Advance(meleeCrowd,1); crowdMoving+=meleeCrowd.Units.Take(18).Count(u => u.Moving); }
        check(crowdMoving < 80,"excess large melee attackers settle instead of shuffling indefinitely");
        var giant = Open(); giant.Spawn(0,404,Body(1500)); var victim=giant.Spawn(1,408,Body(1200)); victim.Cooldown=10000;
        giant.Enqueue(0,[0],OrderKind.Attack,target:1); Advance(giant,160);
        check(victim.Health < 100,"melee can reach across bodies wider than the legacy center range");

        var slopeHeights = Enumerable.Range(0,41*25).Select(i => i%41*400+i/41*100).ToArray();
        var slope = new SimWorld(new(40,24,1000,Enumerable.Repeat(true,960).ToArray(),slopeHeights));
        foreach (int cell in new[] {403,405,407,483,485,487}) slope.Spawn(0,cell,Body(927,2534));
        var slopeEnemy = slope.Spawn(1,265,Body(693,2197)); slopeEnemy.Health = 10000; slopeEnemy.Cooldown = 10000;
        slope.Enqueue(0,Enumerable.Range(0,6),OrderKind.Attack,target:slopeEnemy.Id); Advance(slope,450);
        check(slope.Units.Take(6).Any(u => u.X > 10000) && slopeEnemy.Health < 10000,
            "close large allies spread on slopes without mixing horizontal and 3D spacing");

        var dynamicNav = new GridNavigation(10,10,1000,Enumerable.Repeat(true,100).ToArray(),new int[121],null,[new(5,[44],[])]);
        var wideNav = dynamicNav.ForBody(700,2200);
        check(!wideNav.CanStand(44),"body navigation inherits active obstacles");
        dynamicNav.SetObstacle(5,false);
        check(wideNav.CanStand(44) && wideNav.FindPath(42,46).Count > 0,"obstacle removal updates the existing body graph and route cache");
        dynamicNav.SetObstacle(5,true); check(!wideNav.CanStand(44),"obstacle restoration updates the existing body graph");

        var pick = Open(); var front=pick.Spawn(1,405,Body(693)); var back=pick.Spawn(0,485,Body(927));
        var origin=new Vector3(5.5f,1,0); var ray=Vector3.UnitZ;
        check(UnitPicker.Pick(pick.Units,origin,ray)?.Id == front.Id,"body picking chooses the nearest visible enemy");
        check(UnitPicker.Pick(pick.Units,origin,ray,2) == null,"body picking respects terrain occlusion distance");
        front.Health=0; check(UnitPicker.Pick(pick.Units,origin,ray)?.Id == back.Id,"dead units cannot intercept selection");
        check(UnitPicker.Pick(pick.Units,new(6.3f,2,0),ray)?.Id == back.Id,"larger and taller bodies are clickable away from the root marker");
        check(UnitPicker.Pick(pick.Units,origin,Vector3.Zero) == null,"degenerate selection rays are ignored");
    }

    public static void Cached(Action<bool,string> check,GridNavigation nav,CombatRules rules,JsonElement cache,JsonElement manifest)
    {
        nav.LoadBodyProfiles(cache);
        foreach (var definition in rules.Units.Values)
        {
            var graph = nav.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
            check(Enumerable.Range(nav.GroundCount,nav.NodeCount-nav.GroundCount).Any(n => !graph.CanStand(n) && nav.CanStand(n)),
                "real mesh clearance rejects narrow nodes for "+definition.DisplayName);
        }
        var world = new SimWorld(nav,rules);
        var knight = rules.Units["UNIT_G_ELF_KNIGHT"]; var hero=rules.Units["UNIT_G_HERO_ARAGORN"];
        var walk = nav.ForBody(knight.BodyRadiusMm,knight.BodyHeightMm);
        int start=Enumerable.Range(0,nav.GroundCount).First(n => walk.CanStand(n)
            && nav.Center(n).X > 210000 && nav.Center(n).X < 220000 && nav.Center(n).Z > 155000 && nav.Center(n).Z < 165000);
        var reserved=new HashSet<int>();
        for (int i=0;i<6;i++) { int node=walk.Nearest(start,reserved); reserved.Add(node); world.Spawn(0,node,i==0?hero:knight); }
        int goal=walk.Nearest(start+12,connectedTo:start); world.Enqueue(0,Enumerable.Range(0,6),OrderKind.Move,goal); Advance(world,600);
        check(world.Units.All(u => u.Goal < 0),"real-map mixed-size squad reaches distinct formation slots without body overlap");
        var army = new SimWorld(nav,rules);
        foreach (var spawn in manifest.GetProperty("spawns").EnumerateArray().Where(s => s.GetProperty("team").GetInt32() == 0))
        {
            string type=manifest.GetProperty("models")[spawn.GetProperty("model").GetInt32()].GetProperty("unit").GetString()!;
            army.Spawn(0,spawn.GetProperty("cell").GetInt32(),rules.Units[type]);
        }
        army.Enqueue(0,army.Units.Select(u => u.Id),OrderKind.Move,walk.Nearest(army.Units[8].Node+12)); Advance(army,1000);
        check(army.Units.All(u => u.Goal < 0),"all eighteen source-sized demo allies finish a combined real-map move");
        var encounter = new SimWorld(nav,rules);
        foreach (var spawn in manifest.GetProperty("spawns").EnumerateArray())
        {
            string type = manifest.GetProperty("models")[spawn.GetProperty("model").GetInt32()].GetProperty("unit").GetString()!;
            encounter.Spawn(spawn.GetProperty("team").GetInt32(),spawn.GetProperty("cell").GetInt32(),rules.Units[type]);
        }
        encounter.Step();
        check(encounter.Units.All(u => u.Target < 0 && !u.StrikeActive),"source-sized demo armies start outside automatic engagement range");
        check(encounter.Units.Where(u => u.Team == 0).Skip(12).All(u => u.Definition.Name == "UNIT_G_ELVEN_ARCHER"),
            "third friendly squad contains six Elven Archers");
    }
}
