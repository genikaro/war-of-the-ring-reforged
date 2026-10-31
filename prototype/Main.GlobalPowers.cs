using Godot;
using Reforged.Simulation;
using Reforged.Rendering;
using Reforged.Input;

public partial class Main
{
    private int _pendingGlobalPower = -1;
    private readonly Dictionary<GlobalArea,ElvenSpellEffect> _globalVisuals = [];
    private string? _bramblesModel;

    private NavigationHit? GlobalGroundAt(Vector2 point)
    {
        var origin=_camera.ProjectRayOrigin(point);var direction=_camera.ProjectRayNormal(point);
        return _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
    }

    private void ChooseGlobalPower(int slot)
    {
        if (_world == null || _observing) return;
        if (!_world.CanUseGlobalPower(PlayerTeam,slot,out string reason)) { Notify(reason); return; }
        CancelTargeting(); OpenBuildMenu(false);
        _pendingGlobalPower = slot;
        _orders.Begin(OrderKind.UseGlobalPower); _dragging = false; _hud.SelectionBox = null;
        _status.Text = _world.GlobalPower(slot,PlayerTeam)!.Name+": choose "+(_world.GlobalPower(slot,PlayerTeam)!.Kind == GlobalPowerKind.HeroicLegacy ? "a friendly hero" : "a point on the battlefield");
    }

    private void TargetGlobalPower(Vector2 point)
    {
        if (_world?.GlobalPower(_pendingGlobalPower,PlayerTeam) is not {} power) { CancelTargeting(); return; }
        if (!_world.CanUseGlobalPower(PlayerTeam,power.Slot,out string reason)) { Notify(reason); CancelTargeting(); return; }
        var ground = GlobalGroundAt(point); var target = power.Kind == GlobalPowerKind.HeroicLegacy ? UnitAt(point) : null;
        int node = ground?.Node ?? -1,x = (int)((ground?.Position.X ?? 0)*1000),z = (int)((ground?.Position.Z ?? 0)*1000);
        if (!_world.ValidGlobalTarget(PlayerTeam,power,node,x,z,target?.Id ?? -1))
        { Notify(power.Kind == GlobalPowerKind.HeroicLegacy ? "Choose a living friendly hero without Heroic Legacy" : "Choose clear ground for this summon"); return; }
        _world.EnqueueGlobalPower(PlayerTeam,power.Slot,node,x,z,target?.Id ?? -1);
        CancelTargeting();
        _status.Text = power.Name+(_paused ? " queued (paused)" : " cast");
    }

    private void UpdateGlobalPowerCursor(bool overInterface)
    {
        if (_world?.GlobalPower(_pendingGlobalPower,PlayerTeam) is not {} power || !_world.CanUseGlobalPower(PlayerTeam,_pendingGlobalPower,out _))
        { CancelTargeting(); _commands.UpdateCursor(null); return; }
        bool valid = false;
        if (!overInterface)
        {
            if (power.Kind == GlobalPowerKind.HeroicLegacy)
                valid = _world.ValidGlobalTarget(PlayerTeam,power,-1,0,0,UnitAt(_pointerPosition)?.Id ?? -1);
            else if (GlobalGroundAt(_pointerPosition) is {} ground)
            {
                valid = _world.ValidGlobalTarget(PlayerTeam,power,ground.Node,(int)(ground.Position.X*1000),(int)(ground.Position.Z*1000));
                ShowAbilityArea(ground.Position,power.RadiusMm,valid);
            }
        }
        _commands.UpdateCursor(OrderKind.UseGlobalPower,valid,overInterface,power.Cursor);
    }

    private void PresentGlobalPowerEvents()
    {
        foreach (var cast in _world!.GlobalPowerEvents)
        {
            var power = _world.Rules.GlobalPowers.Single(p => p.Id == cast.Power);
            var position = new Vector3(-cast.X/1000f,cast.Y/1000f,cast.Z/1000f);
            PlayAbilitySound(power.SoundEvents,cast.Tick,position);
            foreach (int effect in power.Kind switch {
                         GlobalPowerKind.Blind => new[] {225,400},GlobalPowerKind.HeroicLegacy => new[] {289},_ => Array.Empty<int>() })
                ElvenBurst(effect,-1,position,cast.Tick);
        }
    }

    private void PresentGlobalAreas()
    {
        var active = _world!.GlobalAreas.Where(a => a.Until > _world.Tick && a.Power.Kind is GlobalPowerKind.Brambles or GlobalPowerKind.BlessedWind).ToHashSet();
        foreach (var area in active)
        {
            if (!_globalVisuals.TryGetValue(area,out var visual))
            {
                int effect = area.Power.Kind == GlobalPowerKind.Brambles ? 287 : 285;
                if (!_elvenVisuals.TryGetValue(effect,out var data)) continue;
                visual = new ElvenSpellEffect(data.Layers,area:true,areaRadius:area.Power.RadiusMm/1000f);
                if (area.Power.Kind == GlobalPowerKind.Brambles && _bramblesModel is {} model)
                    visual.Root.AddChild(LoadGlb(model));
                AddChild(visual.Root); _globalVisuals.Add(area,visual);
            }
            visual.Draw(Math.Max(0,_healingTime-area.Start/(double)SimWorld.TicksPerSecond),
                new(-area.X/1000f,area.Y/1000f,area.Z/1000f),2,loop:true);
        }
        foreach (var area in _globalVisuals.Keys.Where(a => !active.Contains(a)).ToArray())
        { _globalVisuals[area].Free(); _globalVisuals.Remove(area); }
    }
}
