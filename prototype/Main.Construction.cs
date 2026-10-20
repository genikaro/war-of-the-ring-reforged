using Godot;
using Reforged.Rendering;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _constructionTargeting;
    /// <summary>Building chosen for placement; null while targeting a foundation to cancel.</summary>
    private string? _constructionBuilding;
    private readonly Dictionary<string,string> _constructionFiles = [];
    private (string Building,string Portrait)[] _constructionChoices = [];
    private readonly Dictionary<int,BuildingVisual> _constructionViews = [];
    private Node3D? _constructionPreview;
    private StandardMaterial3D? _previewMaterial;
    private bool _previewValid;
    private sealed class BuildingVisual(Node3D root,UnitAppearance appearance,AnimationPlayer? player,string[] clips,Label3D label)
    {
        public Node3D Root = root;
        public UnitAppearance Appearance = appearance;
        public AnimationPlayer? Player = player;
        public string[] Clips = clips;
        public Label3D Label = label;
        public int Stage = -1;
    }

    private IEnumerable<int> SelectedWorkers() => _world!.Units.Where(u => u.Team == 0 && u.Health > 0
        && _selected.Contains(u.Id) && _world.Workers.ContainsKey(u.Definition.Name)).Select(u => u.Id);

    private ResourceKind? ResourceBuildingKind(string building) => _world!.ConstructionRules.Values.FirstOrDefault(r => r.Name == building)?.Kind;

    private void LoadSkirmishConstruction(JsonElement data)
    {
        if (!data.TryGetProperty("construction",out var construction) || !construction.TryGetProperty("placeable",out var placeable))
            throw new InvalidOperationException("Refresh construction: python tools/skirmish_construction.py --cache converted/skirmish");
        var world = _world!;
        foreach (var start in data.GetProperty("bases").EnumerateArray())
            world.StartBuildings.Add(start.GetProperty("team").GetInt32(),[start.GetProperty("building").GetString()!]);
        ConstructionDefinition Rules(JsonElement rules,ResourceKind? kind) => new(rules.GetProperty("name").GetString()!,rules.GetProperty("display_name").GetString()!,kind,
            rules.GetProperty("food_cost").GetInt32(),rules.GetProperty("ore_cost").GetInt32(),rules.GetProperty("duration_ticks").GetInt32(),
            rules.GetProperty("max_health").GetInt32(),rules.GetProperty("food_refund_ppm").GetInt32(),rules.GetProperty("ore_refund_ppm").GetInt32(),
            rules.GetProperty("extra_worker_ppm").GetInt32(),rules.GetProperty("initial_health_ppm").GetInt32(),
            rules.GetProperty("prerequisites").EnumerateArray().Select(p => p.GetString()!).ToArray());
        foreach (var property in construction.GetProperty("definitions").EnumerateObject())
        {
            ResourceKind kind = property.Name == "food" ? ResourceKind.Food : ResourceKind.Ore;
            world.ConstructionRules.Add(kind,Rules(property.Value,kind));
        }
        foreach (var file in data.GetProperty("building_files").EnumerateObject()) _constructionFiles.Add(file.Name,file.Value.GetString()!);
        foreach (var entry in construction.GetProperty("sites").EnumerateArray())
        {
            int id = entry.GetProperty("resource").GetInt32(),obstacle = entry.GetProperty("obstacle").GetInt32();
            var source = world.Resources[id];
            world.ConstructionSites.Add(id,new(id,obstacle,SkirmishInteractionRadius(obstacle,source.X,source.Z)));
        }
        foreach (var item in placeable.EnumerateObject())
        {
            var footprint = item.Value.GetProperty("footprint");
            var polygon = footprint.GetProperty("polygon_mm").EnumerateArray().Select(p => new FootprintPoint(p[0].GetInt32(),p[1].GetInt32())).ToArray();
            world.BuildingRules.Add(item.Name,Rules(item.Value,null));
            world.Placements.Add(item.Name,new(polygon,footprint.GetProperty("min_height_mm").GetInt32(),footprint.GetProperty("max_height_mm").GetInt32(),
                InteractionRadius(polygon.Select(p => Math.Sqrt((double)p.X*p.X+(double)p.Z*p.Z)).Max()),
                item.Value.GetProperty("must_be_near").GetBoolean(),item.Value.GetProperty("drop_off").GetBoolean()));
        }
        foreach (var radius in construction.GetProperty("placement_radius_mm").EnumerateObject()) world.PlacementRadiusMm.Add(radius.Name,radius.Value.GetInt32());
        var order = construction.GetProperty("order").EnumerateArray().Select(n => n.GetString()!).ToArray();
        world.PlaceableBuildings.AddRange(order.Where(world.BuildingRules.ContainsKey));
        _constructionChoices = order.Select(n => (n,_structureDefinitions[n].GetProperty("portrait").GetString()!)).ToArray();
        foreach (var start in data.GetProperty("bases").EnumerateArray())
        {
            var at = start.GetProperty("position");
            world.StartStructures.Add(new(start.GetProperty("team").GetInt32(),start.GetProperty("building").GetString()!,
                (int)Math.Round(at[0].GetDouble()*1000),(int)Math.Round(at[1].GetDouble()*1000)));
        }
    }

    private GatherSource? ResourceAt(Vector2 mouse)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mouse),direction = _camera.ProjectRayNormal(mouse);
        var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
        if (hit == null) return null;
        int x = (int)Math.Round(hit.Position.X*1000),z = (int)Math.Round(hit.Position.Z*1000);
        return _world!.Resources.Values.Where(r => SimWorld.DistanceSquared(x,z,r.X,r.Z) <= 4000L*4000)
            .OrderBy(r => SimWorld.DistanceSquared(x,z,r.X,r.Z)).ThenBy(r => r.Id).FirstOrDefault();
    }

    /// <summary>The ground cell under the pointer, for open-ground placement.</summary>
    private int PlacementCellAt(Vector2 mouse)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mouse),direction = _camera.ProjectRayNormal(mouse);
        var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z),groundOnly: true);
        return hit == null ? -1 : _world!.Navigation.Cell((int)Math.Round(hit.Position.X*1000),(int)Math.Round(hit.Position.Z*1000));
    }

    private void ChooseConstruction(string? building)
    {
        if (building == null && SelectedStructureDetails() is { Team: 0,UnderConstruction: true,SiteId: >= 0 } selected)
        {
            _world!.Enqueue(0,[],OrderKind.CancelConstruction,target: selected.SiteId);
            _status.Text = "Construction cancellation queued"; return;
        }
        if (!_skirmish || !SelectedWorkers().Any()) return;
        ClearConstructionTargeting(); _orders.Cancel(); _dragging = false; _hud.SelectionBox = null;
        _constructionTargeting = true; _constructionBuilding = building;
        if (building == null) { _status.Text = "Choose an unfinished building to cancel"; return; }
        var rules = _world!.BuildingRules.GetValueOrDefault(building) ?? _world.ConstructionRules[ResourceBuildingKind(building)!.Value];
        _status.Text = "Build "+rules.DisplayName+(ResourceBuildingKind(building) != null ? ": choose a resource site" : ": choose open ground near your buildings");
        _constructionPreview = LoadGlb(_constructionFiles[building],_teamColors[0]); AddChild(_constructionPreview);
        var data = UnitAppearance.Read(System.IO.Path.Combine(_cache,_constructionFiles[building]));
        new UnitAppearance(_constructionPreview,data).Apply(BuildingStateClips(data)[3],0);
        _previewMaterial = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor = new Color(.3f,1,.3f,.45f) };
        foreach (var mesh in Descendants(_constructionPreview).OfType<MeshInstance3D>()) mesh.MaterialOverride = _previewMaterial;
    }

    /// <summary>Source construction thirds (states 19/17/18) followed by the healthy state 0 of a restored building model.</summary>
    private static string[] BuildingStateClips(JsonElement data)
    {
        var states = data.GetProperty("models")[0].GetProperty("states");
        return new[] {19,17,18,0}.Select(id => states.EnumerateArray().First(s => s.GetProperty("id").GetInt32() == id)
            .GetProperty("choices")[0].GetProperty("clip").GetString()!).ToArray();
    }

    private void ClearConstructionTargeting()
    {
        _constructionTargeting = false; _constructionBuilding = null; _previewValid = false;
        if (_constructionPreview != null) { _constructionPreview.QueueFree(); _constructionPreview = null; }
        _previewMaterial = null;
    }

    private void TargetConstruction(Vector2 mouse,bool queued)
    {
        if (_constructionBuilding is { } building && ResourceBuildingKind(building) == null)
        {
            int cell = PlacementCellAt(mouse);
            if (!_world!.CanPlaceBuilding(0,building,cell,out string blocked)) { _status.Text = blocked; return; }
            _world.Enqueue(0,SelectedWorkers(),OrderKind.PlaceBuilding,cell,_world.PlaceableBuildings.IndexOf(building),queued);
            _status.Text = "Build "+_world.BuildingRules[building].DisplayName+(_paused ? " queued (paused)" : " order issued");
            ClearConstructionTargeting(); return;
        }
        var source = ResourceAt(mouse);
        if (source == null) { _status.Text = "Choose a resource site"; return; }
        var site = _world!.ConstructionSites[source.Id];
        if (_constructionBuilding is { } resourceBuilding)
        {
            var kind = ResourceBuildingKind(resourceBuilding)!.Value;
            if (source.Kind != kind) { _status.Text = kind == ResourceKind.Food ? "A Mill requires a food well" : "A Foundry requires an ore pile"; return; }
            if (!_world.CanStartConstruction(0,source.Id,out string reason)) { _status.Text = reason; return; }
            _world.Enqueue(0,SelectedWorkers(),OrderKind.Build,target: source.Id,queued: queued);
            _status.Text = "Build "+_world.ConstructionRules[kind].DisplayName+(_paused ? " queued (paused)" : " order issued");
        }
        else
        {
            if (site.Team != 0 || site.Complete) { _status.Text = "Choose your unfinished building"; return; }
            _world.Enqueue(0,[],OrderKind.CancelConstruction,target: source.Id); _status.Text = "Construction cancellation queued";
        }
        ClearConstructionTargeting();
    }

    private void UpdateConstructionPreview()
    {
        if (_constructionPreview == null || _constructionBuilding is not { } building) return;
        bool overUi = _commands.GetGlobalRect().HasPoint(_pointerPosition)
            || _toolsPanel.IsVisibleInTree() && _toolsPanel.GetGlobalRect().HasPoint(_pointerPosition);
        _constructionPreview.Visible = !overUi;
        if (ResourceBuildingKind(building) is not { } kind)
        {
            int cell = PlacementCellAt(_pointerPosition);
            _previewValid = _world!.CanPlaceBuilding(0,building,cell,out _);
            if (cell >= 0) { var (x,z) = _world.Navigation.Center(cell); _constructionPreview.Position = PositionOf(x,z); }
            else _constructionPreview.Visible = false;
        }
        else
        {
            var source = ResourceAt(_pointerPosition);
            _previewValid = source != null && source.Kind == kind && _world!.CanStartConstruction(0,source.Id,out _);
            if (source != null) _constructionPreview.Position = PositionOf(source.X,source.Z);
            else
            {
                Vector3 origin = _camera.ProjectRayOrigin(_pointerPosition),direction = _camera.ProjectRayNormal(_pointerPosition);
                var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
                if (hit != null) _constructionPreview.Position = new(-hit.Position.X,hit.Position.Y,hit.Position.Z);
                else _constructionPreview.Visible = false;
            }
        }
        _previewMaterial!.AlbedoColor = _previewValid ? new Color(.3f,1,.3f,.45f) : new Color(1,.15f,.1f,.5f);
    }

    private void PresentConstruction()
    {
        foreach (int id in _constructionViews.Keys.Where(id => !_world!.ConstructionSites.TryGetValue(id,out var s) || s.Team < 0).ToArray())
        {
            _constructionViews.Remove(id,out var old); old!.Root.QueueFree();
            int obstacle = _structureViews.Values.First(v => v.Root == old.Root).Id;
            _structureViews.Remove(obstacle); if (_selectedStructure == obstacle) _selectedStructure = null;
        }
        foreach (var site in _world!.ConstructionSites.Values.Where(s => s.Team >= 0))
        {
            var rules = _world.SiteRules(site); var (x,z) = _world.SitePosition(site);
            if (!_constructionViews.TryGetValue(site.Id,out var view))
            {
                var model = LoadGlb(_constructionFiles[rules.Name],_teamColors[site.Team]);
                var root = new Node3D { Position = PositionOf(x,z),Name = "Building_"+site.Id };
                root.AddChild(model); AddChild(root);
                var appearanceData = UnitAppearance.Read(System.IO.Path.Combine(_cache,_constructionFiles[rules.Name]));
                var clips = BuildingStateClips(appearanceData);
                var label = new Label3D { Position = new(0,7,0),FontSize = 48,PixelSize = .018f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest = true,Modulate = new Color("fff3cb") };
                root.AddChild(label);
                view = new(root,new UnitAppearance(model,appearanceData),Descendants(model).OfType<AnimationPlayer>().FirstOrDefault(),clips,label);
                _constructionViews.Add(site.Id,view);
                RegisterStructure(site.Obstacle,site.Team,rules.Name,root,site.Resource);
            }
            int stage = site.Complete ? 3 : (int)Math.Min(2,site.ProgressMicroTicks*3/(rules.DurationTicks*1_000_000L));
            if (stage != view.Stage)
            {
                string clip = view.Clips[stage]; view.Appearance.Apply(clip,0);
                string? animation = view.Player?.GetAnimationList().FirstOrDefault(a => a == clip || a.EndsWith('/'+clip));
                if (animation != null) { view.Player!.Play(animation); view.Player.Advance(0); }
                view.Stage = stage;
            }
            if (view.Player != null) view.Player.SpeedScale = _paused ? 0 : 1;
            view.Label.Text = site.Complete ? "" : $"{rules.DisplayName} {site.ProgressMicroTicks*100/(rules.DurationTicks*1_000_000L)}%\n{site.Health}/{rules.MaxHealth}";
        }
    }
}
