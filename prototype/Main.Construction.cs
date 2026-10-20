using Godot;
using Reforged.Rendering;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _constructionTargeting;
    private ResourceKind? _constructionKind;
    private readonly Dictionary<ResourceKind,string> _constructionFiles = [];
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

    private void LoadSkirmishConstruction(JsonElement data)
    {
        if (!data.TryGetProperty("construction",out var construction))
            throw new InvalidOperationException("Refresh construction: python tools/skirmish_construction.py --cache converted/skirmish");
        foreach (var start in data.GetProperty("bases").EnumerateArray())
            _world!.StartBuildings.Add(start.GetProperty("team").GetInt32(),[start.GetProperty("building").GetString()!]);
        foreach (var property in construction.GetProperty("definitions").EnumerateObject())
        {
            ResourceKind kind = property.Name == "food" ? ResourceKind.Food : ResourceKind.Ore;
            var rules = property.Value;
            _world!.ConstructionRules.Add(kind,new(rules.GetProperty("name").GetString()!,rules.GetProperty("display_name").GetString()!,kind,
                rules.GetProperty("food_cost").GetInt32(),rules.GetProperty("ore_cost").GetInt32(),rules.GetProperty("duration_ticks").GetInt32(),
                rules.GetProperty("max_health").GetInt32(),rules.GetProperty("food_refund_ppm").GetInt32(),rules.GetProperty("ore_refund_ppm").GetInt32(),
                rules.GetProperty("extra_worker_ppm").GetInt32(),rules.GetProperty("initial_health_ppm").GetInt32(),
                rules.GetProperty("prerequisites").EnumerateArray().Select(p => p.GetString()!).ToArray()));
            _constructionFiles.Add(kind,data.GetProperty("building_files").GetProperty(rules.GetProperty("name").GetString()!).GetString()!);
        }
        foreach (var entry in construction.GetProperty("sites").EnumerateArray())
        {
            int id = entry.GetProperty("resource").GetInt32(),obstacle = entry.GetProperty("obstacle").GetInt32();
            var source = _world!.Resources[id];
            _world.ConstructionSites.Add(id,new(id,obstacle,SkirmishInteractionRadius(obstacle,source.X,source.Z)));
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

    private void ChooseConstruction(ResourceKind? kind)
    {
        if (!_skirmish || !SelectedWorkers().Any()) return;
        ClearConstructionTargeting(); _orders.Cancel(); _dragging = false; _hud.SelectionBox = null;
        _constructionTargeting = true; _constructionKind = kind;
        _status.Text = kind is { } k ? "Build "+_world!.ConstructionRules[k].DisplayName+": choose a resource site" : "Choose an unfinished building to cancel";
        if (kind is { } previewKind)
        {
            _constructionPreview = LoadGlb(_constructionFiles[previewKind],_teamColors[0]); AddChild(_constructionPreview);
            var data = UnitAppearance.Read(System.IO.Path.Combine(_cache,_constructionFiles[previewKind]));
            string healthy = data.GetProperty("models")[0].GetProperty("states").EnumerateArray()
                .First(s => s.GetProperty("id").GetInt32() == 0).GetProperty("choices")[0].GetProperty("clip").GetString()!;
            new UnitAppearance(_constructionPreview,data).Apply(healthy,0);
            _previewMaterial = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor = new Color(.3f,1,.3f,.45f) };
            foreach (var mesh in Descendants(_constructionPreview).OfType<MeshInstance3D>()) mesh.MaterialOverride = _previewMaterial;
        }
    }

    private void ClearConstructionTargeting()
    {
        _constructionTargeting = false; _constructionKind = null; _previewValid = false;
        if (_constructionPreview != null) { _constructionPreview.QueueFree(); _constructionPreview = null; }
        _previewMaterial = null;
    }

    private void TargetConstruction(Vector2 mouse,bool queued)
    {
        var source = ResourceAt(mouse);
        if (source == null) { _status.Text = "Choose a resource site"; return; }
        var site = _world!.ConstructionSites[source.Id];
        if (_constructionKind is { } kind)
        {
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
        if (_constructionPreview == null || _constructionKind is not { } kind) return;
        bool overUi = _commands.GetGlobalRect().HasPoint(_pointerPosition)
            || _toolsPanel.IsVisibleInTree() && _toolsPanel.GetGlobalRect().HasPoint(_pointerPosition);
        var source = ResourceAt(_pointerPosition);
        _constructionPreview.Visible = !overUi;
        _previewValid = source != null && source.Kind == kind && _world!.CanStartConstruction(0,source.Id,out _);
        if (source != null) _constructionPreview.Position = PositionOf(source.X,source.Z);
        else
        {
            Vector3 origin = _camera.ProjectRayOrigin(_pointerPosition),direction = _camera.ProjectRayNormal(_pointerPosition);
            var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
            if (hit != null) _constructionPreview.Position = new(-hit.Position.X,hit.Position.Y,hit.Position.Z);
            else _constructionPreview.Visible = false;
        }
        _previewMaterial!.AlbedoColor = _previewValid ? new Color(.3f,1,.3f,.45f) : new Color(1,.15f,.1f,.5f);
    }

    private void PresentConstruction()
    {
        foreach (var site in _world!.ConstructionSites.Values)
        {
            if (site.Team < 0)
            {
                if (_constructionViews.Remove(site.Resource,out var old)) old.Root.QueueFree();
                continue;
            }
            var source = _world.Resources[site.Resource]; var rules = _world.ConstructionRules[source.Kind];
            if (!_constructionViews.TryGetValue(site.Resource,out var view))
            {
                var model = LoadGlb(_constructionFiles[source.Kind],_teamColors[site.Team]);
                var root = new Node3D { Position = PositionOf(source.X,source.Z),Name = "ResourceBuilding_"+site.Resource };
                root.AddChild(model); AddChild(root);
                var appearanceData = UnitAppearance.Read(System.IO.Path.Combine(_cache,_constructionFiles[source.Kind]));
                var states = appearanceData.GetProperty("models")[0].GetProperty("states");
                var clips = new[] {19,17,18,0}.Select(id => states.EnumerateArray().First(s => s.GetProperty("id").GetInt32() == id)
                    .GetProperty("choices")[0].GetProperty("clip").GetString()!).ToArray();
                var label = new Label3D { Position = new(0,7,0),FontSize = 48,PixelSize = .018f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest = true,Modulate = new Color("fff3cb") };
                root.AddChild(label);
                view = new(root,new UnitAppearance(model,appearanceData),Descendants(model).OfType<AnimationPlayer>().FirstOrDefault(),clips,label);
                _constructionViews.Add(site.Resource,view);
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
