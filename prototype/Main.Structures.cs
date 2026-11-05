using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private int? _selectedStructure;
    private readonly Dictionary<string,JsonElement> _structureDefinitions = [];
    /// <summary>The info panel's name for each weapon of a defended building, in its weapon order.</summary>
    private readonly Dictionary<string,string[]> _defenseLabels = [];
    private readonly Dictionary<int,StructureVisual> _structureViews = [];
    private sealed record StructureVisual(int Id,int Team,string Type,int Resource,bool Obstacle,Node3D Root,MeshInstance3D Ring,
        (MeshInstance3D Mesh,Vector3[] Faces)[] Meshes)
    {
        public float CommandBlip;
        public Color RingColor;
    }

    private void LoadStructureDefinitions(JsonElement data)
    {
        if (!data.GetProperty("construction").TryGetProperty("buildings",out var buildings))
            throw new InvalidOperationException("Refresh structure data: python tools/skirmish_construction.py --cache converted/skirmish");
        foreach (var item in buildings.EnumerateObject()) _structureDefinitions.Add(item.Name,item.Value.Clone());
    }

    private void LoadNeutralStructures(JsonElement data)
    {
        if (!data.TryGetProperty("selectable_objects",out var objects))
            throw new InvalidOperationException("Refresh neutral selection: python tools/skirmish_construction.py --cache converted/skirmish");
        foreach (var item in objects.GetProperty("definitions").EnumerateObject()) _structureDefinitions[item.Name] = item.Value.Clone();
        // Build-161: the landmarks the map names are known before any random marker draws, whatever the placement order.
        _world!.ReserveMapPlaces(objects.GetProperty("placements").EnumerateArray().Select(p => p.GetProperty("type").GetString()!));
        foreach (var item in objects.GetProperty("placements").EnumerateArray())
        {
            int id = item.GetProperty("id").GetInt32();
            var root = _obstacleViews.GetValueOrDefault(id) ?? Descendants(this).OfType<Node3D>()
                .First(n => n.Name.ToString() == item.GetProperty("name").GetString());
            if (item.GetProperty("type").GetString()!.StartsWith("OBJECT_PLACE_OF_POWER")) AddPlaceOfPower(id,item.GetProperty("type").GetString()!,root);
            RegisterStructure(id,-1,PlaceAt(id)?.Type ?? item.GetProperty("type").GetString()!,root,_world!.Resources.ContainsKey(id) ? id : -1);
            _minimapMarkers.Add((item.GetProperty("type").GetString()!,-root.GlobalPosition.X*1000,root.GlobalPosition.Z*1000));
        }
        _commands.SetMinimapMarkers(_minimapMarkers);
    }

    private void RegisterStructure(int id,int team,string type,Node3D root,int resource = -1)
    {
        var meshes = Descendants(root).OfType<MeshInstance3D>().Where(m => m.Mesh != null && !m.HasMeta("place_control_zone"))
            .Select(m => (Mesh: m,Faces: m.Mesh!.GetFaces())).ToArray();
        var points = new List<Vector3>();
        var layer = _obstacleLayers.EnumerateArray().FirstOrDefault(o => o.GetProperty("id").GetInt32() == id);
        bool obstacle = layer.ValueKind != JsonValueKind.Undefined;
        var polygons = new List<Vector2[]>();
        if (obstacle)
            foreach (var box in layer.GetProperty("footprints").EnumerateArray())
                polygons.Add(box.GetProperty("polygon").EnumerateArray().Select(p => new Vector2(p[0].GetSingle(),p[1].GetSingle())).ToArray());
        else if (_world!.ConstructionSites.Values.FirstOrDefault(s => s.Obstacle == id) is { Building: { } building } site
            && _world.Placements.TryGetValue(building,out var placement))
        {
            // Runtime buildings have no entry in the map's saved obstacle layers. Use their
            // placed footprint, rather than the bounds of every hidden construction/ruin mesh.
            obstacle = true;
            polygons.Add(placement.Polygon.Select(p => new Vector2((site.X+p.X)/1000f,(site.Z+p.Z)/1000f)).ToArray());
        }
        else
        {
            // Selectable objects need no pathing obstacle. Outline their projected
            // mesh bounds without adding collision or changing navigation.
            var vertices = meshes.SelectMany(m => m.Faces.Select(v => m.Mesh.GlobalTransform*v)).ToArray();
            if (vertices.Length == 0) throw new InvalidOperationException("Selectable object has no geometry: "+root.Name);
            float left = -vertices.Max(v => v.X),right = -vertices.Min(v => v.X),top = vertices.Min(v => v.Z),bottom = vertices.Max(v => v.Z);
            polygons.Add([new(left,top),new(right,top),new(right,bottom),new(left,bottom)]);
        }
        if (type=="BUILDING_E_ORC_BARRACKS")
        {
            // The Mound's authored collision box is tilted ~23 degrees inside an unrotated model.
            // Its selection frame follows the building axes rather than exposing that internal collision angle.
            var bounds=polygons.SelectMany(p=>p).ToArray();
            float left=bounds.Min(p=>p.X),right=bounds.Max(p=>p.X),top=bounds.Min(p=>p.Y),bottom=bounds.Max(p=>p.Y);
            polygons.Clear();polygons.Add([new(left,top),new(right,top),new(right,bottom),new(left,bottom)]);
        }
        foreach (var polygon in polygons)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i+1)%polygon.Length];
                var p = PositionOf((int)Math.Round(a.X*1000),(int)Math.Round(a.Y*1000))+Vector3.Up*.07f;
                var q = PositionOf((int)Math.Round(b.X*1000),(int)Math.Round(b.Y*1000))+Vector3.Up*.07f;
                var side = (q-p).Cross(Vector3.Up).Normalized()*.045f;
                foreach (var v in new[] {p-side,q-side,p+side,p+side,q-side,q+side}) points.Add(root.ToLocal(v));
            }
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        var ringMesh = new ArrayMesh(); ringMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        var ring = new MeshInstance3D { Mesh = ringMesh,Visible = false,CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = (_observing || _sandboxMap) && team >= 0 ? TeamColor(team).Lightened(.25f) : new Color(team < 0 ? "ded2a5" : team == PlayerTeam ? "e9c22d" : "e65045"),CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        root.AddChild(ring); _structureViews.Add(id,new(id,team,type,resource,obstacle,root,ring,meshes));
    }

    private bool StructureVisible(StructureVisual entry) => entry.Root.IsVisibleInTree()
        && (!entry.Obstacle || _world!.Navigation.IsObstacleActive(entry.Id));

    private (int Id,float Distance)? StructureAt(Vector2 mouse)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mouse),direction = _camera.ProjectRayNormal(mouse);
        var terrain = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
        float visibleDistance = terrain?.Distance+.03f ?? float.PositiveInfinity;
        (int Id,float Distance)? hit = null;
        foreach (var entry in _structureViews.Values.OrderBy(s => s.Id))
        {
            if (!StructureVisible(entry)) continue;
            foreach (var (mesh,faces) in entry.Meshes)
            {
                if (!mesh.IsVisibleInTree()) continue;
                var inverse = mesh.GlobalTransform.AffineInverse(); var from = inverse*origin; var toward = inverse.Basis*direction;
                if (!RayHitsBounds(mesh.GetAabb(),from,toward)) continue;
                for (int i = 0; i+2 < faces.Length; i += 3)
                {
                    var result = Geometry3D.RayIntersectsTriangle(from,toward,faces[i],faces[i+1],faces[i+2]);
                    if (result.VariantType == Variant.Type.Nil) continue;
                    float distance = origin.DistanceTo(mesh.GlobalTransform*result.AsVector3());
                    if (distance <= visibleDistance && (hit == null || distance < hit.Value.Distance)) hit = (entry.Id,distance);
                }
            }
        }
        return hit;
    }

    private int? StructureInBox(Rect2 rect)
    {
        // Keep a single structure inspector. When several structures overlap
        // the box, prefer the one whose ground anchor is nearest its center.
        foreach (var entry in _structureViews.Values.Where(StructureVisible)
            .Where(s => !_camera.IsPositionBehind(s.Root.GlobalPosition))
            .OrderBy(s => _camera.UnprojectPosition(s.Root.GlobalPosition).DistanceSquaredTo(rect.GetCenter())).ThenBy(s => s.Id))
        {
            foreach (var (mesh,faces) in entry.Meshes)
            {
                if (!mesh.IsVisibleInTree()) continue;
                for (int i = 0; i+2 < faces.Length; i += 3)
                {
                    var triangle = new[] {mesh.GlobalTransform*faces[i],mesh.GlobalTransform*faces[i+1],mesh.GlobalTransform*faces[i+2]};
                    if (triangle.Any(_camera.IsPositionBehind)) continue;
                    var polygon = ClipSelectionTriangle(rect,triangle.Select(_camera.UnprojectPosition).ToArray());
                    if (polygon.Length < 3) continue;
                    var point = polygon.Aggregate(Vector2.Zero,(sum,p) => sum+p)/polygon.Length;
                    // Use the same depth-aware picker as a click: terrain and
                    // nearer structures must not expose hidden geometry.
                    if (StructureAt(point)?.Id == entry.Id) return entry.Id;
                }
            }
        }
        return null;
    }

    private static Vector2[] ClipSelectionTriangle(Rect2 rect,Vector2[] triangle)
    {
        var polygon = triangle.ToList();
        for (int edge = 0; edge < 4 && polygon.Count > 0; edge++)
        {
            int axis = edge/2;
            float boundary = edge%2 == 0 ? rect.Position[axis] : rect.End[axis];
            bool Inside(Vector2 p) => edge%2 == 0 ? p[axis] >= boundary : p[axis] <= boundary;
            var clipped = new List<Vector2>(); var previous = polygon[^1];
            foreach (var point in polygon)
            {
                if (Inside(previous) != Inside(point))
                    clipped.Add(previous.Lerp(point,(boundary-previous[axis])/(point[axis]-previous[axis])));
                if (Inside(point)) clipped.Add(point);
                previous = point;
            }
            polygon = clipped;
        }
        return polygon.ToArray();
    }

    private static bool RayHitsBounds(Aabb bounds,Vector3 origin,Vector3 direction)
    {
        float enter = 0,exit = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float low = bounds.Position[axis],high = bounds.End[axis],at = origin[axis],step = direction[axis];
            if (Math.Abs(step) < .000001f) { if (at < low || at > high) return false; continue; }
            float first = (low-at)/step,last = (high-at)/step;
            enter = Math.Max(enter,Math.Min(first,last)); exit = Math.Min(exit,Math.Max(first,last));
            if (enter > exit) return false;
        }
        return true;
    }

    private void SelectStructure(int id,bool toggle = false)
    {
        _selected.Clear(); _orders.Cancel(); ClearConstructionTargeting();
        _selectedStructure = toggle && _selectedStructure == id ? null : id;
    }

    private StructureDetails? SelectedStructureDetails()
    {
        if (_selectedStructure is not int id || !_structureViews.TryGetValue(id,out var entry)) return null;
        if (!StructureVisible(entry)) { _selectedStructure = null; return null; }
        var world = _world!;
        var site = entry.Team >= 0 ? world.ConstructionSites.Values.FirstOrDefault(s => s.Obstacle == id && s.Team == entry.Team) : null;
        // An upgrade changes the building's type under the same view.
        var definition = _structureDefinitions[site?.Building ?? entry.Type];
        int max = site != null ? world.SiteMaxHealth(site) : definition.GetProperty("max_health").GetInt32();
        int progress = site == null || site.Complete ? 100 : (int)(site.ProgressMicroTicks*100/(world.SiteRules(site).DurationTicks*1_000_000L));
        string resource = entry.Resource < 0 ? "" : $"{world.Resources[entry.Resource].Kind} remaining: {(world.Resources[entry.Resource].RemainingMilli+999)/1000}";
        var place = PlaceAt(id);
        string building = site?.Building ?? entry.Type;
        var weapons = world.Defenses.TryGetValue(building,out var first) ? new[] {first}
            .Concat(world.ExtraDefenses.GetValueOrDefault(building) ?? []).Select((defense,weapon) => DefenseHudStats.From(
                _defenseLabels.TryGetValue(building,out var labels) && weapon < labels.Length ? labels[weapon] : "Attack",defense)).ToArray() : [];
        string attack = string.Join(" / ",weapons.Select(w => w.Attack));
        return new(id,entry.Team,place?.Name ?? definition.GetProperty("display_name").GetString()!,definition.GetProperty("portrait").GetString()!,site != null ? ShownHealth(site) : max,max,
            definition.GetProperty("armor").GetDouble().ToString("0.##",System.Globalization.CultureInfo.InvariantCulture),progress,place == null ? resource : PlaceSummary(place),entry.Resource,
            !definition.TryGetProperty("has_health",out var hasHealth) || hasHealth.GetBoolean(),site?.Id ?? -1,
            place == null ? "" : place.Owner == PlayerTeam && !_observing ? "Place of power · yours until an enemy stands alone in its zone"
                : $"Place of power · stand alone within {place.RadiusMm/1000} m to take it",attack,
            Burning: site is { FirePpm: > 0,Health: > 0 },Weapons:weapons);
    }

    private void PresentStructureSelection(float dt)
    {
        var details = SelectedStructureDetails(); _commands.RefreshStructure(details);
        _commands.RefreshPlacePower(_world!,_selectedStructure is int selectedPlace ? PlaceAt(selectedPlace) : null,_observing);
        foreach (var entry in _structureViews.Values)
        {
            entry.CommandBlip = Math.Max(0,entry.CommandBlip-dt);
            bool blip = entry.CommandBlip > 0, selected = details?.Id == entry.Id, hovered = _hoveredStructure == entry.Id;
            int owner = PlaceAt(entry.Id)?.Owner ?? entry.Team;
            var color = (_observing || _sandboxMap) && owner >= 0 ? TeamColor(owner).Lightened(.25f)
                : new Color(owner < 0 ? "ded2a5" : owner == PlayerTeam ? "e9c22d" : "e65045");
            if (hovered && !selected) color = new Color(TeamColor(owner),.5f);
            color = CommandRingColor(entry.CommandBlip,owner,color,selected || hovered);
            entry.Ring.Visible = StructureVisible(entry) && (blip ? color.A > .001f : selected || hovered);
            if (entry.RingColor != color)
            {
                ((StandardMaterial3D)entry.Ring.MaterialOverride).AlbedoColor = color;
                entry.RingColor = color;
            }
        }
        PresentPlaceZones(details?.Id);
    }
}
