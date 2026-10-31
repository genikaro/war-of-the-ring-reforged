using System.Text.Json;
using Reforged.Simulation;

static class ElvenAbilityChecks
{
    private const int W = 100;
    private static readonly AbilityDefinition Protection = new(24,0,"Protection","shield",false,AbilityKind.Protection,
        RechargeTicks:700,RangeMm:10000,CastClip:"protection",CastTicks:31,EffectTick:13,DurationTicks:1000,ShieldMilli:150000);
    private static readonly AbilityDefinition Dispel = new(23,1,"Negate Magic","dispel",false,AbilityKind.Dispel,
        RechargeTicks:200,RangeMm:15000,CastClip:"dispel",CastTicks:34,EffectTick:13,AreaRadiusMm:7000,RequiredTechnique:"dispel");
    private static readonly AbilityDefinition Light = new(9,2,"Light","light",false,AbilityKind.SearingLight,
        RechargeTicks:900,RangeMm:16000,CastClip:"light",CastTicks:31,EffectTick:3,AreaRadiusMm:5000,DpsMilli:22000,DurationTicks:100,RequiredTechnique:"light");
    private static readonly AbilityDefinition Cloak = new(-1022,2,"Cloak","cloak",true,AbilityKind.ElvenCloak,AlphaPpm:500000,RequiredTechnique:"cloak");
    private static readonly UnitDefinition Bearer = UnitDefinition.Probe with { Name="bearer",Abilities=[Protection,Dispel,Light],MaxHealth=10000 };
    private static readonly UnitDefinition Archer = UnitDefinition.Probe with { Name="archer",Abilities=[Cloak],MaxHealth=10000 };
    private static readonly UnitDefinition Dummy = UnitDefinition.Probe with { Name="dummy",Passive=true,MaxHealth=10000 };
    private static int Cell(int x,int z = 15) => z*W+x;
    private static void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private static SimWorld World(bool researched = true)
    {
        var world = new SimWorld(new GridNavigation(W,40,1000,Enumerable.Repeat(true,W*40).ToArray(),new int[(W+1)*41]));
        if (researched) world.Researched.Add(0,["light","dispel","cloak"]);
        return world;
    }
    private static void Hold(SimUnit unit) => unit.Order = OrderKind.HoldGround;
    private static void Ground(SimWorld world,SimUnit caster,AbilityDefinition ability,int x,int z = 15500)
        => world.EnqueueGroundAbility(caster.Team,caster.Id,ability.Slot,world.Navigation.Cell(x,z),x,z);

    public static void Run(Action<bool,string> check)
    {
        Protection.Validate(); Dispel.Validate(); Light.Validate(); Cloak.Validate();
        Shields(check); Area(check); Dispelling(check); Cloaks(check); Orders(check);
        string Replay() { var w=World(); var s=w.Spawn(0,Cell(10),Bearer); w.Spawn(1,Cell(22),Dummy); Ground(w,s,Light,22500); Step(w,120); return w.StateDigest(); }
        check(Replay()==Replay(),"Elven casts, shields and searing damage replay deterministically");
    }

