using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

static class HeroExperienceChecks
{
    private static readonly HeroExperienceDefinition Progression=new(1000000,400000,[
        new(1,0,0,0,0),new(2,100000,25,0,50000),new(3,250000,50,1000,100000),new(4,500000,75,2000,150000)]);
    private static readonly UnitDefinition Hero=UnitDefinition.Probe with {Id=16,Name="hero",DisplayName="Hero",Hero=true,
        MaxHealth=500,ArmorMilli=3000,DpsMilli=100000,Experience=Progression};
    private static SimWorld Open()=>new(new(60,30,1000,Enumerable.Repeat(true,1800).ToArray(),new int[61*31]));
    private static int Cell(int x,int z=10)=>z*60+x;
    private static void Advance(SimWorld w,int ticks){for(int t=0;t<ticks;t++)w.Step();}
    private static (SimWorld W,SimUnit H,SimUnit T) Fixture(int dps=100000,int health=10000,int team=1,UnitDefinition? definition=null)
    {
        var w=Open();var h=w.Spawn(0,Cell(10),(definition??Hero) with {DpsMilli=dps});
        var target=w.Spawn(team,Cell(12),UnitDefinition.Probe with {Passive=true,MaxHealth=health});
        h.Facing=UnitFacing.FromDirection(1,0);return(w,h,target);
    }
    private static void Hit(SimWorld w,SimUnit h,SimUnit target,bool force=false)
    {
        long before=target.HealthMilli;w.Enqueue(h.Team,[h.Id],force?OrderKind.ForceAttack:OrderKind.Attack,target:target.Id);
        for(int t=0;t<150;t++){w.Step();if(target.HealthMilli<before)return;}
        throw new Exception("hero experience fixture did not land its hit");
    }

