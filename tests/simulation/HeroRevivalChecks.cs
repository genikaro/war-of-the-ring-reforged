using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

static class HeroRevivalChecks
{
    private const int W=50,Producer=9000;
    private static int Cell(int x,int z=10)=>z*W+x;
    private static readonly HeroExperienceDefinition Progression=new(1000000,400000,[
        new(1,0,0,0,0),new(2,100000,25,0,50000),new(3,250000,50,1000,100000),new(4,500000,75,2000,150000)]);
    private static readonly UnitDefinition Hero=UnitDefinition.Probe with {Id=16,Name="hero",DisplayName="Hero",Hero=true,
        Passive=true,MaxHealth=500,ArmorMilli=3000,DpsMilli=100000,Experience=Progression};
    private static SimWorld Open(CombatRules? rules=null)=>new(new(W,30,1000,Enumerable.Repeat(true,W*30).ToArray(),new int[(W+1)*31]),rules);
    private static void Advance(SimWorld world,int ticks){for(int i=0;i<ticks;i++)world.Step();}
    private static (SimWorld World,SimUnit Fallen) Fixture()
    {
        var rules=new CombatRules();rules.Units.Add(Hero.Name,Hero);
        var w=Open(rules);w.Economies.Add(0,new(500,500){Fate=20});w.InitialPopulationCapacity=10;
        w.ProductionRules.Add(Hero.Name,new(Hero.Name,"Hero",35,20,1,2,Unique:true,FateCost:3));
        w.Producers.Add(Producer,new(Producer,0,30500,20500,Cell(30,18),[Hero.Name]));
        var fallen=w.Spawn(0,Cell(10),Hero);fallen.HeroLevel=3;fallen.ExperienceMicro=333123456;
        return(w,fallen);
    }
    private static void Train(SimWorld w)=>w.Enqueue(0,[],OrderKind.Train,0,Producer);
    private static void DefeatInCombat(SimWorld w,SimUnit hero)
    {
        var killer=w.Spawn(1,Cell(12),UnitDefinition.Probe with {MaxHealth=10000,DpsMilli=1000000});
        killer.Facing=UnitFacing.FromDirection(-1,0);w.Enqueue(1,[killer.Id],OrderKind.Attack,target:hero.Id);
        for(int i=0;i<100 && hero.Health>0;i++)w.Step();
        if(hero.Health>0)throw new Exception("hero revival fixture did not defeat its hero");
    }

