using Reforged.Simulation;

static class NatureAbilityChecks
{
    private const int W=90;
    private static int Cell(int x,int z=20)=>z*W+x;
    private static readonly AbilityDefinition Morph=new(32,0,"Bear Form","bear",false,AbilityKind.Morph,
        RechargeTicks:400,RangeMm:10000,CastClip:"transform",CastTicks:6,EffectTick:2,MorphUnit:"bear",RequiredTechnique:"morph");
    private static readonly AbilityDefinition Human=Morph with {Id=31,Name="Human Form",MorphUnit="human",RequiredTechnique=null};
    private static readonly AbilityDefinition Herbs=new(4,1,"Herbs","herbs",false,AbilityKind.HealingHerbs,
        HealMilli:75000,RechargeTicks:120,RangeMm:8000,ResponseRangeMm:15000,CastClip:"heal",CastTicks:5,EffectTick:2);
    private static readonly AbilityDefinition Blow=new(28,1,"Blow","blow",false,AbilityKind.SavageBlow,
        HealMilli:75000,RechargeTicks:300,RangeMm:4000,AreaRadiusMm:4000,CastClip:"blow",CastTicks:6,EffectTick:2,
        ArcDegrees:260,KnockbackSpeedMm:7000,RequiredTechnique:"blow");
    private static readonly AbilityDefinition Tree=new(33,0,"Treeform","tree",false,AbilityKind.Treeform,
        RechargeTicks:500,RangeMm:10000,CastClip:"enter",CastTicks:5,EffectTick:0,DurationTicks:160,AreaRadiusMm:10000,
        DpsMilli:20000,LoopClip:"tree",ExitClip:"exit",ExitTicks:5,RequiredTechnique:"tree");
    private static readonly UnitDefinition HumanUnit=UnitDefinition.Probe with {Id=20,Name="human",MaxHealth=200,Abilities=[Morph,Herbs]};
    private static readonly UnitDefinition Bear=UnitDefinition.Probe with {Id=55,Name="bear",MaxHealth=420,ArmorMilli=2000,DpsMilli=17000,Abilities=[Human,Blow],KnockbackImmune=true};
    private static readonly UnitDefinition Huorn=UnitDefinition.Probe with {Id=53,Name="huorn",MaxHealth=740,Abilities=[Tree],KnockbackImmune=true};
    private static readonly UnitDefinition Dummy=UnitDefinition.Probe with {Name="dummy",Passive=true,MaxHealth=1000};
    private static SimWorld World(bool researched=true)
    {
        var w=new SimWorld(new GridNavigation(W,60,1000,Enumerable.Repeat(true,W*60).ToArray(),new int[(W+1)*61]),new CombatRules());
        w.Rules.Units.Add("human",HumanUnit); w.Rules.Units.Add("bear",Bear);
        if(researched) w.Researched.Add(0,["morph","blow","tree"]);
        return w;
    }
    private static void Step(SimWorld w,int n){for(int i=0;i<n;i++)w.Step();}
    private static void Cast(SimWorld w,SimUnit u,int slot)=>w.EnqueueAbility(u.Team,[u.Id],slot,u.Id);
    public static void Run(Action<bool,string> check)
    {
        foreach(var a in new[]{Morph,Human,Herbs,Blow,Tree})a.Validate();
        Morphing(check); Healing(check); ManualHerbs(check); Blows(check); Trees(check);
        string Replay(){var w=World();var u=w.Spawn(0,Cell(10),HumanUnit);Cast(w,u,0);Step(w,8);w.Spawn(1,Cell(10,23),Dummy);Cast(w,u,1);Step(w,25);return w.StateDigest();}
        check(Replay()==Replay(),"nature casts, forms and knockback replay deterministically");
    }
    private static void Morphing(Action<bool,string> check)
    {
        var w=World(false);var u=w.Spawn(0,Cell(10),HumanUnit);u.Health=100;
        Cast(w,u,0);Step(w,8);check(u.Definition==HumanUnit,"Bear Form is research gated");
        u.AbilityResearch.Add("morph");u.AbilityReadyAt[1]=500;Cast(w,u,0);Step(w,8);
        check(u.Definition==Bear && u.HealthMilli==210000 && u.Id==0,"morph preserves identity and damaged health percentage");
        check(u.PopulationUnit=="human" && u.Ability(1)?.Kind==AbilityKind.SavageBlow,"bear keeps its recruitment identity and exchanges Herbs for Savage Blow");
        check(u.AbilityReadyAt[0]>w.Tick && u.AbilityReadyAt[1]==0,"morph shares its recharge across forms without transferring Herbs recharge to Savage Blow");
        Cast(w,u,0);w.Step();check(u.Definition==Bear,"return transformation cannot bypass shared recharge");
        u.AbilityReadyAt[0]=0;Cast(w,u,0);Step(w,8);
        check(u.Definition==HumanUnit && u.HealthMilli==100000 && u.AbilityReadyAt[1]==500,"return restores human stats, health share and its own Herbs recharge");
        var cancel=World();var c=cancel.Spawn(0,Cell(10),HumanUnit);Cast(cancel,c,0);cancel.Step();cancel.Enqueue(0,[c.Id],OrderKind.Stop);Step(cancel,8);
        check(c.Definition==HumanUnit && c.AbilityReadyAt[0]==0,"Stop interrupts transformation without changing form or spending recharge");
        var crowded=World();crowded.Rules.Units["bear"]=Bear with {BodyRadiusMm=2000};var h=crowded.Spawn(0,Cell(10),HumanUnit);crowded.Spawn(0,Cell(12),Dummy);
        Cast(crowded,h,0);Step(crowded,8);check(h.Definition==HumanUnit && !h.Casting,"a larger transformed body cannot overlap a neighbor");
        var failed=World();var f=failed.Spawn(0,Cell(10),HumanUnit);Cast(failed,f,0);failed.Step();f.Health=0;Step(failed,8);
        check(f.Definition==HumanUnit && f.Health==0,"death during transformation cannot resurrect or change form");
        var trained=World();trained.Researched[0].Add("claw");
        trained.Techniques.Add(new("claw","Claw","lore",0,0,0,1,null,null,new Dictionary<string,(int,int)>{{"human",(1000,1000)},{"bear",(3000,2000)}}));
        var veteran=trained.Spawn(0,Cell(10),HumanUnit);Cast(trained,veteran,0);Step(trained,8);
        check(veteran.TechniqueDpsMilli==3000 && veteran.ArmorMilli==4000,"morph recalculates the new form's researched damage and armor");
    }
    private static void Healing(Action<bool,string> check)
    {
        var w=World();var h=w.Spawn(0,Cell(10),HumanUnit);var a=w.Spawn(0,Cell(13),Dummy);a.Health=100;Step(w,4);
        check(a.Health==175 && h.AbilityReadyAt[1]==122,"idle Herbs heals 75 at the animation event and spends its six-second recharge then");
        Step(w,115);check(a.Health==175,"Herbs cannot heal during recharge");Step(w,10);check(a.Health==250,"automatic healing repeats after recharge");
        check(!w.ValidAbilityTarget(h,Herbs,w.Spawn(1,Cell(40),Dummy)),"Herbs rejects enemy targets");
        var move=World();var m=move.Spawn(0,Cell(10),HumanUnit);var hurt=move.Spawn(0,Cell(23),Dummy);hurt.Health=100;
        Step(move,150);check(hurt.Health>100,"idle healer approaches an ally inside the 15 m response radius");
        var hold=World();var s=hold.Spawn(0,Cell(10),HumanUnit);var far=hold.Spawn(0,Cell(23),Dummy);far.Health=100;s.Order=OrderKind.HoldGround;Step(hold,150);
        check(s.X==10500 && far.Health==100,"holding healers do not leave position to tend distant allies");
        var held=World();var healer=held.Spawn(0,Cell(10),HumanUnit);healer.Order=OrderKind.HoldGround;var nearby=held.Spawn(0,Cell(13),Dummy);nearby.Health=100;
        Step(held,8);check(nearby.Health==175 && healer.Order==OrderKind.HoldGround,"automatic Herbs preserves Hold Ground after tending an ally in range");
    }
    private static void ManualHerbs(Action<bool,string> check)
    {
        var w=World();var h=w.Spawn(0,Cell(10),HumanUnit);var first=w.Spawn(0,Cell(13),Dummy);var chosen=w.Spawn(0,Cell(16),Dummy);
        first.Health=100;chosen.Health=300;h.Order=OrderKind.HoldGround;w.Step();
        check(h.RedirectableHerbs && h.Casting && h.OrderTarget==first.Id,"automatic Herbs wind-up remains available for manual redirection");
        w.EnqueueHerbsTargeting(0,h.Id,true);w.Step();
        check(h.HerbsTargeting && !h.Casting && h.AbilityReadyAt[1]==0 && first.Health==100,"arming manual Herbs cancels only an unapplied automatic heal without spending cooldown");
        Step(w,8);check(!h.Casting && first.Health==100 && chosen.Health==300,"automatic healing cannot consume the ability while the player chooses a target");
        w.EnqueueAbility(0,[h.Id],1,chosen.Id);w.EnqueueHerbsTargeting(0,h.Id,false);Step(w,4);
        check(!h.HerbsTargeting && chosen.Health==375 && first.Health==100 && h.HerbReturnOrder==null,"manual Herbs heals the chosen ally and releases its targeting reservation");
        int recharge=h.AbilityReadyAt[1];w.EnqueueHerbsTargeting(0,h.Id,true);w.Step();
        check(h.AbilityReadyAt[1]==recharge,"manual targeting cannot reset a spent Herbs cooldown");
        w.EnqueueHerbsTargeting(0,h.Id,false);Step(w,120);check(first.Health>100,"automatic Herbs resumes after manual casting and recharge");
        var cancel=World();var c=cancel.Spawn(0,Cell(10),HumanUnit);var injured=cancel.Spawn(0,Cell(13),Dummy);injured.Health=100;
        cancel.EnqueueHerbsTargeting(1,c.Id,true);cancel.Step();check(!c.HerbsTargeting && c.Casting,"an opponent cannot reserve a healer's ability");
        cancel.EnqueueHerbsTargeting(0,c.Id,true);cancel.Step();cancel.EnqueueHerbsTargeting(0,c.Id,false);Step(cancel,4);
        check(!c.HerbsTargeting && injured.Health==175,"cancelling manual targeting resumes automatic healing");
        var direct=World();var d=direct.Spawn(0,Cell(10),HumanUnit);var auto=direct.Spawn(0,Cell(13),Dummy);var target=direct.Spawn(0,Cell(16),Dummy);
        auto.Health=100;target.Health=300;direct.Step();direct.EnqueueAbility(0,[d.Id],1,target.Id);Step(direct,4);
        check(auto.Health==100 && target.Health==375 && d.HeldOrder==null,"a direct manual Herbs command also overrides an automatic wind-up");
        string Replay()
        {
            var r=World();var caster=r.Spawn(0,Cell(10),HumanUnit);var ally=r.Spawn(0,Cell(13),Dummy);ally.Health=100;
            r.EnqueueHerbsTargeting(0,caster.Id,true);Step(r,5);r.EnqueueHerbsTargeting(0,caster.Id,false);Step(r,5);return r.StateDigest();
        }
        check(Replay()==Replay(),"manual Herbs targeting and cancellation replay deterministically");
        var original=World();var originalCaster=original.Spawn(0,Cell(10),HumanUnit);original.Spawn(0,Cell(13),Dummy).Health=100;
        original.EnqueueHerbsTargeting(0,originalCaster.Id,true);Step(original,5);original.EnqueueHerbsTargeting(0,originalCaster.Id,false);Step(original,5);
        var replay=World();replay.Spawn(0,Cell(10),HumanUnit);replay.Spawn(0,Cell(13),Dummy).Health=100;
        replay.Commands.AddRange(original.Commands);Step(replay,10);
        check(original.StateDigest()==replay.StateDigest(),"recorded reservation commands reproduce automatic healing without HUD state");
    }
    private static void Blows(Action<bool,string> check)
    {
        var w=World();var b=w.Spawn(0,Cell(20),Bear);var front=w.Spawn(1,Cell(20,23),Dummy);var back=w.Spawn(1,Cell(20,17),Dummy);
        var side=w.Spawn(1,Cell(24),Dummy);var ally=w.Spawn(0,Cell(18,22),Dummy);var immune=w.Spawn(1,Cell(18,23),Dummy with{KnockbackImmune=true});
        Cast(w,b,1);Step(w,3);
        check(front.Health==925 && side.Health==925 && back.Health==1000 && ally.Health==1000,"Savage Blow hits the inclusive 4 m forward arc and excludes allies and the rear");
        check(front.KnockbackUntil>w.Tick && immune.Health==925 && immune.KnockbackUntil<0,"knockback immunity prevents displacement without preventing the 75 damage");
        check(w.AbilityEvents.Any(e=>e.Kind==AbilityEventKind.SavageBlow && e.Target==immune.Id),"knockback-immune victims still receive Savage Blow's impact effect");
        int start=front.Z;Step(w,20);check(front.Z>start && !SimWorld.BodiesOverlap(front,b),"knockback moves the victim away without overlapping the caster");
        check(b.AbilityReadyAt[1]>w.Tick,"Savage Blow has a fifteen-second recharge");
        var wall=World();var source=wall.Spawn(0,Cell(20),Bear);var victim=wall.Spawn(1,Cell(20,23),Dummy);
        wall.Spawn(0,Cell(20,25),Dummy);Cast(wall,source,1);Step(wall,22);
        check(victim.Z<25500 && wall.Units.Where(u=>u.Id!=victim.Id).All(u=>!SimWorld.BodiesOverlap(victim,u)),"knockback stops before occupied ground instead of passing through units");
        var blocked=World();var bear=blocked.Spawn(0,Cell(20),Bear);var unit=blocked.Spawn(1,Cell(20,23),Dummy);
        blocked.Navigation.Allowed[Cell(20,25)]=false;Cast(blocked,bear,1);Step(blocked,22);
        check(unit.Z<25000 && blocked.Navigation.CanTraverseGroundPoints(unit.X,unit.Z,unit.X,unit.Z),"knockback cannot cross blocked terrain");
        // Build-129: the source Knocked Back clip plays out on the ground, then Get Up, before the victim acts again.
        var getUp=World();var blower=getUp.Spawn(0,Cell(20),Bear);
        var faller=getUp.Spawn(1,Cell(20,23),Dummy with{KnockbackTicks=30,GetUpTicks=25,KnockbackClip="knocked",GetUpClip="get up"});
        Cast(getUp,blower,1);Step(getUp,3);
        int landed=faller.KnockbackUntil,recovered=faller.KnockbackRecoverUntil;
        check(recovered==Math.Max(landed,faller.KnockbackStart+30)+25,"a knocked-back unit lies until its clip ends, then gets up");
        Step(getUp,landed-getUp.Tick+1);int lying=faller.Z;
        check(faller.NatureLocked(getUp.Tick) && faller.KnockbackUntil<=getUp.Tick,"the victim stays down after landing");
        Step(getUp,recovered-getUp.Tick);
        check(!faller.NatureLocked(getUp.Tick) && faller.Z==lying,"the victim acts again once it has got up, where it landed");
    }
    private static void Trees(Action<bool,string> check)
    {
        var w=World();var h=w.Spawn(0,Cell(20),Huorn);h.Health=400;var victim=w.Spawn(1,Cell(30),Dummy);var outside=w.Spawn(1,Cell(31),Dummy);var ally=w.Spawn(0,Cell(20,25),Dummy);
        Cast(w,h,0);w.Step();
        check(victim.Dazed?.Until==160 && outside.Dazed==null && ally.Dazed==null,"Treeform dazes enemies at the inclusive 10 m boundary only");
        w.Enqueue(1,[victim.Id],OrderKind.Move,Cell(40));Step(w,158);
        check(victim.X==30500 && h.X==20500 && h.Health==559,"Treeform holds caster and dazed enemy still while healing exactly 20 HP/s");
        check(w.StatusesOf(victim).Any(s=>s.Kind==StatusKind.Dazed) && w.StatusesOf(h).Any(s=>s.Kind==StatusKind.Treeform),"Treeform and daze expose timed status icons");
        Step(w,8);check(victim.X>30500 && h.TreeformAbility==null && h.Health==560,"daze expires and the Huorn finishes its exit animation after eight seconds");
        check(h.AbilityReadyAt[0]==500,"Treeform recharge is twenty-five seconds");
        var stun=World();var t=stun.Spawn(0,Cell(20),Huorn);var protectedUnit=stun.Spawn(1,Cell(24),Dummy with {Abilities=[new(99,0,"Immunity","immune",true,AbilityKind.Immunity,Immunities:StatusProtection.Stun)]});
        Cast(stun,t,0);stun.Step();check(protectedUnit.Dazed==null,"stun immunity blocks Treeform's daze");
    }
    public static void Cached(Action<bool,string> check,string cache)
    {
        var manifest=CacheManifest.Load(cache);manifest.Verify();
        var rules=CombatRules.Parse(File.ReadAllText(manifest.Resolve(manifest.Root.GetProperty("combat_rules").GetString()!)));
        var human=rules.Units["UNIT_G_BEORNING"];var bear=rules.Units["UNIT_G_BEAR"];var tree=rules.Units["UNIT_G_HUORN"];
        check(human.Abilities![1] is {Kind:AbilityKind.HealingHerbs,HealMilli:75000,RangeMm:8000,ResponseRangeMm:15000,RechargeTicks:120},"cached Herbs uses original unit healing fields");
        check(bear is {MaxHealth:420,ArmorMilli:2000,DpsMilli:17000,KnockbackImmune:true} && bear.Abilities![0].MorphUnit==human.Name,"cached bear has source stats and return form");
        check(bear.Abilities![1] is {Kind:AbilityKind.SavageBlow,HealMilli:75000,AreaRadiusMm:4000,ArcDegrees:260,RechargeTicks:300},"cached Savage Blow uses original damage, arc, radius and recharge");
        check(tree.Abilities![0] is {Kind:AbilityKind.Treeform,DurationTicks:160,AreaRadiusMm:10000,DpsMilli:20000,RechargeTicks:500},"cached Treeform follows the spell's child radius, timer and healing");
        var w=new SimWorld(new GridNavigation(W,60,1000,Enumerable.Repeat(true,W*60).ToArray(),new int[(W+1)*61]),rules);
        w.Researched.Add(0,["TECHNIQUE_BEAR_FORM","TECHNIQUE_SAVAGE_BLOW","TECHNIQUE_TREEFORM"]);
        w.ProductionRules[human.Name]=new(human.Name,human.DisplayName,100,70,3,20);
        var u=w.Spawn(0,Cell(20),human);Cast(w,u,0);Step(w,50);
        check(u.Definition==bear && w.PopulationUsed(0)==3,"real Beorning morph keeps all three population reserved");
        u.Health=0;check(w.PopulationUsed(0)==0,"a transformed Beorning releases population when it dies");
    }
}
