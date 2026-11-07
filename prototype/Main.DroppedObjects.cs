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

    /// <summary>The exported Obelisk keeps only its idle-state meshes (the converter leaves the summon and collapse sets out
    /// of the scene); this loops the idle state's clip, which drifts the floating stones.</summary>
    private static void ShowObeliskIdle(Node3D view)
    {
        if (Descendants(view).OfType<AnimationPlayer>().FirstOrDefault() is { } player
            && player.GetAnimationList().FirstOrDefault(n => n.Contains("Long_Idle")) is { } idle)
        { player.GetAnimation(idle).LoopMode = Animation.LoopModeEnum.Linear; player.Play(idle); }
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
        // Build-164: a standing Obelisk and the purple ring of the 15 m it protects; enemies see it where their fog is lifted.
        foreach (var area in _world.GlobalAreas.Where(a => a.Power.Kind == GlobalPowerKind.Obelisk && a.Until > _world.Tick))
        {
            var key = ("obelisk",area.Team+64*area.Start); live.Add(key);
            if (!_droppedMarkers.TryGetValue(key,out var marker)) _droppedMarkers[key] = marker = DroppedMarker(area.Power.RadiusMm/1000f,new Color(.55f,.25f,.75f,.45f));
            marker.Position = new(-area.X/1000f,area.Y/1000f+.1f,area.Z/1000f);
            bool firstShow = !_droppedViews.ContainsKey(key);
            PresentDroppedModel(key,marker.Position-Vector3.Up*.1f,area.Team);
            if (firstShow && _droppedViews.TryGetValue(key,out var view)) ShowObeliskIdle(view);
            marker.Visible = _observing || area.Team == PlayerTeam || _world.InSight(ViewTeam,area.X,area.Z);
            if (_droppedViews.TryGetValue(key,out var shown)) shown.Visible = marker.Visible;
        }
        foreach (var key in _droppedMarkers.Keys.Where(k => !live.Contains(k)).ToArray())
        { _droppedMarkers[key].QueueFree(); _droppedMarkers.Remove(key); }
        foreach (var key in _droppedViews.Keys.Where(k => !live.Contains(k)).ToArray())
        { _droppedViews[key].QueueFree(); _droppedViews.Remove(key); }
    }
}

/// <summary>Build-172: a standing Obelisk can be selected, as in the original: its portrait, name, "Sentinel" and its health
/// out of 5000 (the selected panel of the Evil compendium's capture 29), with an empty action grid.</summary>
public partial class Main
{
    private int _selectedObelisk = -1;
    private const float ObeliskPickHeight = 7f,ObeliskPickRadius = 1.6f;

    private IEnumerable<GlobalArea> StandingObelisks() => _world == null ? []
        : _world.GlobalAreas.Where(a => a.Power.Kind == GlobalPowerKind.Obelisk && a.Until > _world.Tick
            && _world.ObeliskHealthMilli.GetValueOrDefault(a.Team) > 0
            && (_observing || a.Team == PlayerTeam || _world.InSight(ViewTeam,a.X,a.Z)));

    /// <summary>The team whose Obelisk lies under the pointer: its column, projected to the screen.</summary>
    private int? ObeliskAt(Vector2 mouse)
    {
        foreach (var area in StandingObelisks().OrderBy(a => a.Team))
        {
            var bottom = new Vector3(-area.X/1000f,area.Y/1000f,area.Z/1000f); var top = bottom+Vector3.Up*ObeliskPickHeight;
            if (_camera.IsPositionBehind(bottom) || _camera.IsPositionBehind(top)) continue;
            Vector2 a = _camera.UnprojectPosition(bottom),b = _camera.UnprojectPosition(top);
            float radius = Math.Max(10,a.DistanceTo(_camera.UnprojectPosition(bottom+_camera.GlobalBasis.X*ObeliskPickRadius)));
            var along = b-a; float t = along.LengthSquared() < .01f ? 0 : Math.Clamp((mouse-a).Dot(along)/along.LengthSquared(),0,1);
            if (mouse.DistanceTo(a+along*t) <= radius) return area.Team;
        }
        return null;
    }

    private Reforged.Presentation.StructureDetails? SelectedObeliskDetails()
    {
        if (StandingObelisks().FirstOrDefault(a => a.Team == _selectedObelisk) is not { } area) { _selectedObelisk = -1; return null; }
        int health = (int)((_world!.ObeliskHealthMilli.GetValueOrDefault(area.Team)+999)/1000);
        return new(Reforged.Presentation.StructureDetails.ObeliskId(area.Team),area.Team,area.Power.Name,area.Power.Icon,health,area.Power.ObjectHealth,
            "",100,"Sentinel",-1,Obelisk: true);
    }
}
