using Godot;
using Reforged.Simulation;
using Reforged.Rendering;

public partial class Main
{
    private int _pendingAbilityCaster = -1, _pendingAbilitySlot = -1;
    private readonly List<(Label3D Label,int EndTick)> _healLabels = [];
    private readonly List<(int EventId,AudioStreamWav Stream,float VolumeDb)> _abilitySounds = [];
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
        if (ability.Kind == AbilityKind.Unsimulated) { Notify(ability.Name+" is not simulated yet"); return; }
        if (ability.Instant)
        {
            // A training dummy's button serves every selected dummy at once.
            _world.EnqueueAbility(PlayerTeam,candidates.Where(u => u.Ability(slot)?.Kind == ability.Kind).Select(u => u.Id),slot,-1);
            _status.Text = ability.Name+(_paused ? " queued (paused)" : ""); return;
        }
        if (ability.Passive) { _status.Text = ability.Name+" is passive"; return; }
        if (ability.Kind == AbilityKind.Ring)
        {
            _world.EnqueueAbility(PlayerTeam,[caster.Id],slot,-1);
            _status.Text = ability.Name+(caster.RingActive ? " removed" : " worn")+(_paused ? " (paused)" : ""); return;
        }
        if (caster.Casting || caster.AbilityReadyAt[slot] > _world.Tick)
        { _status.Text = ability.Name+" is recharging"; return; }
        if (ability.Kind == AbilityKind.Ward)
        {
            // Guardian Wind is cast on the spot: no target to choose.
            _world.EnqueueAbility(PlayerTeam,[caster.Id],slot,caster.Id);
            _status.Text = ability.Name+(_paused ? " queued (paused)" : ""); return;
        }
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
        _world.EnqueueAbility(PlayerTeam,[caster.Id],ability.Slot,target.Id); _orders.Cancel();
        _status.Text = _paused ? "Greater Kingsfoil queued (paused)" : "Greater Kingsfoil";
    }

    private void PlayAbilitySound(int[]? events,int index,Vector3 position)
    {
        var sounds = _abilitySounds.Where(s => events == null || events.Contains(s.EventId)).ToArray();
        if (sounds.Length == 0 || _impactPlayers.Count == 0) return;
        var player = _impactPlayers[_impactCursor++%_impactPlayers.Count]; var sound = sounds[index%sounds.Length];
        player.Stream = sound.Stream; player.VolumeDb = sound.VolumeDb;
        player.Position = position; player.StreamPaused = _paused; player.Play();
    }

    private void PresentAbilityEvents()
    {
        foreach (var toggle in _world!.AbilityEvents.Where(e => e.Kind is AbilityEventKind.RingOn or AbilityEventKind.RingOff))
            if (_world.Units[toggle.Unit].Ability(toggle.Slot)?.SoundEvents is [int on,int off])
                PlayAbilitySound([toggle.Kind == AbilityEventKind.RingOn ? on : off],0,UnitPosition(_world.Units[toggle.Unit]));
        foreach (var effect in _world.AbilityEvents.Where(e => e.Kind == AbilityEventKind.Heal))
        {
            var target = _world.Units[effect.Target];
            var label = new Label3D { Text = string.Create(System.Globalization.CultureInfo.InvariantCulture,$"+{effect.HealthMilli/1000m:0.#}"),FontSize = 44,PixelSize = .018f,
                Modulate = new Color("b4ff93"),OutlineModulate = new Color("173312"),OutlineSize = 9,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest = true,
                Position = UnitPosition(target)+Vector3.Up*(target.Definition.BodyHeightMm/1000f+.6f) };
            AddChild(label); _healLabels.Add((label,_world.Tick+24));
            if (_healingLayers.Length > 0)
            {
                var visual = new HealingEffect(effect.Target,effect.Tick/(double)SimWorld.TicksPerSecond,_healingDuration,_healingLayers);
                AddChild(visual.Root); _healingEffects.Add(visual);
            }
            PlayAbilitySound(_world.Units[effect.Unit].Ability(effect.Slot)?.SoundEvents,effect.Unit,UnitPosition(target));
        }
        foreach (var armor in _world.AbilityEvents.Where(e => e.Kind == AbilityEventKind.Armor))
        {
            var dummy = _world.Units[armor.Unit];
            var label = new Label3D { Text = string.Create(System.Globalization.CultureInfo.InvariantCulture,$"Armor {armor.HealthMilli/1000m:0.#}"),FontSize = 40,PixelSize = .018f,
                Modulate = new Color("e6dcb4"),OutlineModulate = new Color("2a2418"),OutlineSize = 9,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest = true,
                Position = UnitPosition(dummy)+Vector3.Up*(dummy.Definition.BodyHeightMm/1000f+.6f) };
            AddChild(label); _healLabels.Add((label,_world.Tick+24));
        }
        foreach (var ward in _world.AbilityEvents.Where(e => e.Kind == AbilityEventKind.Ward))
        {
            // ponytail: the source particle effects (160, 329) are not imported; a label marks the cast.
            var caster = _world.Units[ward.Unit];
            var label = new Label3D { Text = caster.Ability(ward.Slot)?.Name ?? "",FontSize = 40,PixelSize = .018f,
                Modulate = new Color("bfe6ff"),OutlineModulate = new Color("10283a"),OutlineSize = 9,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest = true,
                Position = UnitPosition(caster)+Vector3.Up*(caster.Definition.BodyHeightMm/1000f+.6f) };
            AddChild(label); _healLabels.Add((label,_world.Tick+40));
            PlayAbilitySound(caster.Ability(ward.Slot)?.SoundEvents,ward.Unit,UnitPosition(caster));
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
