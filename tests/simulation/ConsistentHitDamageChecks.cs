using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

static class ConsistentHitDamageChecks
{
    private static readonly UnitDefinition Aragorn = UnitDefinition.Probe with {Id=16,Name="aragorn",DpsMilli=20000,
        // The source timelines and equal choice weights: two single strikes and one two-strike animation.
        Attacks=[new("first",10000,34,[new(21,1000000)]),new("second",10000,31,[new(19,1000000)]),new("double",10000,37,[new(13,1000000),new(27,1000000)])]};
    private static SimWorld Open() => new(new(20,20,1000,Enumerable.Repeat(true,400).ToArray(),new int[441]));
    private static (SimWorld World,SimUnit Hero,List<CombatEvent> Hits) Duel(UnitDefinition definition,int armor=0,int target=24)
    {
        var w=Open();var hero=w.Spawn(0,205,definition);
        w.Spawn(1,207,UnitDefinition.Probe with {Id=target,Passive=true,MaxHealth=10000,ArmorMilli=armor});
        w.Enqueue(0,[hero.Id],OrderKind.Attack,target:1);var hits=new List<CombatEvent>();
        for(int tick=0;tick<105;tick++){w.Step();hits.AddRange(w.CombatEvents.Where(e=>e.Unit==hero.Id && e.Kind==CombatEventKind.Hit));}
        return(w,hero,hits);
    }
    public static void Run(Action<bool,string> check)
    {
        var f=Duel(Aragorn);var stats=UnitHudStats.From(f.Hero,f.World.Rules);
        check(stats.Attack=="26" && stats.AttackBonus=="","unupgraded Aragorn retains the displayed 26 damage per strike");
        check(f.Hits.Select(e=>e.Attack).Distinct().Count()==3 && f.Hits.All(e=>e.DamageMilli==26000),
            "all three Aragorn animations deal exactly the HUD's 26 damage at zero armor");
        check(f.Hits.All(e=>e.Tick-e.StartTick==Aragorn.Attacks[e.Attack].Hits[e.Hit].Tick),"consistent damage preserves every source hit-event tick");
        check(f.Hits.Where(e=>e.Attack==2).Select(e=>e.Hit).Distinct().Order().SequenceEqual([0,1]),"both strikes in the double animation deal a full hit");
        check(stats.AttackTip.Contains("Damage per second: 20.392"),"the damage tooltip reports DPS from actual damage and cadence");
        var armored=Duel(Aragorn,armor:2000);
        check(armored.Hits.All(e=>e.DamageMilli==23400),"armor adjusts the consistent hit once, without animation-dependent damage");
        var fury=Aragorn with {Abilities=[new(25,1,"Fury","",true,AbilityKind.Fury,DpsMilli:30000,BonusScale:1500000,BonusTargets:[52,33])]};
        var upgraded=Duel(fury);var furyStats=UnitHudStats.From(upgraded.Hero,upgraded.World.Rules);
        check(furyStats.Attack=="39" && upgraded.Hits.All(e=>e.DamageMilli==39000),"Fury's equipped damage matches the HUD on every animation");
        var wraith=Duel(fury,target:52);
        check(wraith.Hits.All(e=>e.DamageMilli==58500),"Fury retains its additional target-specific multiplier");
        var unequal=Aragorn with {Attacks=[new("unequal",1,40,[new(10,900000),new(30,100000)])]};
        check(new[]{0,1}.All(hit=>unequal.HitDamage(unequal.Attacks[0],hit,20000)==20000),"animation event scales do not silently change the displayed strike damage");

        var ranged=Aragorn with {ProjectileSpeedMmPerSecond=25000,ReachMm=13000,MinimumRangeMm=1000,MeleeDpsMilli=30000,
            Attacks=[new("bow",1,40,[new(10,1000000)]),new("bow-fast",1,20,[new(10,1000000)]),new("contact",1,12,[new(5,1000000)],Melee:true)]};
        check(ranged.HitDamage(ranged.Attacks[0],0,20000)==30000 && ranged.HitDamage(ranged.Attacks[1],0,20000)==30000,
            "ranged attack variants share the displayed missile damage");
        check(ranged.HitDamage(ranged.Attacks[2],0,30000)==18000,"ranged units retain their separate contact weapon and cadence");
        check(ranged.HitDurationPpm()==1500000,"projectile chains use the same damage conversion as the initial projectile");
        var replay=Open();replay.Spawn(0,205,Aragorn);replay.Spawn(1,207,UnitDefinition.Probe with {Id=24,Passive=true,MaxHealth=10000});
        replay.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(f.World.Commands))!);
        for(int tick=0;tick<105;tick++)replay.Step();
        check(f.World.StateDigest()==replay.StateDigest(),"consistent-hit combat records and replays deterministically");
    }
    public static void Cached(Action<bool,string> check,string folder)
    {
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        var aragorn=rules.Units["UNIT_G_HERO_ARAGORN"];
        check(aragorn.BaseHitDamageMilli()==26000,"the actual imported Aragorn has a 26-damage base strike");
        foreach(var attack in aragorn.Attacks)
            for(int hit=0;hit<attack.Hits.Length;hit++)
                check(aragorn.HitDamage(attack,hit,aragorn.DpsMilli)==26000,"cached "+attack.Clip+" matches the HUD on strike "+hit);
    }
}
