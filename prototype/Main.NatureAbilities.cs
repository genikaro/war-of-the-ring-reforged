using Godot;
using Reforged.Simulation;

public partial class Main
{
    private string? _natureRootFile;
    private readonly Dictionary<int,Node3D> _natureRoots = [];
    private int _herbsTargetingCaster = -1;

    private void ReserveHerbsTargeting(int caster)
    {
        if (_world == null || caster == _herbsTargetingCaster) return;
        if (_herbsTargetingCaster >= 0)
            _world.EnqueueHerbsTargeting(_world.Units[_herbsTargetingCaster].Team,_herbsTargetingCaster,false);
        _herbsTargetingCaster=caster;
        if (caster >= 0) _world.EnqueueHerbsTargeting(PlayerTeam,caster,true);
    }

    private void PresentNatureRoots()
    {
        foreach (var unit in _world!.Units.Where(u => u.Present && u.Dazed is { Ability.Kind: AbilityKind.Treeform } && u.Dazed.Until > _world.Tick))
        {
            if (_natureRootFile == null) continue;
            if (!_natureRoots.TryGetValue(unit.Id,out var root))
            {
                root=new Node3D {Name="EntanglingRoots"}; AddChild(root); _natureRoots.Add(unit.Id,root);
                // Six source shard emitters surround the victim; radial placement adapts their legacy transforms.
                for(int i=0;i<6;i++)
                {
                    float angle=i*Mathf.Tau/6,radius=Math.Max(.5f,unit.Definition.BodyRadiusMm/1000f);
                    var shard=LoadGlb(_natureRootFile); root.AddChild(shard);
                    shard.Position=new(Mathf.Sin(angle)*radius,0,Mathf.Cos(angle)*radius); shard.Rotation=new(0,angle,0);
                }
            }
            root.Position=UnitPosition(unit); root.Visible=_world.CanSeeUnit(ViewTeam,unit);
        }
        foreach (int id in _natureRoots.Keys.Where(id => !_world.Units[id].Present || _world.Units[id].Dazed?.Ability.Kind != AbilityKind.Treeform || !(_world.Units[id].Dazed?.Until > _world.Tick)).ToArray())
        { _natureRoots[id].QueueFree(); _natureRoots.Remove(id); }
    }
    private bool TryHerbOrder(SimUnit target,bool queued)
    {
        if (_world == null || target.Team != PlayerTeam || target.HealthMilli >= (long)target.MaxHealth*1000) return false;
        var healers = _world.Units.Where(u => _selected.Contains(u.Id) && u.Present && u.Team == PlayerTeam
            && u.EnabledAbilities.Any(a => a.Kind == AbilityKind.HealingHerbs)).ToArray();
        if (healers.Length == 0) return false;
        foreach (var healer in healers)
            _world.EnqueueAbility(PlayerTeam,[healer.Id],healer.EnabledAbilities.First(a => a.Kind == AbilityKind.HealingHerbs).Slot,target.Id);
        var others = _selected.Where(id => !healers.Any(u => u.Id==id)).ToArray();
        if (others.Length > 0) _world.Enqueue(PlayerTeam,others,OrderKind.Guard,target:target.Id,cell:target.Node,queued:queued);
        ShowUnitCommandFeedback(target); _status.Text="Healing Herbs"+(_paused ? " queued (paused)" : "");
        return true;
    }

    private (string Clip,double Fraction)? NatureAnimation(SimUnit unit,double tick)
    {
        // Build-129: Knocked Back plays at its own pace and holds its last (lying) frame, then Get Up plays.
        if (unit.KnockbackRecoverUntil > tick && unit.Definition.KnockbackClip != "")
        {
            var definition = unit.Definition;
            int down = unit.KnockbackRecoverUntil-definition.GetUpTicks;
            if (definition.GetUpTicks > 0 && definition.GetUpClip != "" && tick >= down)
                return (definition.GetUpClip,Math.Clamp((tick-down)/definition.GetUpTicks,0,1));
            int flight = definition.KnockbackTicks > 0 ? definition.KnockbackTicks : unit.KnockbackUntil-unit.KnockbackStart;
            return (definition.KnockbackClip,Math.Clamp((tick-unit.KnockbackStart)/flight,0,1));
        }
        if (unit.Dazed is { } dazed && dazed.Until > tick && unit.Definition.DazedClip != "")
            return (unit.Definition.DazedClip,Math.Max(0,tick-(dazed.Until-dazed.Ability.DurationTicks))/40%1);
        if (unit.TreeformAbility is { } tree)
        {
            double elapsed=tick-unit.TreeformStart;
            if (tick >= unit.TreeformUntil) return (tree.ExitClip,Math.Clamp((tick-unit.TreeformUntil)/tree.ExitTicks,0,1));
            if (elapsed < tree.CastTicks) return (tree.CastClip,Math.Clamp(elapsed/tree.CastTicks,0,1));
            return (tree.LoopClip,(elapsed-tree.CastTicks)/tree.CastTicks%1);
        }
        return null;
    }

    private void SeekNature(SimUnit unit,UnitView view,double fraction)
    {
        double tick=_world!.Tick-1+fraction;
        if (NatureAnimation(unit,tick) is { } animation && view.Player != null)
        {
            view.Player.Seek(animation.Fraction*view.Player.CurrentAnimationLength,true);
            view.Appearance?.Apply(animation.Clip,animation.Fraction);
        }
        if (unit.KnockbackUntil > tick && tick >= unit.KnockbackStart)
        {
            double seconds=(tick-unit.KnockbackStart)/SimWorld.TicksPerSecond;
            float lift=(float)Math.Max(0,unit.KnockbackLiftMm/1000.0*seconds-4.9*seconds*seconds);
            view.Root.Position+=Vector3.Up*lift;
        }
    }

    private void PresentNatureEvents()
    {
        foreach (var e in _world!.AbilityEvents)
        {
            if (e.Unit<0) continue;
            var unit=_world.Units[e.Unit]; var ability=unit.Ability(e.Slot);
            if (e.Kind==AbilityEventKind.MorphEffect) ElvenBurst(190,e.Unit,UnitPosition(unit),e.Tick);
            if (e.Kind==AbilityEventKind.SavageBlow) ElvenBurst(134,e.Target,UnitPosition(_world.Units[e.Target]),e.Tick);
            if (e.Kind==AbilityEventKind.Start && ability?.Kind is AbilityKind.Morph or AbilityKind.SavageBlow or AbilityKind.Treeform)
                PlayAbilitySound(ability.SoundEvents,e.Unit,UnitPosition(unit));
            if (e.Kind==AbilityEventKind.TreeformEnd) PlayAbilitySound(ability?.EndSoundEvents,e.Unit,UnitPosition(unit));
            if (e.Kind==AbilityEventKind.Dazed) PlayAbilitySound([279],e.Target,UnitPosition(_world.Units[e.Target]));
        }
    }
}
