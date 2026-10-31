using System.Globalization;
using System.Text.Json;
using Reforged.Presentation;
using Reforged.Simulation;

/// <summary>Gameplay balance, per-hit HUD, and tower missiles against buildings, including no unit shooter.</summary>
public static class TowerChecks
{
    private const int W = 60,H = 40,Tower = 30000,Hall = 30001;
    private const string BoltWeapon = "WEAPON_BUILDING_G_SUPERTOWER_BOLT";
    private static readonly DefenseDefinition Arrows = new(14000,0,19000,[1000,1200,1400,1600,1750],0,4800,0,FatePpm:250000);
    private static readonly DefenseDefinition Bolt = new DefenseDefinition(35000,10000,22000,[100,200,300,400,500],0,4600,0)
        .ForGameplay(BoltWeapon);

    private static SimWorld Fixture(bool ballista = false)
    {
        var w = new SimWorld(new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
            obstacles:[new NavigationObstacle(Tower,[],[]),new NavigationObstacle(Hall,[],[]),new NavigationObstacle(Hall+1,[],[])]),CombatRules.Probe);
        w.BuildingRules.Add("tower",new("tower","Tower",null,0,0,60,500,0,0,0,0,[],2000));
        w.BuildingRules.Add("hall",new("hall","Hall",null,0,0,60,1000,0,0,0,0,[],4000));
        w.Defenses.Add("tower",Arrows);
        if (ballista) w.ExtraDefenses.Add("tower",[Bolt]);
        w.ConstructionSites.Add(Tower,new(Tower,Tower,1000,"tower",10500,15500) {Team=0,Health=500,Complete=true});
        return w;
    }

    private static ConstructionSite Building(SimWorld w,int id = Hall,int team = 1,int x = 22500,bool complete = true)
    {
        var s = new ConstructionSite(id,id,1000,"hall",x,15500) {Team=team,Health=1000,Complete=complete};
        w.ConstructionSites.Add(id,s); return s;
    }

    private static void Step(SimWorld w,int ticks) { for (int i=0;i<ticks;i++) w.Step(); }

    public static void Run(Action<bool,string> check)
    {
        var source = new DefenseDefinition(35000,10000,22000,[100,200,300,400,500],0,4600,0);
        check(source.DamagePerHitMilli==350000 && Bolt.DamagePerHitMilli==50000 && Bolt.FiringInterval==60,
            "prototype balance replaces the imported 350-damage/10-second bolt with exactly 50/3");
        check(source.ForGameplay("WEAPON_BUILDING_G_TOWER_ARROW")==source && Arrows.DamagePerHitMilli==14000,
            "the explicit bolt balance leaves the arrow weapon unchanged");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var hud = DefenseHudStats.From("Ballista",Bolt);
            check(hud.Attack=="50" && hud.Tooltip.Contains("Damage per second: 16.667") && hud.Tooltip.Contains("a hit every 3 s")
                && hud.Tooltip.Contains("Range: 10–22 m"),"tower HUD shows damage per hit with accurate cadence, DPS and range in the tooltip");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        var arrowWorld = Fixture(); var hall = Building(arrowWorld); arrowWorld.Step();
        check(arrowWorld.Projectiles is [{Unit:-1,Target:-1,Site:Hall,Source:Tower,SourceTeam:0,DamageMilli:10000}]
            && arrowWorld.ConstructionSites[Tower].DefenseSiteTarget==Hall,"Watchtower arrows target buildings and subtract their armour");
        Step(arrowWorld,15);
        check(hall.Health==990 && arrowWorld.CombatEvents.All(e=>e.Unit<0),"tower arrows land on buildings without requiring a unit shooter");

        var both = Fixture(true); var marked = Building(both); both.Step();
        check(both.Projectiles.Count==2 && both.Projectiles.Single(p=>p.Attack==1) is {Site:Hall,DamageMilli:38000},
            "both Ballista Tower weapons target buildings and the bolt subtracts armour over three seconds");
        var releases = new List<int> {0};
        for (int i=0;i<180;i++)
        {
            both.Step(); releases.AddRange(both.CombatEvents.Where(e=>e.Kind==CombatEventKind.Release && e.Attack==1).Select(e=>e.Tick));
        }
        check(releases.SequenceEqual([0,60,120,180]) && marked.Health<1000,"building shots keep the exact three-second bolt cadence");
        var replay = Fixture(true); Building(replay); Step(replay,both.Tick);
        check(replay.StateDigest()==both.StateDigest(),"tower building targets, cooldowns and projectiles replay identically");

        var minimum = Fixture(true); Building(minimum,x:15500); minimum.Step();
        check(minimum.Projectiles is [{Attack:0}],"a building inside the bolt minimum range is hit only by arrows");
        var outer = Fixture(true); Building(outer,x:31500); outer.Step();
        check(outer.Projectiles is [{Attack:1}],"a building beyond arrow range but within bolt range is hit only by the bolt");
        var distant = Fixture(true); Building(distant,x:33500); Step(distant,70);
        check(distant.Projectiles.Count==0,"neither weapon attacks a building beyond its maximum range");
        foreach (int team in new[]{-1,0})
        {
            var safe = Fixture(true); Building(safe,team:team); Step(safe,70);
            check(safe.Projectiles.Count==0 && safe.ConstructionSites[Hall].Health==1000,"towers ignore friendly and neutral structures, team="+team);
        }
        var foundation = Fixture(true); var unfinished = Building(foundation,complete:false); Step(foundation,15);
        check(unfinished.Health<1000,"enemy foundations can be hit by towers");
        var idle = Fixture(true); Building(idle); idle.ConstructionSites[Tower].Complete=false; Step(idle,70);
        check(idle.Projectiles.Count==0,"unfinished towers cannot shoot buildings");

        var choice = Fixture(true); Building(choice,x:21500);
        var unit = choice.Spawn(1,15*W+25,UnitDefinition.Probe with {DpsMilli=0,MaxHealth=1000}); unit.Order=OrderKind.HoldGround;
        choice.Step();
        check(choice.Projectiles.Count==2 && choice.Projectiles.All(p=>p.Target==unit.Id && p.Site<0),"towers choose enemy units before acquiring buildings");
        var retarget = Fixture(true); var first = Building(retarget,x:21500); var next = Building(retarget,Hall+1,x:22500);
        retarget.Step(); first.Team=0; Step(retarget,60);
        check(first.Health==952 && next.Health<1000 && retarget.ConstructionSites[Tower].DefenseSiteTarget==next.Id,
            "towers stop firing at a captured building and acquire the next enemy after already released shots land");

        var kill = Fixture(); var doomed = Building(kill); doomed.Health=1; kill.Step();
        // A launched tower projectile still lands and credits its team after the shooter is removed.
        kill.ConstructionSites.Remove(Tower); Step(kill,15);
        check(!kill.ConstructionSites.ContainsKey(Hall)
            && kill.Records[0].BuildingKills.GetValueOrDefault("Hall")==1 && kill.Records[1].BuildingLosses.GetValueOrDefault("Hall")==1,
            "a tower projectile can raze a building after its shooter dies and credits the correct team");
        var lost = Fixture(true); Building(lost); lost.Step(); lost.ConstructionSites.Remove(Hall); Step(lost,70);
        check(lost.Projectiles.Count==0 && lost.ConstructionSites[Tower].DefenseSiteTarget<0,"shots at a removed building expire and its target is cleared");
        var plate = Fixture(); Building(plate); plate.Defenses["tower"]=Arrows with {DpsMilli=1000}; Step(plate,30);
        check(plate.ConstructionSites[Hall].Health==1000,"armour that absorbs a tower shot does not leak one point of building damage");
        var fate = Fixture(); var asset = Building(fate);
        fate.FateRules = new(500,800000,1100000,1450000,1000000,[]); fate.Economies.Add(0,new(0,0));
        Step(fate,15);
        check(fate.Economies[0].FateProgressMicro==(1000-asset.Health)*fate.FatePerDamagePpm(0,10500,15500,250000),
            "tower building damage earns Fate with the arrow weapon's quarter-rate modifier");
    }

    public static void Cached(Action<bool,string> check,string cache)
    {
        var construction = CacheManifest.Load(cache).Root.GetProperty("skirmish").GetProperty("construction");
        var source = construction.GetProperty("defenses").GetProperty("BUILDING_G_TOWER_UPGRADE")[1];
        var origin = source.GetProperty("origin_mm");
        var actual = new DefenseDefinition(source.GetProperty("dps_milli").GetInt32(),source.GetProperty("minimum_range_mm").GetInt32(),
            source.GetProperty("range_mm").GetInt32(),source.GetProperty("rates_milli").EnumerateArray().Select(r=>r.GetInt32()).ToArray(),
            origin[0].GetInt32(),origin[1].GetInt32(),origin[2].GetInt32()).ForGameplay(source.GetProperty("weapon").GetString()!);
        check(actual.DamagePerHitMilli==50000 && actual.FiringInterval==60,"the existing content cache loads the new bolt balance without a rebuild");
    }
}