    private static void Shields(Action<bool,string> check)
    {
        var w=World(); var s=w.Spawn(0,Cell(10),Bearer); var ally=w.Spawn(0,Cell(12),Dummy); var enemy=w.Spawn(1,Cell(30),Dummy);
        check(w.ValidAbilityTarget(s,Protection,ally) && !w.ValidAbilityTarget(s,Protection,enemy),"Protection accepts healthy allies and rejects enemies");
        w.EnqueueAbility(0,[s.Id],0,ally.Id); Step(w,15);
        check(ally.ProtectionMilli==150000 && ally.HealthMilli==10000000,"Protection creates a separate 150 HP buffer at its source toolscript event");
        check(w.StatusesOf(ally).Any(x=>x.Kind==StatusKind.Protection),"Protection appears in the status bar");
        ally.SearingEffects.Add(new(enemy.Id,enemy.Team,Light,w.Tick+200)); Step(w,200);
        check(ally.ProtectionMilli==0 && ally.HealthMilli==9930000,$"shield absorbs the first 150 of 220 spell damage and spills only the remainder into health: shield {ally.ProtectionMilli}, health {ally.HealthMilli}");
        ally.ProtectionMilli=150000; ally.ProtectionUntil=w.Tick+1; Step(w,2);
        check(ally.ProtectionMilli==0 && !w.StatusesOf(ally).Any(x=>x.Kind==StatusKind.Protection),"unused Protection expires and loses its status");
        var melee=w.Spawn(1,Cell(13),UnitDefinition.Probe with { DpsMilli=100000,MaxHealth=10000 });
        ally.ProtectionMilli=150000; ally.ProtectionUntil=w.Tick+1000; long health=ally.HealthMilli;
        w.Enqueue(1,[melee.Id],OrderKind.Attack,target:ally.Id); Step(w,30);
        check(ally.ProtectionMilli<150000 && ally.HealthMilli<=health,"Protection also intercepts melee weapon hits");
        var p=World(); var shooter=p.Spawn(0,Cell(10),Bearer); var victim=p.Spawn(1,Cell(22),Dummy);
        victim.ProtectionMilli=150000; victim.ProtectionUntil=1000;
        p.Projectiles.Add(new(0,shooter.Id,victim.Id,0,0,200000,0,0,0,shooter.X,1000,shooter.Z,victim.X,1000,victim.Z)); p.Step();
        check(victim.ProtectionMilli==0 && victim.HealthMilli==9950000,"Protection absorbs projectile impacts and preserves damage overflow");
    }

    private static void Area(Action<bool,string> check)
    {
        var w=World(); var s=w.Spawn(0,Cell(10),Bearer); Hold(s);
        var inside=w.Spawn(1,Cell(22),Dummy); var boundary=w.Spawn(1,Cell(27),Dummy); var outside=w.Spawn(1,Cell(28),Dummy);
        var ally=w.Spawn(0,Cell(21,17),Dummy); var neutral=w.Spawn(-1,Cell(22,17),Dummy);
        Ground(w,s,Light,22500); Step(w,5);
        check(inside.SearingEffects.Count==1 && boundary.SearingEffects.Count==1 && outside.SearingEffects.Count==0,"5 m area includes its exact boundary and excludes units beyond it");
        check(ally.SearingEffects.Count==0 && neutral.SearingEffects.Count==0,"Light does not harm allies or neutrals");
        inside.X+=20000; long before=inside.HealthMilli; s.Health=0; Step(w,110);
        check(inside.HealthMilli==9890000 && boundary.HealthMilli==9890000 && outside.HealthMilli==10000000,"Light deals exactly 110 health damage in five seconds, even after leaving its area or the caster dying");
        check(inside.SearingEffects.Count==0 && before>inside.HealthMilli,"searing debuff expires after its full duration");
        var overlap=World(); var c=overlap.Spawn(0,Cell(10),Bearer); var d=overlap.Spawn(1,Cell(22),Dummy);
        Ground(overlap,c,Light,22500); Step(overlap,32); int firstUntil=d.SearingEffects.Single().Until; c.AbilityReadyAt[2]=0;
        Ground(overlap,c,Light,22500); Step(overlap,5);
        check(d.SearingEffects.Count==1 && d.SearingEffects.Single().Until>firstUntil,"repeated Light refreshes a single searing child spell");
        var lethal=World(); var killer=lethal.Spawn(0,Cell(10),Bearer); var victim=lethal.Spawn(1,Cell(22),Dummy); victim.Health=10;
        Ground(lethal,killer,Light,22500); Step(lethal,20);
        check(!victim.Present && killer.KillCount==1 && lethal.Records[0].UnitKills.Values.Sum()==1,"lethal searing damage records one kill and clears a dead target");
    }

