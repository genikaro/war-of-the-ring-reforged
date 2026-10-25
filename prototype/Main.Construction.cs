using Godot;
using Reforged.Rendering;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _constructionTargeting;
    private string? _constructionBuilding;
    /// <summary>Worker build page open, for the selection it was opened with.</summary>
    private bool _buildMenu;
    /// <summary>The open build page is Advanced: the buildings a Citadel upgrade opens (build-58).</summary>
    private bool _advancedBuildPage;
    private string _buildMenuSelection = "";
    private readonly Dictionary<string,string> _constructionFiles = [];
    private (string Building,string Portrait)[] _constructionChoices = [];
    private readonly Dictionary<int,BuildingVisual> _constructionViews = [];
    private Node3D? _constructionPreview;
    private StandardMaterial3D? _previewMaterial,_ghostMaterial;
    private readonly Dictionary<(string Building,int X,int Z),Node3D> _foundationGhosts = [];
    private bool _previewValid;
    private sealed class BuildingVisual(Node3D root,Node3D model,UnitAppearance appearance,AnimationPlayer? player,string[] clips,HealthBar bar,string building)
    {
        public string Building = building;
        public Node3D Model = model;
        public Node3D Root = root;
        public UnitAppearance Appearance = appearance;
        public AnimationPlayer? Player = player;
        public string[] Clips = clips;
        public HealthBar Bar = bar;
        public int Stage = -1;
    }

    /// <summary>Owner-chosen turns of building models about their origin, in degrees counterclockwise seen from above
    /// (build-75). Only the model turns: footprint, picking ring and selection border stay as placed. The Mill's blades
    /// sit 18° west of north in the source model; -153° points them south-east, toward the camera. Tower arrow origins
    /// are unturned model offsets, so towers stay unturned.</summary>
    private static readonly Dictionary<string,float> ModelYawDegrees = new() { ["BUILDING_G_FOOD_STRUCTURE"] = -153 };

    private static Node3D TurnModel(Node3D model,string building)
    {
        if (ModelYawDegrees.TryGetValue(building,out float yaw)) model.RotationDegrees = new(0,yaw,0);
        return model;
    }

    private IEnumerable<int> SelectedWorkers() => _world!.Units.Where(u => u.Team == PlayerTeam && u.Health > 0
        && _selected.Contains(u.Id) && _world.Workers.ContainsKey(u.Definition.Name)).Select(u => u.Id);

    private ResourceKind? ResourceBuildingKind(string building) => _world!.ConstructionRules.Values.FirstOrDefault(r => r.Name == building)?.Kind;

    private void LoadSkirmishConstruction(JsonElement data)
    {
        // build-25 adds the Watchtower's source defenses, build-44 the upgrades.
        if (!data.TryGetProperty("construction",out var construction) || !construction.TryGetProperty("placeable",out var placeable)
            || !construction.TryGetProperty("defenses",out var defenses) || !construction.TryGetProperty("upgrades",out var upgrades))
            throw new InvalidOperationException("Refresh construction: python tools/skirmish_construction.py --cache converted/skirmish");
        var world = _world!;
        foreach (var start in OpenBases(data))
            world.StartBuildings.Add(start.GetProperty("team").GetInt32(),[start.GetProperty("building").GetString()!]);
        int Armor(string name) => _structureDefinitions.TryGetValue(name,out var definition)
            ? (int)Math.Round(definition.GetProperty("armor").GetDouble()*1000) : 0;
        ConstructionDefinition Rules(JsonElement rules,ResourceKind? kind) => new(rules.GetProperty("name").GetString()!,rules.GetProperty("display_name").GetString()!,kind,
            rules.GetProperty("food_cost").GetInt32(),rules.GetProperty("ore_cost").GetInt32(),rules.GetProperty("duration_ticks").GetInt32(),
            rules.GetProperty("max_health").GetInt32(),rules.GetProperty("food_refund_ppm").GetInt32(),rules.GetProperty("ore_refund_ppm").GetInt32(),
            rules.GetProperty("extra_worker_ppm").GetInt32(),rules.GetProperty("initial_health_ppm").GetInt32(),
            rules.GetProperty("prerequisites").EnumerateArray().Select(p => p.GetString()!).ToArray(),
            Armor(rules.GetProperty("name").GetString()!));
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
        foreach (var item in upgrades.EnumerateObject())
        {
            string promoted = item.Value.GetProperty("name").GetString()!;
            world.BuildingRules.Add(promoted,Rules(item.Value,null));
            world.Upgrades.Add(item.Name,promoted);
            // ponytail: the upgraded building keeps the footprint it was placed with (the source boxes match).
            world.Placements.Add(promoted,world.Placements[item.Name]);
        }
        foreach (var item in defenses.EnumerateObject())
        {
            // Build-58 lists every weapon of a building (the Ballista Tower has two); older packs hold one object.
            var weapons = item.Value.ValueKind == JsonValueKind.Array ? item.Value.EnumerateArray().ToArray() : [item.Value];
            var loaded = weapons.Select((weapon,index) =>
            {
                var origin = weapon.GetProperty("origin_mm");
                var defense = new DefenseDefinition(weapon.GetProperty("dps_milli").GetInt32(),weapon.GetProperty("minimum_range_mm").GetInt32(),
                    weapon.GetProperty("range_mm").GetInt32(),weapon.GetProperty("rates_milli").EnumerateArray().Select(r => r.GetInt32()).ToArray(),
                    origin[0].GetInt32(),origin[1].GetInt32(),origin[2].GetInt32());
                defense.Validate();
                LoadProjectileStyle(index == 0 ? item.Name : item.Name+"#"+index,weapon.GetProperty("projectile"));
                return defense;
            }).ToArray();
            world.Defenses.Add(item.Name,loaded[0]);
            if (loaded.Length > 1) world.ExtraDefenses.Add(item.Name,loaded[1..]);
            _defenseLabels[item.Name] = weapons.Select(w => w.TryGetProperty("weapon",out var key) && key.GetString()!.EndsWith("_BOLT") ? "Ballista" : "Arrows").ToArray();
        }
        foreach (var radius in construction.GetProperty("placement_radius_mm").EnumerateObject()) world.PlacementRadiusMm.Add(radius.Name,radius.Value.GetInt32());
        var order = construction.GetProperty("order").EnumerateArray().Select(n => n.GetString()!).ToArray();
        world.PlaceableBuildings.AddRange(order.Where(world.BuildingRules.ContainsKey));
        _constructionChoices = order.Select(n => (n,_structureDefinitions[n].GetProperty("portrait").GetString()!)).ToArray();
        foreach (var start in OpenBases(data))
        {
            var at = start.GetProperty("position");
            world.StartStructures.Add(new(start.GetProperty("team").GetInt32(),start.GetProperty("building").GetString()!,
                (int)Math.Round(at[0].GetDouble()*1000),(int)Math.Round(at[1].GetDouble()*1000)));
        }
    }

    /// <summary>A strip across the base of the foundation, from its footprint's mean horizontal span.</summary>
    private (float Width,float Height) SiteBarSize(ConstructionSite site)
    {
        float span;
        if (site.Building != null && _world!.Placements.TryGetValue(site.Building,out var placement))
        {
            var polygon = placement.Polygon;
            span = (polygon.Max(p => p.X)-polygon.Min(p => p.X)+polygon.Max(p => p.Z)-polygon.Min(p => p.Z))/2000f;
        }
        else
        {
            var points = _obstacleLayers.EnumerateArray().First(o => o.GetProperty("id").GetInt32() == site.Obstacle)
                .GetProperty("footprints").EnumerateArray().SelectMany(b => b.GetProperty("polygon").EnumerateArray())
                .Select(p => (X: p[0].GetSingle(),Z: p[1].GetSingle())).ToArray();
            span = (points.Max(p => p.X)-points.Min(p => p.X)+points.Max(p => p.Z)-points.Min(p => p.Z))/2;
        }
        float width = Math.Clamp(span*.8f,1.5f,12);
        return (width,Math.Clamp(width*.05f,.18f,.34f));
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

    /// <summary>The buildings of the open build page, in palette order: Build holds Tier 1, Advanced every building that
    /// asks for an upgraded Citadel.</summary>
    private (string Building,string Portrait)[] PageChoices() => _constructionChoices.Where(c => AdvancedBuilding(c.Building) == _advancedBuildPage).ToArray();

    private bool AdvancedBuilding(string building) => _world!.BuildingRules.TryGetValue(building,out var rules) && rules.Prerequisites.Any(_world.Upgrades.ContainsValue);

    private void OpenBuildMenu(bool open) => OpenBuildMenu(open,advanced: false);

    private void OpenBuildMenu(bool open,bool advanced)
    {
        ClearConstructionTargeting(); _orders.Cancel();
        _buildMenu = open && _skirmish && SelectedWorkers().Any(); _advancedBuildPage = advanced;
        _buildMenuSelection = string.Join(',',_selected.Order());
    }

    /// <summary>Close the build page when the selection it was opened for changes.</summary>
    private void SyncBuildMenu()
    {
        if (_buildMenu && (!SelectedWorkers().Any() || _buildMenuSelection != string.Join(',',_selected.Order()))) { _buildMenu = false; ClearConstructionTargeting(); }
        _commands.BuildMenuOpen = _buildMenu;
    }

    /// <summary>A building to place from the build page, or null to cancel the selected foundation.</summary>
    private void ChooseConstruction(string? building)
    {
        if (building == null)
        {
            if (SelectedStructureDetails() is { UnderConstruction: true,SiteId: >= 0 } selected && selected.Team == PlayerTeam)
            { _world!.Enqueue(PlayerTeam,[],OrderKind.CancelConstruction,target: selected.SiteId); _status.Text = "Construction cancellation queued"; }
            return;
        }
        if (!_skirmish || !SelectedWorkers().Any()) return;
        ClearConstructionTargeting(); _orders.Cancel(); _dragging = false; _hud.SelectionBox = null;
        _constructionTargeting = true; _constructionBuilding = building;
        var rules = _world!.BuildingRules.GetValueOrDefault(building) ?? _world.ConstructionRules[ResourceBuildingKind(building)!.Value];
        _status.Text = "Build "+rules.DisplayName+(ResourceBuildingKind(building) != null ? ": choose a resource site" : ": choose open ground near your buildings");
        _previewMaterial = GhostMaterial(new Color(.3f,1,.3f,.45f));
        _constructionPreview = BuildingGhost(building,_previewMaterial);
    }

    private static StandardMaterial3D GhostMaterial(Color color) => new() { Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor = color };

    /// <summary>The finished building drawn in one translucent colour.</summary>
    private Node3D BuildingGhost(string building,StandardMaterial3D material)
    {
        var ghost = TurnModel(LoadGlb(_constructionFiles[building],TeamColor(Math.Max(0,PlayerTeam))),building); AddChild(ghost);
        var data = UnitAppearance.Read(CachePath(_constructionFiles[building]));
        new UnitAppearance(ghost,data).Apply(BuildingStateClips(data)[3],0);
        foreach (var mesh in Descendants(ghost).OfType<MeshInstance3D>()) mesh.MaterialOverride = material;
        return ghost;
    }

    /// <summary>Translucent buildings where the player's workers are headed to lay a foundation, queued placements
    /// included, so a building ordered far away shows where it will stand until the foundation replaces it.</summary>
    private void PresentFoundationGhosts()
    {
        var world = _world!;
        var wanted = new HashSet<(string Building,int X,int Z)>();
        foreach (var unit in world.Units.Where(u => u.Team == PlayerTeam && u.Health > 0))
            foreach (var (kind,cell,target) in unit.OrderQueue.Select(o => (o.Kind,o.Cell,o.Target)).Prepend((unit.Order,unit.PlacementCell,unit.OrderTarget)))
            {
                if (kind == OrderKind.PlaceBuilding && cell >= 0 && target >= 0 && target < world.PlaceableBuildings.Count)
                {
                    var (x,z) = world.Navigation.Center(cell);
                    if (!world.ConstructionSites.Values.Any(s => s.Team == PlayerTeam && s.Building != null && s.X == x && s.Z == z))
                        wanted.Add((world.PlaceableBuildings[target],x,z));
                }
                else if (kind == OrderKind.Build && world.ConstructionSites.TryGetValue(target,out var site) && site.Team < 0)
                { var (x,z) = world.SitePosition(site); wanted.Add((world.SiteRules(site).Name,x,z)); }
            }
        foreach (var key in _foundationGhosts.Keys.Where(k => !wanted.Contains(k)).ToArray()) { _foundationGhosts.Remove(key,out var old); old!.QueueFree(); }
        _ghostMaterial ??= GhostMaterial(new Color(.6f,.85f,1,.35f));
        foreach (var key in wanted.Where(k => !_foundationGhosts.ContainsKey(k)))
        {
            var ghost = BuildingGhost(key.Building,_ghostMaterial); ghost.Position = PositionOf(key.X,key.Z);
            _foundationGhosts.Add(key,ghost);
        }
    }

    /// <summary>Source construction thirds (states 19/17/18) followed by the healthy state 0 of a restored building model.
    /// A building only ever reached by upgrade may lack a construction state (Citadel 2 has no 18); it shows the healthy one.</summary>
    private static string[] BuildingStateClips(JsonElement data)
    {
        var states = data.GetProperty("models")[0].GetProperty("states").EnumerateArray().ToArray();
        string Clip(int id) => (states.FirstOrDefault(s => s.GetProperty("id").GetInt32() == id) is { ValueKind: JsonValueKind.Object } state ? state
            : states.First(s => s.GetProperty("id").GetInt32() == 0)).GetProperty("choices")[0].GetProperty("clip").GetString()!;
        return new[] {19,17,18,0}.Select(Clip).ToArray();
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
            if (!_world!.CanPlaceBuilding(PlayerTeam,building,cell,out string blocked,ignoreUnits: true)) { Notify(blocked); return; }
            _world.Enqueue(PlayerTeam,SelectedWorkers(),OrderKind.PlaceBuilding,cell,_world.PlaceableBuildings.IndexOf(building),queued);
            _status.Text = "Build "+_world.BuildingRules[building].DisplayName+(_paused ? " queued (paused)" : " order issued");
            ClearConstructionTargeting(); _buildMenu = false; return;
        }
        var source = ResourceAt(mouse);
        if (source == null || _constructionBuilding is not { } resourceBuilding) { Notify("Choose a resource site"); return; }
        var kind = ResourceBuildingKind(resourceBuilding)!.Value;
        if (source.Kind != kind) { Notify(kind == ResourceKind.Food ? "A Mill requires a food well" : "A Foundry requires an ore pile"); return; }
        if (!_world!.CanStartConstruction(PlayerTeam,source.Id,out string reason) && reason != SimWorld.UnitBlocksFoundation) { Notify(reason); return; }
        _world.Enqueue(PlayerTeam,SelectedWorkers(),OrderKind.Build,target: source.Id,queued: queued);
        _status.Text = "Build "+_world.ConstructionRules[kind].DisplayName+(_paused ? " queued (paused)" : " order issued");
        ClearConstructionTargeting(); _buildMenu = false;
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
            _previewValid = _world!.CanPlaceBuilding(PlayerTeam,building,cell,out _,ignoreUnits: true);
            if (cell >= 0) { var (x,z) = _world.Navigation.Center(cell); _constructionPreview.Position = PositionOf(x,z); }
            else _constructionPreview.Visible = false;
        }
        else
        {
            var source = ResourceAt(_pointerPosition);
            _previewValid = source != null && source.Kind == kind
                && (_world!.CanStartConstruction(PlayerTeam,source.Id,out string reason) || reason == SimWorld.UnitBlocksFoundation);
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

    /// <summary>An upgraded building takes its new model; picking follows the new geometry.</summary>
    private void SwapBuildingModel(ConstructionSite site,BuildingVisual view,string building)
    {
        view.Model.QueueFree();
        var model = TurnModel(LoadGlb(_constructionFiles[building],_teamColors[site.Team]),building); view.Root.AddChild(model);
        var data = UnitAppearance.Read(CachePath(_constructionFiles[building]));
        view.Model = model; view.Appearance = new UnitAppearance(model,data); view.Clips = BuildingStateClips(data);
        view.Player = Descendants(model).OfType<AnimationPlayer>().FirstOrDefault();
        view.Building = building; view.Stage = -1;
        var entry = _structureViews[site.Obstacle];
        _structureViews[site.Obstacle] = entry with { Type = building,
            Meshes = Descendants(model).OfType<MeshInstance3D>().Where(m => m.Mesh != null).Select(m => (Mesh: m,Faces: m.Mesh!.GetFaces())).ToArray() };
    }

    private void PresentConstruction()
    {
        foreach (int id in _constructionViews.Keys.Where(id => !_world!.ConstructionSites.TryGetValue(id,out var s) || s.Team < 0).ToArray())
        {
            _constructionViews.Remove(id,out var old);
            int obstacle = _structureViews.Values.First(v => v.Root == old!.Root).Id;
            _structureViews.Remove(obstacle); if (_selectedStructure == obstacle) _selectedStructure = null;
            _obstacleViews.Remove(obstacle); _buildingViews.Remove(old.Root);
            old.Root.QueueFree();
        }
        foreach (var site in _world!.ConstructionSites.Values.Where(s => s.Team >= 0))
        {
            var rules = _world.SiteRules(site); var (x,z) = _world.SitePosition(site);
            if (!_constructionViews.TryGetValue(site.Id,out var view))
            {
                var model = TurnModel(LoadGlb(_constructionFiles[rules.Name],_teamColors[site.Team]),rules.Name);
                var root = new Node3D { Position = PositionOf(x,z),Name = "Building_"+site.Id };
                root.AddChild(model); AddChild(root);
                var appearanceData = UnitAppearance.Read(CachePath(_constructionFiles[rules.Name]));
                var clips = BuildingStateClips(appearanceData);
                var (barWidth,barHeight) = SiteBarSize(site);
                var bar = new HealthBar(barWidth,barHeight) { Position = new(0,.14f,0) };
                view = new(root,model,new UnitAppearance(model,appearanceData),Descendants(model).OfType<AnimationPlayer>().FirstOrDefault(),clips,bar,rules.Name);
                _constructionViews.Add(site.Id,view);
                RegisterStructure(site.Obstacle,site.Team,rules.Name,root,site.Resource);
                // Added after registration so the strip is not picked as the structure's own geometry.
                root.AddChild(bar);
            }
            if (view.Building != rules.Name) SwapBuildingModel(site,view,rules.Name);
            int stage = site.Complete ? 3 : (int)Math.Min(2,site.ProgressMicroTicks*3/(rules.DurationTicks*1_000_000L));
            if (stage != view.Stage)
            {
                string clip = view.Clips[stage]; view.Appearance.Apply(clip,0);
                string? animation = view.Player?.GetAnimationList().FirstOrDefault(a => a == clip || a.EndsWith('/'+clip));
                if (animation != null) { view.Player!.Play(animation); view.Player.Advance(0); }
                view.Stage = stage;
            }
            if (view.Player != null) view.Player.SpeedScale = _paused ? 0 : 1;
            // Construction health climbs with the work done, so the health bar doubles as the progress bar.
            view.Bar.Refresh(site.Health,_world!.SiteMaxHealth(site));
        }
    }
}