    public static void Run(Action<bool,string> check)
    {
        var f=Fixture();var w=f.World;var old=f.Fallen;
        check(!w.CanTrain(0,Producer,0,out _),"living heroes still block duplicate recruitment");
        old.AbilityReadyAt[0]=1000;old.ProtectionMilli=150000;old.ProtectionUntil=1000;old.KillCount=7;
        DefeatInCombat(w,old);
        check(w.CanTrain(0,Producer,0,out _),"saved progression does not reserve a defeated hero's recruitment slot");
        w.Producers[Producer].RallyCell=Cell(40,20);Train(w);Advance(w,2);var revived=w.Units[^1];
        check(revived.Id!=old.Id && revived.HeroLevel==3 && revived.ExperienceMicro==333123456,"Citadel recruitment restores the defeated hero's level and exact partial XP");
        check(revived.MaxHealth==550 && revived.HealthMilli==550000 && revived.ArmorMilli==4000 && revived.MeleeDamageRate==110000,"revived heroes return at full restored maximum health with their level's armor and attack");
        check(revived.AbilityReadyAt.All(t=>t==0) && revived.ProtectionMilli==0 && revived.KillCount==0,"revival restores progression without copying temporary effects or counters");
        check(w.HeroLevelEvents.Count==0,"restoring a hero does not emit a false level-up event");
        check(revived.Order==OrderKind.Move && revived.OrderDestination>=0,"revived heroes still follow the producer's rally order");
        check(w.Economies[0].FoodMilli==465000 && w.Economies[0].OreMilli==480000 && w.Economies[0].Fate==17,"revival pays the normal recruitment cost exactly once");
        check(HeroExperienceText.From(revived).Tooltip.Contains("83.12 / 250 XP to level 4"),"restored XP readout preserves progress through the current level");
        check(!w.CanTrain(0,Producer,0,out _),"the revived hero once again blocks duplicate recruitment");

        revived.ExperienceMicro=401999999;revived.Health=0;Advance(w,3);
        var again=w.Spawn(0,Cell(35,23),Hero);
        check(again.HeroLevel==3 && again.ExperienceMicro==401999999,"a second revival keeps experience gained after the first revival");
        Advance(w,4);again.HeroLevel=4;again.ExperienceMicro=500000000;again.Health=0;Advance(w,4);
        var capped=w.Spawn(0,Cell(39,23),Hero);
        check(capped.HeroLevel==4 && capped.ExperienceMicro==500000000 && capped.ExperienceProgressPpm==1000000,"maximum-level heroes retain their cap across repeated deaths");
        check(capped.HealthMilli==575000 && capped.ArmorMilli==5000,"older corpses cannot overwrite the newest saved level bonuses");
        var enemy=w.Spawn(1,Cell(45,23),Hero);
        var other=w.Spawn(0,Cell(43,23),Hero with {Name="other",Id=17});
        check(enemy.HeroLevel==1 && enemy.ExperienceMicro==0,"another player's hero never inherits saved progression");
        check(other.HeroLevel==1 && other.ExperienceMicro==0,"a different hero type never inherits saved progression");
        var ordinary=w.Spawn(0,Cell(41,25),Hero with {Hero=false,Experience=null});
        check(ordinary.HeroLevel==1 && ordinary.MaxHealth==500,"ordinary units cannot inherit hero progression through their name or model");
        var fresh=Open().Spawn(0,Cell(10),Hero);
        check(fresh.HeroLevel==1 && fresh.ExperienceMicro==0,"a new match starts without the previous match's hero records");

        var instant=Fixture();instant.Fallen.Health=0;instant.World.InstantBuildTeams.Add(0);Train(instant.World);instant.World.Step();
        check(instant.World.Units[^1].HeroLevel==3 && instant.World.Units[^1].ExperienceMicro==333123456,"instant revival before the corpse cleanup pass still restores progression");
        var cancelled=Fixture();cancelled.Fallen.Health=0;Train(cancelled.World);cancelled.World.Step();
        cancelled.World.Enqueue(0,[],OrderKind.CancelTraining,0,Producer);cancelled.World.Step();
        check(cancelled.World.Units.Count==1 && cancelled.World.Economies[0].Fate==20,"cancelling a queued revival refunds its cost without spawning a hero");
        Train(cancelled.World);Advance(cancelled.World,2);
        check(cancelled.World.Units[^1].ExperienceMicro==333123456,"cancelling a revival does not erase the saved experience record");

        var places=Fixture();places.Fallen.Health=0;places.World.Step();
        places.World.PlacesOfPower.Add(new(1,"health","Health",45000,25000,[new(1,"health","Health",1000,false,true,false,[],SimWorld.MaxHealthModifier,1500000,0)]){Owner=0});
        Train(places.World);Advance(places.World,2);var boosted=places.World.Units[^1];
        check(boosted.MaxHealth==825 && boosted.HealthMilli==825000,"revival combines restored level HP with the team's current health-place bonus exactly once");

        // Reproduce the recorded combat/recruitment prefix without the later fixture-only mutations.
        var original=Fixture();DefeatInCombat(original.World,original.Fallen);Train(original.World);Advance(original.World,2);
        var recorded=Fixture();var k=recorded.World.Spawn(1,Cell(12),UnitDefinition.Probe with {MaxHealth=10000,DpsMilli=1000000});k.Facing=UnitFacing.FromDirection(-1,0);
        recorded.World.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(original.World.Commands))!);Advance(recorded.World,original.World.Tick);
        check(recorded.World.StateDigest()==original.World.StateDigest(),"hero death, saved progression and recruitment replay deterministically");
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        foreach(var definition in rules.Units.Values.Where(u=>u.Hero && u.Experience!=null))
        {
            var w=Open(rules);var old=w.Spawn(0,Cell(10),definition);old.HeroLevel=6;old.ExperienceMicro=3123456789;old.Health=0;w.Step();
            var revived=w.Spawn(0,Cell(30,20),definition);
            check(revived.HeroLevel==6 && revived.ExperienceMicro==3123456789,definition.DisplayName+" keeps original level-6 partial experience on revival");
            check(revived.MaxHealth==old.MaxHealth && revived.ArmorMilli==old.ArmorMilli && revived.MeleeDamageRate==old.MeleeDamageRate
                && revived.HealthMilli==revived.MaxHealth*1000L,definition.DisplayName+" restores its original level bonuses at full health");
        }
    }
}