    private static void Dispelling(Action<bool,string> check)
    {
        var w=World(); var s=w.Spawn(0,Cell(10),Bearer); var ally=w.Spawn(0,Cell(22),Dummy); var enemy=w.Spawn(1,Cell(24),Dummy);
        var outside=w.Spawn(1,Cell(30),Dummy);
        var broken=new AbilityDefinition(26,0,"Break","break",true,AbilityKind.ShieldBreak,ArmorReductionMilli:1000,DurationTicks:400);
        foreach (var u in new[]{ally,enemy,outside})
        {
            u.ProtectionMilli=150000; u.ProtectionUntil=1000; u.ProtectionAbility=Protection;
            u.MissileWardUntil=1000; u.MissileWardPpm=10000;
            u.ShieldBreaks.Add(new(enemy.Id,1000,1000,broken)); u.SearingEffects.Add(new(s.Id,0,Light,1000));
        }
        Ground(w,s,Dispel,22500); Step(w,35);
        check(ally.ShieldBreaks.Count==0 && ally.SearingEffects.Count==0 && ally.ProtectionMilli>0 && ally.MissileWardUntil>0,"Negate Magic clears hostile spells from allies and preserves their friendly buffs");
        check(enemy.ProtectionMilli==0 && enemy.MissileWardUntil<0 && enemy.ShieldBreaks.Count==1 && enemy.SearingEffects.Count==1,"Negate Magic strips enemy shields and wards while preserving hostile effects on enemies");
        check(outside.ProtectionMilli>0 && outside.MissileWardUntil>0,"Negate Magic respects its 7 m radius");
        var camouflage=new AbilityDefinition(14,0,"Camouflage","camo",false,AbilityKind.Camouflage,RechargeTicks:60,AlphaPpm:500000);
        var hidden=w.Spawn(1,Cell(23,18),Dummy with { Abilities=[camouflage] }); hidden.Camouflaged=true;
        var cloaked=w.Spawn(1,Cell(24,18),Archer); cloaked.AbilityResearch.Add("cloak");
        s.AbilityReadyAt[1]=0; Ground(w,s,Dispel,23500,18500); Step(w,15);
        check(!hidden.Camouflaged && hidden.AbilityReadyAt[0]>w.Tick,"area dispel can remove enemy Camouflage and start its normal recharge");
        check(cloaked.ElvenCloaked,"dispelling spells does not remove researched Elven Cloaks");
    }

    private static void Cloaks(Action<bool,string> check)
    {
        var w=World(false); var a=w.Spawn(0,Cell(10),Archer); var e=w.Spawn(1,Cell(60),Dummy);
        check(!a.ElvenCloaked && w.CanSeeUnit(1,a),"Elven Cloaks stays inactive before research");
        a.AbilityResearch.Add("cloak"); w.Researched[0]=["cloak"];
        var recruit=w.Spawn(0,Cell(12),Archer);
        check(a.ElvenCloaked && recruit.ElvenCloaked && !w.CanSeeUnit(1,a),"cloak research covers existing and future Archers");
        w.Enqueue(0,[a.Id],OrderKind.Move,Cell(20)); Step(w,40);
        check(a.ElvenCloaked && !w.CanSeeUnit(1,a),"the researched cloak stays active while moving");
        var detector=w.Spawn(1,Cell(22),Dummy with { DetectionMask=1 });
        check(w.CanSeeUnit(1,a) && a.ElvenCloaked,"Detection reveals cloaked Archers without removing the passive");
        detector.Health=0;
        check(!w.CanSeeUnit(1,a) && w.CanSeeUnit(-1,a),"dead detectors no longer reveal cloaks; observers still see them");
        check(w.StatusesOf(a).Any(s=>s.Kind==StatusKind.ElvenCloak),"researched cloak has a status icon");
    }