    public static void Run(Action<bool,string> check)
    {
        Progression.Validate();Hero.Validate();
        var f=Fixture();check(f.H.HeroLevel==1 && f.H.ExperienceMicro==0 && f.H.MaxHealth==500 && f.H.ArmorMilli==3000,"heroes begin at level 1 with their base stats");
        f.H.Health=250;Hit(f.W,f.H,f.T);
        check(f.H.HeroLevel==2 && f.H.ExperienceMicro==100000000,"actual enemy damage awards experience and crosses the next cumulative boundary");
        check(f.H.MaxHealth==525 && f.H.HealthMilli==262500,"level-up adds source HP while preserving the wounded hero's health fraction");
        check(f.H.MeleeDamageRate==105000 && f.H.ArmorMilli==3000,"level 2 uses its replacement 5% damage and zero armor bonuses");
        check(f.H.Order==OrderKind.Attack && f.H.StrikeActive && f.H.Cooldown>0,"level-up preserves the current attack, animation and recovery");
        check(f.W.HeroLevelEvents is [{PreviousLevel:1,Level:2}],"level-up emits a presentation event exactly once");
        check(f.H.ExperienceProgressPpm==0 && HeroExperienceText.From(f.H).Tooltip.Contains("0 / 150 XP to level 3"),"experience bar starts the next level at zero with its incremental XP cost");
        var stats=UnitHudStats.From(f.H,f.W.Rules);
        check(stats.AttackTip.Contains("105") && stats.Health=="263/525","level bonuses reach the attack and health readouts");

        var below=Fixture(dps:99999);below.H.Definition=below.H.Definition with {DamagePerHitMilli=99999};Hit(below.W,below.H,below.T);
        check(below.H.HeroLevel==1 && below.H.ExperienceMicro==99999000 && below.H.ExperienceProgressPpm==999990,"fractional XP is retained without premature level-up");
        var cap=Fixture(dps:1000000);Hit(cap.W,cap.H,cap.T);
        check(cap.H.HeroLevel==4 && cap.H.ExperienceMicro==500000000 && cap.H.ExperienceProgressPpm==1000000,"a large hit resolves every crossed level and clamps XP at the level cap");
        check(cap.H.MaxHealth==575 && cap.H.ArmorMilli==5000 && cap.H.MeleeDamageRate==1150000,"high-level bonuses replace lower-level bonuses rather than stacking them");
        check(HeroExperienceText.From(cap.H).Tooltip.Contains("Maximum level"),"capped heroes show a full bar and maximum level");
        check(UnitHudStats.From(cap.H,cap.W.Rules).ArmorTip.Contains("hero level"),"armor tooltip names the hero level contribution");

        var overkill=Fixture(dps:1000000,health:17);Hit(overkill.W,overkill.H,overkill.T);
        check(overkill.H.ExperienceMicro==17000000 && overkill.H.KillCount==1,"overkill awards XP for remaining health only, with no duplicate kill XP");
        var weapon=Fixture(definition:Hero with {MeleeWeaponExperiencePpm=900000});Hit(weapon.W,weapon.H,weapon.T);
        check(weapon.H.ExperienceMicro==90000000,"normal attacks use their source weapon experience modifier");
        var shield=Fixture();shield.T.ProtectionMilli=200000;shield.T.ProtectionUntil=100;
        shield.W.Enqueue(0,[shield.H.Id],OrderKind.Attack,target:shield.T.Id);Advance(shield.W,10);
        check(shield.H.ExperienceMicro==0 && shield.T.HealthMilli==10000000,"absorbed damage awards no experience");
        var friendly=Fixture(team:0);Hit(friendly.W,friendly.H,friendly.T,true);
        check(friendly.H.ExperienceMicro==0,"friendly fire cannot farm hero experience");
        var soldier=Fixture(definition:Hero with {Hero=false,Experience=null});Hit(soldier.W,soldier.H,soldier.T);
        check(soldier.H.HeroLevel==1 && soldier.H.ExperienceMicro==0,"ordinary soldiers do not acquire hero levels");

        var places=Fixture();
        places.W.PlacesOfPower.Add(new(1,"xp","Experience Stone",55000,25000,[new(19,"experience","Experience Stone",1000,false,true,false,[],SimWorld.ExperienceModifier,1500000,0)]){Owner=0});
        Hit(places.W,places.H,places.T);
        check(places.H.ExperienceMicro==150000000 && places.H.HeroLevel==2,"controlled EXPERIENCE places multiply the hero's earned XP");
        check(places.W.StatusesOf(places.H).Any(s=>s.Kind==StatusKind.PlaceOfPower),"experience place benefits appear in the unit status bar");

        var build=Open();var builder=build.Spawn(0,Cell(10),Hero with {MeleeWeaponExperiencePpm=900000});builder.Facing=UnitFacing.FromDirection(1,0);
        build.BuildingRules["tower"]=new("tower","Tower",null,0,0,20,1000,0,0,1000000,1000000,[]);
        var tower=new ConstructionSite(90,90,500,"tower",12500,10500){Team=1,Complete=true,Health=1000};build.ConstructionSites[90]=tower;
        build.Enqueue(0,[builder.Id],OrderKind.AttackStructure,target:90);
        for(int t=0;t<150 && tower.Health==1000;t++)build.Step();
        check(tower.Health<1000 && builder.ExperienceMicro==(1000-tower.Health)*360000L,"building XP uses actual damage, the 40% building rate and the weapon modifier");

        var ranged=Fixture(definition:Hero with {ProjectileSpeedMmPerSecond=25000,ReachMm=15000,WeaponExperiencePpm=500000,MeleeWeaponExperiencePpm=900000});
        Hit(ranged.W,ranged.H,ranged.T);check(ranged.H.ExperienceMicro==50000000,"ranged experience is awarded when the projectile hits using its missile weapon rate");
        var dead=Fixture(definition:Hero with {ProjectileSpeedMmPerSecond=1000,ReachMm=15000});
        dead.W.Enqueue(0,[dead.H.Id],OrderKind.Attack,target:dead.T.Id);
        for(int t=0;t<100 && dead.W.Projectiles.Count==0;t++)dead.W.Step();
        check(dead.W.Projectiles.Count>0 && dead.H.ExperienceMicro==0,"projectile release alone earns no XP");
        dead.H.Health=0;Advance(dead.W,60);check(dead.H.ExperienceMicro==0 && dead.H.HeroLevel==1,"a dead hero cannot level up or revive from an arrow still in flight");

        var spell=Open();var wizard=spell.Spawn(0,Cell(10),Hero);var burned=spell.Spawn(1,Cell(20),UnitDefinition.Probe with {Passive=true,MaxHealth=1000});
        wizard.Order=OrderKind.HoldGround;
        var rain=new AbilityDefinition(43,3,"Rain","",false,AbilityKind.RainOfFire,RangeMm:17000,AreaRadiusMm:10000,DurationTicks:20,DpsMilli:18000,ExperiencePpm:100000);
        burned.HeroBurns.Add(new(wizard.Id,rain,20));Advance(spell,20);
        check(wizard.ExperienceMicro==1800000,"spell damage carries its separate experience multiplier to the living source hero");

        var deterministic=Fixture();Hit(deterministic.W,deterministic.H,deterministic.T);
        var replay=Fixture();replay.W.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(deterministic.W.Commands))!);
        Advance(replay.W,deterministic.W.Tick);
        check(replay.W.StateDigest()==deterministic.W.StateDigest(),"combat XP and level bonuses replay deterministically");
        string digest=replay.W.StateDigest();replay.H.ExperienceMicro++;
        check(digest!=replay.W.StateDigest(),"fractional experience participates in the replay digest");
        try{(Progression with {Levels=[new(1,0,0,0,0),new(2,0,0,0,0)]}).Validate();check(false,"invalid experience rules rejected");}catch(ArgumentException){check(true,"invalid experience rules rejected");}
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        foreach(var hero in rules.Units.Values.Where(u=>u.Hero))
        {
            check(hero.Experience is {Levels.Length:10},hero.DisplayName+" imports all ten hero levels");
            check(hero.Experience!.Levels[1].ExperienceMilli==500000 && hero.Experience.Levels[2].ExperienceMilli==1050000
                && hero.Experience.Levels[^1].ExperienceMilli==6300000,hero.DisplayName+" accumulates source per-level thresholds");
            check(hero.Experience.Levels[^1] is {HitPoints:250,DamageBonusPpm:500000},hero.DisplayName+" imports source level 10 health and damage bonuses");
            hero.Validate();
        }
        check(rules.Units["UNIT_G_HERO_FRODO"].Experience!.Levels[^1].ArmorMilli==3000,"Frodo keeps his distinct level 10 armor bonus");
        check(rules.Units["UNIT_G_HERO_ARAGORN"].Experience!.Levels[^1].ArmorMilli==4000,"Aragorn imports his level 10 armor bonus");
    }
}
