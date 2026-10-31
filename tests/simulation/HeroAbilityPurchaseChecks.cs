using Reforged.Simulation;
using System.Text.Json;

static class HeroAbilityPurchaseChecks
{
    private static readonly AbilityDefinition Heal = new(41,0,"Heal","",false,AbilityKind.Heal,
        HealMilli:100000,RechargeTicks:20,RangeMm:10000,CastClip:"heal",CastTicks:3,EffectTick:1,Purchasable:true,PurchaseFateCost:2);
    private static readonly AbilityDefinition Fury = new(25,1,"Fury","",true,AbilityKind.Fury,
        DpsMilli:30000,BonusTargets:[52,33],BonusScale:1500000,Purchasable:true,PurchaseFateCost:2);
    private static readonly AbilityDefinition Immunity = new(44,2,"Immunity","",true,AbilityKind.Immunity,
        Immunities:(StatusProtection)15,Purchasable:true,PurchaseFateCost:1);
    private static readonly AbilityDefinition Aura = new(12,3,"Aura","",true,AbilityKind.Aura,RangeMm:5000,RechargeScalePpm:1500000);
    private static readonly UnitDefinition Hero = UnitDefinition.Probe with { Id=16,Name="hero",Hero=true,Passive=true,
        DpsMilli=20000,MaxHealth=500,Abilities=[Heal,Fury,Immunity,Aura] };
    private static SimWorld Open() => new(new(40,20,1000,Enumerable.Repeat(true,800).ToArray(),new int[861]));
    private static (SimWorld World,SimUnit Hero) Fixture(int fate=4)
    {
        var w=Open();w.Economies.Add(0,new(0,0){Fate=fate,FateProgressMicro=12345});
        return(w,w.Spawn(0,403,Hero));
    }
    private static void Step(SimWorld w,int ticks=1) { for(int i=0;i<ticks;i++)w.Step(); }

