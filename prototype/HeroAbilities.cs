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

    /// <summary>The caster's own finished War Post under the pointer, the structure half of Blood Mark's targets.</summary>
    private ConstructionSite? BloodPostAt(Vector2 point,SimUnit caster)
        => CaptureSiteAt(point) is { } id && _world!.ConstructionSites.TryGetValue(id,out var post) && _world.ValidBloodPost(caster.Team,post) ? post : null;

    private void UpdateAbilityCursor()
    {
        if (_pendingGlobalPower >= 0 && _orders.Pending != OrderKind.UseGlobalPower) _pendingGlobalPower = -1;
        if (_herbsTargetingCaster >= 0 && (_orders.Pending != OrderKind.UseAbility || _pendingAbilityCaster != _herbsTargetingCaster
            || !_selected.Contains(_herbsTargetingCaster))) ReserveHerbsTargeting(-1);
        if (_abilityAreaPreview != null) _abilityAreaPreview.Visible = false;
        if (_world == null) return;
        bool overInterface = _gameMenu.Visible || GetViewport().GuiGetHoveredControl() != null
            || _commands.IsVisibleInTree() && _commands.GetGlobalRect().HasPoint(_pointerPosition)
            || _toolsPanel.IsVisibleInTree() && _toolsPanel.GetGlobalRect().HasPoint(_pointerPosition);
        if (_orders.Pending is not (OrderKind.UseAbility or OrderKind.UseGlobalPower or OrderKind.UsePlacePower))
        { _commands.UpdateCursor(_orders.Pending,overInterface:overInterface); return; }
        if (_orders.Pending == OrderKind.UseGlobalPower) { UpdateGlobalPowerCursor(overInterface); return; }
        if (_orders.Pending == OrderKind.UsePlacePower) { UpdatePlacePowerCursor(overInterface); return; }
        bool? valid = null;
        if (!LivingFriendly(_pendingAbilityCaster)) { CancelTargeting(); _commands.UpdateCursor(null); return; }
        var caster = _world.Units[_pendingAbilityCaster]; var ability = caster.Ability(_pendingAbilitySlot);
        if (!overInterface && ability != null)
        {
            if (ability.BuildingTarget)
                valid = CaptureSiteAt(_pointerPosition) is { } site && _world.ValidCaptureTarget(caster,ability,site);
            else if (ability.GroundTarget && AbilityGroundAt(_pointerPosition) is { } ground)
            {
                valid = _world.ValidGroundAbilityTarget(caster,ability,ground.Node) && !caster.Casting && caster.AbilityReadyAt[ability.Slot] <= _world.Tick;
                ShowAbilityArea(ground.Position,ability.AreaRadiusMm,valid == true);
            }
            else if (ability.Kind == AbilityKind.BloodMark && UnitAt(_pointerPosition) == null)
                valid = BloodPostAt(_pointerPosition,caster) != null && !caster.Casting && caster.AbilityReadyAt[ability.Slot] <= _world.Tick;
            else
            {
                var target = UnitAt(_pointerPosition);
                if (target != null) valid = ReachableAbilityTarget(caster,ability,target) && (!caster.Casting || ability.Kind == AbilityKind.HealingHerbs && caster.RedirectableHerbs)
                    && caster.AbilityReadyAt[ability.Slot] <= _world.Tick;
            }
        }
        _commands.UpdateCursor(OrderKind.UseAbility,valid,overInterface,ability?.Cursor);
    }

    private void ChooseAbility(int slot)
    {
        if (_world == null) return;
        var selected = _world.Units.Where(u => _selected.Contains(u.Id) && u.Present).ToArray();
        var caster = CommandPanel.AbilitySource(selected,PlayerTeam);
        if (caster?.Ability(slot) is not {} ability) return;
        var candidates = selected.Where(u => u.Team == PlayerTeam && u.Ability(slot) != null).ToArray();
        if (!caster.AbilityPurchased(ability))
        {
            if (!_world.CanPurchaseAbility(PlayerTeam,caster.Id,slot,out string reason)) { Notify(reason); return; }
            _world.EnqueueAbilityPurchase(PlayerTeam,caster.Id,slot);
            _status.Text = ability.Name+" unlocked"+(_paused ? " (queued while paused)" : ""); return;
        }
        if (caster.Negated != null) { Notify(ability.Name+": abilities are disabled by Negate Ability"); return; }
        if (!caster.AbilityEnabled(ability)) { Notify(ability.Name+": research at the "+(ability.Kind is AbilityKind.Camouflage or AbilityKind.EagleEye or AbilityKind.ElvenCloak or AbilityKind.EagleFletching ? "Wilderness Outpost" : ability.Kind is AbilityKind.Dispel or AbilityKind.SearingLight or AbilityKind.Morph or AbilityKind.SavageBlow or AbilityKind.Treeform ? "House of Lore" : "Forge")+" first"); return; }
        if (ability.Kind == AbilityKind.Unsimulated) { Notify(ability.Name+" is not simulated yet"); return; }
        if (ability.Instant || ability.Kind is AbilityKind.Camouflage or AbilityKind.Sneak)
        {
            CancelTargeting();
            // Source GroupActivate: Camouflage serves all selected Rangers, as dummy buttons serve all selected dummies.
            _world.EnqueueAbility(PlayerTeam,candidates.Where(u => u.Ability(slot)?.Kind == ability.Kind).Select(u => u.Id),slot,-1);
            _status.Text = ability.Name+(_paused ? " queued (paused)" : ""); return;
        }
        if (ability.Passive) { _status.Text = ability.Name+" is passive"; return; }
        if (ability.Kind == AbilityKind.Ring)
        {
            CancelTargeting();
            _world.EnqueueAbility(PlayerTeam,[caster.Id],slot,-1);
            _status.Text = ability.Name+(caster.RingActive ? " removed" : " worn")+(_paused ? " (paused)" : ""); return;
        }
        bool detonation = ability.Kind == AbilityKind.SatchelCharge && _world.Satchels.Any(b => b.Owner == caster.Id);
        if (!detonation && (caster.Casting && !(ability.Kind == AbilityKind.HealingHerbs && caster.RedirectableHerbs)
            || caster.NatureLocked(_world.Tick) || caster.AbilityReadyAt[slot] > _world.Tick))
        { _status.Text = ability.Name+" is recharging"; return; }
        if (ability.ActivateFateCost > 0 && !(_world.Economies.TryGetValue(PlayerTeam,out var bank) && bank.Fate >= ability.ActivateFateCost))
        { Notify(ability.Name+" needs "+ability.ActivateFateCost+" Fate"); return; }
        if (!_world.CanAffordAbility(caster,ability))
        { Notify($"{ability.Name} needs {ability.ActivateFoodCost} food and {ability.ActivateOreCost} ore"); return; }
        if (ability.SelfCast)
        {
            CancelTargeting();
            // Guardian Wind is cast on the spot: no target to choose.
            _world.EnqueueAbility(PlayerTeam,ability.Kind is AbilityKind.Morph or AbilityKind.Treeform
                ? candidates.Where(u => u.Ability(slot)?.Id == ability.Id).Select(u => u.Id) : [caster.Id],slot,caster.Id);
            _status.Text = ability.Name+(detonation ? " detonated" : "")+(_paused ? " queued (paused)" : ""); return;
        }
        _pendingAbilityCaster = caster.Id; _pendingAbilitySlot = slot;
        ReserveHerbsTargeting(ability.Kind == AbilityKind.HealingHerbs ? caster.Id : -1);
        ClearConstructionTargeting();
        _orders.Begin(OrderKind.UseAbility); _dragging = false; _hud.SelectionBox = null;
        _status.Text = ability.Name+AbilityTargetPrompt(ability);
    }

    private void TargetAbility(Vector2 point)
    {
        if (_world == null || !LivingFriendly(_pendingAbilityCaster)) { CancelTargeting(); return; }
        var caster = _world.Units[_pendingAbilityCaster]; var ability = caster.Ability(_pendingAbilitySlot);
        if (ability?.BuildingTarget == true)
        {
            if (CaptureSiteAt(point) is not { } site || !_world.ValidCaptureTarget(caster,ability,site))
            { _status.Text = ability.Name+AbilityTargetPrompt(ability); return; }
            _world.EnqueueAbility(PlayerTeam,[caster.Id],ability.Slot,site); _orders.Cancel();
            _status.Text = ability.Name+(_paused ? " queued (paused)" : ""); return;
        }
        if (ability?.GroundTarget == true)
        {
            if (AbilityGroundAt(point) is not { } ground || !_world.ValidGroundAbilityTarget(caster,ability,ground.Node))
            { _status.Text = ability.Name+": choose a reachable point on the battlefield"; return; }
            if (caster.AbilityReadyAt[ability.Slot] > _world.Tick || caster.Casting) { CancelTargeting(); return; }
            _world.EnqueueGroundAbility(PlayerTeam,caster.Id,ability.Slot,ground.Node,(int)(ground.Position.X*1000),(int)(ground.Position.Z*1000));
            _orders.Cancel(); if (_abilityAreaPreview != null) _abilityAreaPreview.Visible = false;
            _status.Text = ability.Name+(_paused ? " queued (paused)" : ""); return;
        }
        if (ability?.Kind == AbilityKind.BloodMark && UnitAt(point) == null)
        {
            // The post half of Blood Mark: one of the caster's own War Posts, cast as a ground order at the post.
            if (BloodPostAt(point,caster) is not { } post) { _status.Text = ability.Name+AbilityTargetPrompt(ability); return; }
            if (caster.AbilityReadyAt[ability.Slot] > _world.Tick || caster.Casting) { CancelTargeting(); return; }
            var nav = _world.Navigation.ForBody(caster.Definition.BodyRadiusMm,caster.Definition.BodyHeightMm);
            int stand = nav.Nearest(_world.Navigation.Cell(post.X,post.Z),connectedTo: caster.Node);
            if (stand < 0) { _status.Text = ability.Name+": the War Post cannot be reached"; return; }
            _world.EnqueueGroundAbility(PlayerTeam,caster.Id,ability.Slot,stand,post.X,post.Z); _orders.Cancel();
            _status.Text = ability.Name+(_paused ? " queued (paused)" : ""); return;
        }
        var target = UnitAt(point);
        if (ability == null || target == null || !_world.ValidAbilityTarget(caster,ability,target))
        { _status.Text = ability?.Name+(ability == null ? "" : AbilityTargetPrompt(ability)); return; }
        if (caster.AbilityReadyAt[ability.Slot] > _world.Tick || caster.Casting && !(ability.Kind == AbilityKind.HealingHerbs && caster.RedirectableHerbs)) { CancelTargeting(); return; }
        if (!_world.Navigation.ForBody(caster.Definition.BodyRadiusMm,caster.Definition.BodyHeightMm).IsConnected(caster.Node,target.Node))
        { _status.Text = ability.Name+": target cannot be reached"; return; }
        _world.EnqueueAbility(PlayerTeam,[caster.Id],ability.Slot,target.Id); _orders.Cancel();
        ReserveHerbsTargeting(-1);
        _status.Text = ability.Name+(_paused ? " queued (paused)" : "");
    }

    private void PlayAbilitySound(int[]? events,int index,Vector3 position)
    {
        if (events == null || events.Length == 0) return;
        var sounds = _abilitySounds.Where(s => events.Contains(s.EventId)).ToArray();
        if (sounds.Length == 0 || _impactPlayers.Count == 0) return;
        var player = _impactPlayers[_impactCursor++%_impactPlayers.Count]; var sound = sounds[index%sounds.Length];
        player.Stream = sound.Stream; player.VolumeDb = sound.VolumeDb;
        player.Position = position; player.StreamPaused = _paused; player.Play();
    }

    private void PresentAbilityEvents()
    {
        PresentGlobalPowerEvents();
        PresentHeroSkillEvents();
        PresentElvenEvents();
        PresentNatureEvents();
        foreach (var toggle in _world!.AbilityEvents.Where(e => e.Kind is AbilityEventKind.RingOn or AbilityEventKind.RingOff))
            if (_world.Units[toggle.Unit].Ability(toggle.Slot)?.SoundEvents is [int on,int off])
                PlayAbilitySound([toggle.Kind == AbilityEventKind.RingOn ? on : off],0,UnitPosition(_world.Units[toggle.Unit]));
        foreach (var camouflage in _world.AbilityEvents.Where(e => e.Kind == AbilityEventKind.CamouflageOn))
            PlayAbilitySound(_world.Units[camouflage.Unit].Ability(camouflage.Slot)?.SoundEvents,0,UnitPosition(_world.Units[camouflage.Unit]));
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
