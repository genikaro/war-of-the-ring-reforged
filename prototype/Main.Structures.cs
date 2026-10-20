using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private int? _selectedStructure;
    private readonly Dictionary<string,JsonElement> _structureDefinitions = [];
    private readonly Dictionary<int,StructureVisual> _structureViews = [];
    private sealed record StructureVisual(int Id,int Team,string Type,int Resource,Node3D Root,MeshInstance3D Ring,
        (MeshInstance3D Mesh,Vector3[] Faces)[] Meshes);

    private void LoadStructureDefinitions(JsonElement data)
    {
        if (!data.GetProperty("construction").TryGetProperty("buildings",out var buildings))
            throw new InvalidOperationException("Refresh structure data: python tools/skirmish_construction.py --cache converted/skirmish");
        foreach (var item in buildings.EnumerateObject()) _structureDefinitions.Add(item.Name,item.Value.Clone());
    }

    private void RegisterStructure(int id,int team,string type,Node3D root,int resource = -1)
    {
        var meshes = Descendants(root).OfType<MeshInstance3D>().Where(m => m.Mesh != null).Select(m => (m,m.Mesh!.GetFaces())).ToArray();
        var points = new List<Vector3>();
        var layer = _obstacleLayers.EnumerateArray().First(o => o.GetProperty("id").GetInt32() == id);
        foreach (var box in layer.GetProperty("footprints").EnumerateArray())
        {
            var polygon = box.GetProperty("polygon").EnumerateArray().ToArray();
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i+1)%polygon.Length];
                var p = PositionOf((int)Math.Round(a[0].GetDouble()*1000),(int)Math.Round(a[1].GetDouble()*1000))+Vector3.Up*.07f;
                var q = PositionOf((int)Math.Round(b[0].GetDouble()*1000),(int)Math.Round(b[1].GetDouble()*1000))+Vector3.Up*.07f;
                var side = (q-p).Cross(Vector3.Up).Normalized()*.045f;
                foreach (var v in new[] {p-side,q-side,p+side,p+side,q-side,q+side}) points.Add(root.ToLocal(v));
            }
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        var ringMesh = new ArrayMesh(); ringMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        var ring = new MeshInstance3D { Mesh = ringMesh,Visible = false,CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(team == 0 ? "e9c22d" : "e65045"),CullMode = BaseMaterial3D.CullModeEnum.Disabled,ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        root.AddChild(ring); _structureViews.Add(id,new(id,team,type,resource,root,ring,meshes));
    }

    private (int Id,float Distance)? StructureAt(Vector2 mouse)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mouse),direction = _camera.ProjectRayNormal(mouse);
        var terrain = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
        float visibleDistance = terrain?.Distance+.03f ?? float.PositiveInfinity;
        (int Id,float Distance)? hit = null;
        foreach (var entry in _structureViews.Values.OrderBy(s => s.Id))
        {
            if (!_world!.Navigation.IsObstacleActive(entry.Id)) continue;
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
        if (!_world!.Navigation.IsObstacleActive(id)) { _selectedStructure = null; return null; }
        var definition = _structureDefinitions[entry.Type]; int max = definition.GetProperty("max_health").GetInt32();
        var site = entry.Resource >= 0 ? _world.ConstructionSites[entry.Resource] : null;
        int progress = site == null || site.Complete ? 100 : (int)(site.ProgressMicroTicks*100/(_world.ConstructionRules[_world.Resources[entry.Resource].Kind].DurationTicks*1_000_000L));
        string resource = entry.Resource < 0 ? "" : $"{_world.Resources[entry.Resource].Kind} remaining: {(_world.Resources[entry.Resource].RemainingMilli+999)/1000}";
        return new(id,entry.Team,definition.GetProperty("display_name").GetString()!,definition.GetProperty("portrait").GetString()!,site?.Health ?? max,max,
            definition.GetProperty("armor").GetDouble().ToString("0.##",System.Globalization.CultureInfo.InvariantCulture),progress,resource,entry.Resource);
    }

    private void PresentStructureSelection()
    {
        var details = SelectedStructureDetails(); _commands.RefreshStructure(details);
        foreach (var entry in _structureViews.Values) entry.Ring.Visible = details?.Id == entry.Id;
    }
}