    public static void Run(Action<bool,string> check)
    {
        var f=Fixture();var w=f.World;var hero=f.Hero;
        check(!hero.AbilityEnabled(Heal) && !hero.AbilityEnabled(Fury) && !hero.AbilityEnabled(Immunity)
            && hero.AbilityEnabled(Aura),"paid hero gear starts locked while innate abilities are free");
        check(hero.MeleeDamageRate==20000 && hero.Immunities==StatusProtection.None,"unpurchased passives grant no weapon or immunity bonuses");
        var ally=w.Spawn(0,407,UnitDefinition.Probe);ally.Health=10;
        w.EnqueueAbility(0,[hero.Id],0,ally.Id);Step(w,4);
        check(!hero.Casting && ally.Health==10 && w.Economies[0].Fate==4,"direct ability commands cannot bypass a paid unlock");
        check(!w.CanPurchaseAbility(1,hero.Id,1,out _) && !w.CanPurchaseAbility(0,-1,1,out _)
            && !w.CanPurchaseAbility(0,hero.Id,9,out _) && !w.CanPurchaseAbility(0,hero.Id,3,out _),
            "purchases reject foreign heroes, invalid ids or slots, and innate gear");
        w.EnqueueAbilityPurchase(1,hero.Id,1);w.EnqueueAbilityPurchase(0,hero.Id,3);Step(w);
        check(hero.PurchasedAbilities.Count==0 && w.Economies[0].Fate==4,"invalid purchases never spend Fate or unlock gear");
        w.EnqueueAbilityPurchase(0,hero.Id,1);w.EnqueueAbilityPurchase(0,hero.Id,1);w.EnqueueAbilityPurchase(0,hero.Id,2);Step(w);
        check(w.Economies[0].Fate==1 && hero.PurchasedAbilities.SetEquals([25,44]),"duplicate same-tick purchases charge each ability once");
        check(hero.MeleeDamageRate==30000 && hero.Immunities==(StatusProtection)15 && !hero.Casting,
            "buying a passive applies it immediately without casting");
        check(w.Economies[0].FateProgressMicro==12345,"ability purchases preserve progress toward the next Fate point");
        check(!w.CanPurchaseAbility(0,hero.Id,0,out string reason) && reason=="Requires 2 Fate","an unaffordable ability reports its Fate price");
        w.EnqueueAbilityPurchase(0,hero.Id,0);Step(w);
        check(!hero.AbilityPurchased(Heal) && w.Economies[0].Fate==1,"insufficient Fate never partially pays or unlocks an ability");
        w.GrantResources(0,0,fate:1);w.Enqueue(0,[hero.Id],OrderKind.Move,430);w.EnqueueAbilityPurchase(0,hero.Id,0);Step(w);
        check(hero.AbilityPurchased(Heal) && w.Economies[0].Fate==0 && hero.Order==OrderKind.Move && !hero.Casting,
            "purchasing an active ability preserves movement and does not cast it");
        w.EnqueueAbility(0,[hero.Id],0,ally.Id);Step(w,4);
        check(ally.Health==100 && hero.AbilityReadyAt[0]>w.Tick,"a purchased active ability casts normally on the next command");
        int ready=hero.AbilityReadyAt[0];w.EnqueueAbilityPurchase(0,hero.Id,0);Step(w);
        check(hero.AbilityReadyAt[0]==ready && w.Economies[0].Fate==0,"repurchasing cannot reset a cooldown or charge again");

        var busy=Fixture(5);var patient=busy.World.Spawn(0,407,UnitDefinition.Probe);patient.Health=10;
        busy.World.EnqueueAbilityPurchase(0,busy.Hero.Id,0);Step(busy.World);
        busy.World.EnqueueAbility(0,[busy.Hero.Id],0,patient.Id);Step(busy.World);
        int castStart=busy.Hero.CastStartTick;busy.World.EnqueueAbilityPurchase(0,busy.Hero.Id,1);Step(busy.World);
        check(busy.Hero.Casting && busy.Hero.CastStartTick==castStart && busy.Hero.AbilityPurchased(Fury) && patient.Health==100,
            "buying another ability preserves an active cast and its effect");
        busy.Hero.Negated=new(busy.Hero.Id,new(40,0,"Negate","",false,AbilityKind.NegateAbility),1000);
        check(busy.Hero.AbilityPurchased(Fury) && !busy.Hero.AbilityEnabled(Fury),"Negate Ability suppresses purchased gear without erasing its unlock");
        busy.Hero.Negated=null;
        check(busy.Hero.MeleeDamageRate==30000,"purchased passive bonuses return after Negate Ability expires");

        var race=Fixture(2);var second=race.World.Spawn(0,410,Hero);
        race.World.EnqueueAbilityPurchase(0,race.Hero.Id,1);race.World.EnqueueAbilityPurchase(0,second.Id,1);Step(race.World);
        check(race.Hero.AbilityPurchased(Fury) && !second.AbilityPurchased(Fury) && race.World.Economies[0].Fate==0,
            "competing hero purchases spend a shared bank in recorded command order");
        var ordinary=race.World.Spawn(0,415,Hero with {Hero=false});
        check(!race.World.CanPurchaseAbility(0,ordinary.Id,1,out _),"ordinary units cannot purchase hero gear");
        race.Hero.Health=0;
        check(!race.World.CanPurchaseAbility(0,race.Hero.Id,2,out _),"defeated heroes cannot purchase gear");

        var revival=Fixture(5);revival.World.EnqueueAbilityPurchase(0,revival.Hero.Id,1);Step(revival.World);revival.Hero.Health=0;
        var revived=revival.World.Spawn(0,410,Hero);
        check(revived.AbilityPurchased(Fury) && !revived.AbilityPurchased(Immunity) && revival.World.Economies[0].Fate==3,
            "revival before corpse cleanup restores purchased gear without charging again");
        revival.World.EnqueueAbilityPurchase(0,revived.Id,2);Step(revival.World);revived.Health=0;Step(revival.World);
        var again=revival.World.Spawn(0,415,Hero);Step(revival.World,3);
        check(again.PurchasedAbilities.SetEquals([25,44]),"older corpses cannot overwrite gear bought during a later life");
        var opponent=revival.World.Spawn(1,420,Hero);var other=revival.World.Spawn(0,425,Hero with {Name="other",Id=17});
        check(opponent.PurchasedAbilities.Count==0 && other.PurchasedAbilities.Count==0,"purchases stay with their player and hero type");
        check(Fixture().Hero.PurchasedAbilities.Count==0,"a new match starts with paid abilities locked");

        var original=Fixture();original.World.EnqueueAbilityPurchase(0,0,1);original.World.EnqueueAbilityPurchase(0,0,2);Step(original.World,4);
        var replay=Fixture();replay.World.Commands.AddRange(JsonSerializer.Deserialize<SimCommand[]>(JsonSerializer.Serialize(original.World.Commands))!);Step(replay.World,4);
        check(original.World.StateDigest()==replay.World.StateDigest(),"ability purchases serialize and replay deterministically");
        string digest=replay.World.StateDigest();replay.Hero.PurchasedAbilities.Clear();
        check(digest!=replay.World.StateDigest(),"purchased ability state participates in the replay digest");
        foreach(var bad in new[]{Fury with {PurchaseFateCost=-1},Fury with {PurchaseFateCost=21},Fury with {Purchasable=false}})
        {try{bad.Validate();check(false,"invalid purchase data must fail");}catch(ArgumentException){check(true,"invalid purchase data rejected");}}
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        var prices=new Dictionary<string,int[]> {
            ["ARAGORN"]=[0,2,1],["FRODO"]=[1,1,0],["GIMLI"]=[2,3,0],["LEGOLAS"]=[2,2,0],["GANDALF"]=[2,1,0,3] };
        foreach(var (name,costs) in prices)
        {
            var definition=rules.Units["UNIT_G_HERO_"+name];var w=Open();w.Economies.Add(0,new(0,0){Fate=20});
            var hero=w.Spawn(0,403,definition);
            foreach(var ability in definition.Abilities!)
            {
                check(ability.PurchaseFateCost==costs[ability.Slot] && ability.Purchasable==(costs[ability.Slot]>0),
                    name+" imports the source purchase flag and price for "+ability.Name);
                check(hero.AbilityPurchased(ability)==!ability.Purchasable,name+" starts with the correct unlock state for "+ability.Name);
                if(ability.Purchasable)w.EnqueueAbilityPurchase(0,hero.Id,ability.Slot);
            }
            Step(w);
            check(w.Economies[0].Fate==20-costs.Sum() && definition.Abilities.All(hero.AbilityEnabled),name+" buys its gear at the original Fate prices");
        }
    }
}
