using Reforged.Simulation;
using Reforged.Presentation;

static class RangedSpacingChecks
{
    private const int Width=64,Count=18;
    private static readonly UnitDefinition Archer=UnitDefinition.Probe with {
        Name="spacing-archer",BodyRadiusMm=535,BodyHeightMm=2000,FriendlyCompressionPpm=500_000,
        ReachMm=13000,MinimumRangeMm=1000,ProjectileSpeedMmPerSecond=25000,MeleeDpsMilli=12000,MeleeGapMm=500,
        Attacks=[new("shoot",1,20,[new(5,1_000_000)]),new("stab",1,20,[new(5,1_000_000)],true)] };
    private static readonly UnitDefinition Thrower=Archer with {
        Name="spacing-axethrower",BodyRadiusMm=624,ReachMm=10000,MinimumRangeMm=500,
        Attacks=[new("throw",1,26,[new(12,1_000_000)])] };

    private static SimWorld Packed(UnitDefinition[] profiles,bool distant=false,int count=Count)
    {
        var world=new SimWorld(new(Width,48,1000,Enumerable.Repeat(true,Width*48).ToArray(),new int[(Width+1)*49]));
        int stride=profiles.Max(d=>d.BodyRadiusMm)+100;
        for(int i=0;i<count;i++)
        {
            var unit=world.Spawn(0,(6+i/20*2)*Width+6+i%20*2,profiles[i%profiles.Length]);
            unit.X=12000+i%3*stride; unit.Z=18000+i/3*stride;
            unit.Node=world.Navigation.Cell(unit.X,unit.Z);
            unit.Facing=UnitFacing.FromDirection(1,0);
        }
        var target=world.Spawn(1,20*Width+(distant ? 42 : 21),UnitDefinition.Probe with {
            BodyRadiusMm=570,BodyHeightMm=2000,MaxHealth=1_000_000,MaxAttackers=1,Passive=true });
        world.Enqueue(1,[target.Id],OrderKind.HoldGround);
        world.Enqueue(0,Enumerable.Range(0,count),OrderKind.Attack,target:target.Id);
        return world;
    }

    private sealed record Report(int Shooters,double CrowdedPercent,double MinimumFiringDistance,int MaxSearches,string Digest);
    private static Report Measure(SimWorld world,Action<bool,string> check,string name)
    {
        var shooters=new HashSet<int>();
        int count=world.Units.Count-1;
        int crowded=0,samples=0,searches=0;
        double nearestShot=double.MaxValue;
        bool timing=true,safe=true;
        for(int tick=0;tick<800;tick++)
        {
            world.Step(); searches=Math.Max(searches,world.CombatSearchesLastStep);
            foreach(var release in world.CombatEvents.Where(e=>e.Kind==CombatEventKind.Release && e.Unit>=0))
            {
                var unit=world.Units[release.Unit]; var attack=unit.Definition.Attacks[release.Attack];
                shooters.Add(unit.Id);
                timing&=unit.Definition.FiresProjectile(release.Attack)
                    && release.Tick-release.StartTick==attack.Hits[release.Hit].Tick;
                nearestShot=Math.Min(nearestShot,Math.Sqrt(SimWorld.SeparationSquared(unit,world.Units[release.Target])));
            }
            if(tick%10!=0) continue;
            foreach(var unit in world.Units.Take(count))
            {
                double nearest=double.MaxValue;
                foreach(var other in world.Units.Take(count).Where(u=>u.Id!=unit.Id))
                {
                    double distance=Math.Sqrt(SimWorld.SeparationSquared(unit,other));
                    nearest=Math.Min(nearest,distance/(unit.Definition.BodyRadiusMm+other.Definition.BodyRadiusMm));
                    safe&=!SimWorld.BodiesOverlap(unit,other);
                    var p=MovementPlayback.Sample(unit.Movement,.5); var q=MovementPlayback.Sample(other.Movement,.5);
                    safe&=(p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)>=Math.Pow(SimWorld.BodySeparation(unit,other),2)-4;
                }
                if(tick>=400) { samples++; if(nearest<.80) crowded++; }
            }
        }
        double percentage=crowded*100d/samples;
        Console.WriteLine($"RANGED_SPACING {name} shooters={shooters.Count} crowded={percentage:F2}% nearest_shot={nearestShot:F0}mm searches={searches}");
        check(percentage<=2,name+": ranged ranks keep visible space after spreading or approaching");
        check(shooters.Count==count && nearestShot>4000,name+": every ranged soldier fires from range");
        check(timing,name+": spreading preserves source projectile release timing");
        check(safe,name+": ranged bodies stay separate at ticks and during interpolation");
        check(searches<=SimWorld.CombatSearchesPerTick,name+": ranged positioning uses the shared search cap");
        return new(shooters.Count,percentage,nearestShot,searches,world.StateDigest());
    }

    public static void Run(Action<bool,string> check)
    {
        Measure(Packed([Archer]),check,"packed archers");
        Measure(Packed([Thrower]),check,"packed axethrowers");
        Measure(Packed([Archer,Thrower]),check,"mixed ranged ranks");
        Measure(Packed([Archer,Thrower],count:64),check,"64 packed ranged soldiers");
        var world=Packed([Archer,Thrower],distant:true);
        var result=Measure(world,check,"distant mixed approach");
        var replay=Measure(Packed([Archer,Thrower],distant:true),check,"ranged replay");
        check(result.Digest==replay.Digest,"ranged spreading and approaching replay identically");
        var units=world.Units.Take(Count).Select(u=>u.Id).ToArray();
        world.Enqueue(0,units,OrderKind.Move,20*Width+8); world.Step();
        check(world.Units.Take(Count).All(u=>u.Order==OrderKind.Move && u.Target<0 && !u.StrikeActive),
            "Move immediately interrupts the spaced firing line");
        var hold=Packed([Archer,Thrower]);
        var anchors=hold.Units.Take(Count).Select(u=>(u.X,u.Z)).ToArray();
        hold.Enqueue(0,Enumerable.Range(0,Count),OrderKind.HoldGround);
        for(int tick=0;tick<80;tick++) hold.Step();
        check(hold.Units.Take(Count).Select((u,i)=>(u.X,u.Z)==anchors[i]).All(value=>value),
            "Hold Position keeps its anchors even in a compressed ranged group");
    }

    public static void Cached(Action<bool,string> check,string cache)
    {
        var rules=CombatRules.Parse(File.ReadAllText(CacheManifest.Load(cache).Resolve("combat/rules.json")));
        foreach(string name in new[] {"UNIT_G_ELVEN_ARCHER","UNIT_G_DWARVEN_AXETHROWER"})
            if(rules.Units.TryGetValue(name,out var profile)) Measure(Packed([profile]),check,"source "+name);
    }
}
