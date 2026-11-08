using Godot;
using Reforged.Simulation;

public partial class Main
{
    private int _pendingPlacePower = -1;

    private void ChoosePlacePower()
    {
        if (_world == null || _observing || _selectedStructure is not int id) return;
        if (!_world.CanUsePlacePower(PlayerTeam,id,out string reason)) { Notify(reason); return; }
        CancelTargeting(); OpenBuildMenu(false);
        _pendingPlacePower = id; _orders.Begin(OrderKind.UsePlacePower);
        _dragging = false; _hud.SelectionBox = null;
        _status = PlaceAt(id)!.Name+": choose a point on the battlefield";
    }

    private void TargetPlacePower(Vector2 point)
    {
        if (_world == null || !_world.CanUsePlacePower(PlayerTeam,_pendingPlacePower,out string reason))
        { CancelTargeting(); return; }
        if (GlobalGroundAt(point) is not {} ground) return;
        int x = (int)(ground.Position.X*1000),z = (int)(ground.Position.Z*1000);
        if (!_world.ValidPlacePowerTarget(PlayerTeam,_pendingPlacePower,ground.Node,x,z))
        { Notify("Choose ground within range of this place"); return; }
        string name = PlaceAt(_pendingPlacePower)!.Name;
        _world.EnqueuePlacePower(PlayerTeam,_pendingPlacePower,ground.Node,x,z);
        CancelTargeting(); _status = name+(_paused ? " queued (paused)" : " cast");
    }

    private void UpdatePlacePowerCursor(bool overInterface)
    {
        if (_world == null || !_world.CanUsePlacePower(PlayerTeam,_pendingPlacePower,out _))
        { CancelTargeting(); _commands.UpdateCursor(null); return; }
        var power = _world.PlacePowers[PlaceAt(_pendingPlacePower)!.Type];
        bool valid = false;
        if (!overInterface && GlobalGroundAt(_pointerPosition) is {} ground)
        {
            valid = _world.ValidPlacePowerTarget(PlayerTeam,_pendingPlacePower,ground.Node,(int)(ground.Position.X*1000),(int)(ground.Position.Z*1000));
            ShowAbilityArea(ground.Position,power.RadiusMm,valid);
        }
        _commands.UpdateCursor(OrderKind.UsePlacePower,valid,overInterface,power.Cursor);
    }

    private void PresentPlacePowerEvents()
    {
        foreach (var cast in _world!.PlacePowerEvents)
        {
            var power = _world.PlacePowers[cast.Type];
            var position = PositionOf(cast.X,cast.Z);
            PlayAbilitySound(power.SoundEvents,cast.Tick,position);
            if (power.Kind == "Aviary") ElvenBurst(191,-1,position,cast.Tick);
        }
    }
}
