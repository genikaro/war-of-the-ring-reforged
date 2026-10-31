using Godot;
using Reforged.Simulation;

/// <summary>The source models of the objects the Evil heroes drop. A Satchel Charge lies in the open (a red ring marks its 4 m blast); a Poison Trap
/// is hidden from the other side (the source gives it IsInvisible), so only its own team and an observer see the green ring of its trigger range.</summary>
public partial class Main
{
    private readonly Dictionary<(string Kind,int Id),MeshInstance3D> _droppedMarkers = [];
    private readonly Dictionary<string,(string File,bool Invisible)> _droppedModels = [];
    private readonly Dictionary<(string Kind,int Id),Node3D> _droppedViews = [];

    private void PresentDroppedModel((string Kind,int Id) key,Vector3 position,int team)
    {
        if (!_droppedModels.TryGetValue(key.Kind,out var model)) return;
        if (!_droppedViews.TryGetValue(key,out var view))
        {
            view = LoadGlb(model.File,_teamColors[team]);
            AddChild(view); _droppedViews.Add(key,view);
        }
        view.Position = position;
        view.Visible = !model.Invisible || _observing || team == PlayerTeam;
    }

    private MeshInstance3D DroppedMarker(float radius,Color color)
    {
        var marker = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = radius-.08f,OuterRadius = radius,Rings = 40,RingSegments = 6 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color,ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,NoDepthTest = false } };
        AddChild(marker); return marker;
    }

    private void PresentDroppedObjects()
    {
        if (_world == null) return;
        var live = new HashSet<(string,int)>();
        foreach (var bomb in _world.Satchels)
        {
            var key = ("satchel",bomb.Id); live.Add(key);
            if (!_droppedMarkers.TryGetValue(key,out var marker)) _droppedMarkers[key] = marker = DroppedMarker(bomb.Ability.AreaRadiusMm/1000f,new Color(.85f,.15f,.1f,.55f));
            marker.Position = new(-bomb.X/1000f,bomb.Y/1000f+.1f,bomb.Z/1000f);
            PresentDroppedModel(key,marker.Position-Vector3.Up*.1f,bomb.Team);
            // The ring quickens as the fuse burns down.
            float left = Math.Max(0,bomb.Fuse-_world.Tick)/(float)Math.Max(1,bomb.Ability.DelayTicks);
            marker.Visible = (int)(_world.Tick/(2+left*10)) % 2 == 0 || left > .6f;
        }
        foreach (var trap in _world.Traps)
        {
            var key = ("trap",trap.Id); live.Add(key);
            if (!_droppedMarkers.TryGetValue(key,out var marker)) _droppedMarkers[key] = marker = DroppedMarker(trap.Ability.ResponseRangeMm/1000f,new Color(.3f,.85f,.2f,.4f));
            marker.Position = new(-trap.X/1000f,trap.Y/1000f+.1f,trap.Z/1000f);
            PresentDroppedModel(key,marker.Position-Vector3.Up*.1f,trap.Team);
            marker.Visible = _observing || trap.Team == PlayerTeam;
        }
        foreach (var key in _droppedMarkers.Keys.Where(k => !live.Contains(k)).ToArray())
        { _droppedMarkers[key].QueueFree(); _droppedMarkers.Remove(key); }
        foreach (var key in _droppedViews.Keys.Where(k => !live.Contains(k)).ToArray())
        { _droppedViews[key].QueueFree(); _droppedViews.Remove(key); }
    }
}
