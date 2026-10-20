using Godot;
using Reforged.Simulation;
using Reforged.Rendering;

public partial class Main
{
    private int _pendingAbilityCaster = -1, _pendingAbilitySlot = -1;
    private readonly List<(Label3D Label,int EndTick)> _healLabels = [];
    private readonly List<(AudioStreamWav Stream,float VolumeDb)> _abilitySounds = [];
    private HealingEffect.Layer[] _healingLayers = [];
    private double _healingDuration, _healingTime;
    private readonly List<HealingEffect> _healingEffects = [];
    private Vector2 _pointerPosition = new(-1,-1);

    private bool ReachableAbilityTarget(SimUnit caster,AbilityDefinition ability,SimUnit? target)
        => target != null && _world!.ValidAbilityTarget(caster,ability,target)
            && _world.Navigation.ForBody(caster.Definition.BodyRadiusMm,caster.Definition.BodyHeightMm).IsConnected(caster.Node,target.Node);

    private void UpdateAbilityCursor()
    {
        if (_world == null || _orders.Pending != OrderKind.UseAbility) return;
        bool overInterface = _commands.IsVisibleInTree() && _commands.GetGlobalRect().HasPoint(_pointerPosition)
            || _toolsPanel.IsVisibleInTree() && _toolsPanel.GetGlobalRect().HasPoint(_pointerPosition);
        bool? valid = null;
        if (!LivingFriendly(_pendingAbilityCaster)) { CancelTargeting(); _commands.UpdateCursor(null); return; }
        var caster = _world.Units[_pendingAbilityCaster]; var ability = caster.Ability(_pendingAbilitySlot);
        if (!overInterface && ability != null)
        {
            var target = UnitAt(_pointerPosition);
            if (target != null) valid = ReachableAbilityTarget(caster,ability,target) && !caster.Casting && caster.AbilityReadyAt[ability.Slot] <= _world.Tick;
        }
        _commands.UpdateCursor(OrderKind.UseAbility,valid,overInterface);
    }

    private void ChooseAbility(int slot)
    {
        if (_world == null) return;
        var candidates = _world.Units.Where(u => LivingFriendly(u.Id) && _selected.Contains(u.Id) && u.Ability(slot) != null)
            .OrderBy(u => u.AbilityReadyAt[slot]).ThenBy(u => u.Id).ToArray();
        if (candidates.Length == 0) return;
        var caster = candidates[0]; var ability = caster.Ability(slot)!;
        if (ability.Passive) { _status.Text = ability.Name+" is passive"; return; }
        if (caster.Casting || caster.AbilityReadyAt[slot] > _world.Tick)
        { _status.Text = ability.Name+" is recharging"; return; }
        _pendingAbilityCaster = caster.Id; _pendingAbilitySlot = slot;
        ClearConstructionTargeting();
        _orders.Begin(OrderKind.UseAbility); _dragging = false; _hud.SelectionBox = null;
        _status.Text = ability.Name+": choose an injured friendly unit (or Aragorn)";
    }

    private void TargetAbility(Vector2 point)
    {
        if (_world == null || !LivingFriendly(_pendingAbilityCaster)) { CancelTargeting(); return; }
        var caster = _world.Units[_pendingAbilityCaster]; var ability = caster.Ability(_pendingAbilitySlot);
        var target = UnitAt(point);
        if (ability == null || target == null || !_world.ValidAbilityTarget(caster,ability,target))
        { _status.Text = "Greater Kingsfoil: choose a living friendly unit below 99% health"; return; }
        if (caster.AbilityReadyAt[ability.Slot] > _world.Tick || caster.Casting) { CancelTargeting(); return; }
        if (!_world.Navigation.ForBody(caster.Definition.BodyRadiusMm,caster.Definition.BodyHeightMm).IsConnected(caster.Node,target.Node))
        { _status.Text = "Greater Kingsfoil: target cannot be reached"; return; }
        _world.EnqueueAbility(0,[caster.Id],ability.Slot,target.Id); _orders.Cancel();
        _status.Text = _paused ? "Greater Kingsfoil queued (paused)" : "Greater Kingsfoil";
    }

    private void PresentAbilityEvents()
    {
        foreach (var effect in _world!.AbilityEvents.Where(e => e.Kind == AbilityEventKind.Heal))
        {
            var target = _world.Units[effect.Target];
            var label = new Label3D { Text = $"+{effect.HealthMilli/1000m:0.#}",FontSize = 44,PixelSize = .018f,
                Modulate = new Color("b4ff93"),OutlineModulate = new Color("173312"),OutlineSize = 9,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest = true,
                Position = UnitPosition(target)+Vector3.Up*(target.Definition.BodyHeightMm/1000f+.6f) };
            AddChild(label); _healLabels.Add((label,_world.Tick+24));
            if (_healingLayers.Length > 0)
            {
                var visual = new HealingEffect(effect.Target,effect.Tick/(double)SimWorld.TicksPerSecond,_healingDuration,_healingLayers);
                AddChild(visual.Root); _healingEffects.Add(visual);
            }
            if (_abilitySounds.Count > 0 && _impactPlayers.Count > 0)
            {
                var player = _impactPlayers[_impactCursor++%_impactPlayers.Count];
                var sound = _abilitySounds[effect.Unit%_abilitySounds.Count];
                player.Stream = sound.Stream; player.VolumeDb = sound.VolumeDb;
                player.Position = UnitPosition(target); player.StreamPaused = _paused; player.Play();
            }
        }
        foreach (var flash in _healLabels.Where(f => _world.Tick >= f.EndTick).ToArray())
        { flash.Label.QueueFree(); _healLabels.Remove(flash); }
    }

    private void PresentHealingEffects()
    {
        if (_world == null) return;
        double fraction = _paused ? 1 : Engine.GetPhysicsInterpolationFraction();
        _healingTime = Math.Max(_healingTime,(_world.Tick-1+fraction)/SimWorld.TicksPerSecond);
        foreach (var effect in _healingEffects.ToArray())
        {
            var target = _world.Units[effect.Target];
            if (target.Health <= 0 || !effect.Draw(_healingTime,_views[target.Id].Root.Position,target.Definition.BodyHeightMm/1000f))
            { effect.Free(); _healingEffects.Remove(effect); }
        }
    }

    private void SeekAbility(SimUnit unit,UnitView view,double fraction)
    {
        if (!unit.Casting || view.Player == null || unit.Ability(unit.AbilitySlot) is not { } ability) return;
        double elapsed = Math.Clamp(_world!.Tick-1-unit.CastStartTick+fraction,0,ability.CastTicks);
        view.Player.Seek(elapsed/ability.CastTicks*view.Player.CurrentAnimationLength,true);
        view.Appearance?.Apply(ability.CastClip,elapsed/ability.CastTicks);
    }
}