    private static void Orders(Action<bool,string> check)
    {
        var w=World(false); var s=w.Spawn(0,Cell(10),Bearer); Ground(w,s,Light,22500); Step(w,10);
        check(s.CastSerial==0 && s.AbilityReadyAt[2]==0,"locked ground spells cannot be cast");
        s.AbilityResearch.UnionWith(["light","dispel"]);
        w.EnqueueGroundAbility(1,s.Id,2,Cell(22),22500,15500); w.Step();
        check(s.CastSerial==0,"an opponent cannot order a Lightbearer to cast");
        Ground(w,s,Light,22500); w.Step();
        w.Enqueue(0,[s.Id],OrderKind.Stop); Step(w,10);
        check(!s.Casting && s.AbilityReadyAt[2]==0,"Stop cancels a ground wind-up without spending cooldown");
        Ground(w,s,Light,22500); Step(w,32); check(s.CastSerial==2 && s.AbilityReadyAt[2]>w.Tick,"a stopped ground cast can be retried and spends recharge when it applies");
        Ground(w,s,Light,22500); w.Step(); check(s.CastSerial==2,"ground cast cannot bypass recharge");
        s.AbilityReadyAt[2]=0; Ground(w,s,Light,50500); Step(w,400);
        check(s.CastSerial==3 && s.Order!=OrderKind.UseAbility,"out of range Lightbearer approaches the clicked area then casts");
        check(!w.ValidGroundAbilityTarget(s,Light,-1) && !w.ValidGroundAbilityTarget(s,Light,w.Navigation.NodeCount),"off-map targets are rejected");
    }

    public static void Cached(Action<bool,string> check,string cache)
    {
        var manifest=CacheManifest.Load(cache); manifest.Verify();
        string path=manifest.Resolve(manifest.Root.GetProperty("combat_rules").GetString()!);
        var rules=CombatRules.Parse(File.ReadAllText(path));
        var a=rules.Units["UNIT_G_ELVEN_ARCHER"]; var b=rules.Units["UNIT_G_ELVEN_LIGHTBRINGER"];
        check(a.Abilities is { Length:2 } && a.Abilities.Any(x=>x.Kind==AbilityKind.ElvenCloak),"cached Archers carry their passive cloak");
        check(a.Abilities!.Single(x=>x.Kind==AbilityKind.EagleFletching) is { Slot:3,Passive:true,RequiredTechnique:"TECHNIQUE_EAGLE_FLETCHING",BonusScale:1200000 },
            "cached Archers show source Eagle Fletching in their R passive slot with its research gate");
        check(b.Abilities is { Length:3 } && b.Abilities.All(x=>x.Kind!=AbilityKind.Unsimulated),"all Lightbearer source slots are implemented");
        check(b.Abilities![0] is { ShieldMilli:150000,DurationTicks:1000,RechargeTicks:700,EffectTick:13 },"cached Protection matches source shield, duration, cooldown and toolscript");
        check(b.Abilities[1] is { AreaRadiusMm:7000,RangeMm:15000,RechargeTicks:200 }
            && b.Abilities[2] is { AreaRadiusMm:5000,RangeMm:16000,RechargeTicks:900,DpsMilli:22000,DurationTicks:100 },"cached area spells match source ranges, radii, damage and recharge");
        using var json=JsonDocument.Parse(File.ReadAllText(path)); var root=json.RootElement;
        var bolt=root.GetProperty("Projectiles").GetProperty(b.Name);
        check(bolt.GetProperty("Visual").ValueKind==JsonValueKind.Null && bolt.GetProperty("SpriteEffects").GetArrayLength()>0,"meshless Lightbearer bolt uses the source sprite effect");
        check(bolt.GetProperty("LaunchSounds").EnumerateArray().All(x=>x.GetProperty("EventId").GetInt32()==278)
            && bolt.GetProperty("ImpactSounds").EnumerateObject().Any(),"Lightbearer projectile has its own launch and material impact sounds");
        check(root.GetProperty("ElvenVisuals").EnumerateObject().Count()==7,"all source Elven ability effects are packaged");
        check(b.Abilities[0].Cursor=="cursor_g_protection" && b.Abilities[1].Cursor=="cursor_g_dispel" && b.Abilities[2].Cursor=="cursor_g_light_of_lothlorien",
            "each Lightbearer ability retains its own original cursor");
        using var ui=JsonDocument.Parse(File.ReadAllText(manifest.Resolve(manifest.Root.GetProperty("interface").GetString()!)));
        check(b.Abilities.All(ability=>ui.RootElement.GetProperty("assets").TryGetProperty(ability.Cursor,out var cursor)
                && cursor.GetProperty("cursor_hotspot")[0].GetInt32()==1 && cursor.GetProperty("cursor_hotspot")[1].GetInt32()==3),
            "all Lightbearer cursor images and original arrow-tip hotspots are in the cache");
    }
}
