using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Bilbo's Bluffs skirmish start: start buildings, resource strip and spawn placement.</summary>
public partial class Main
{
    private bool _skirmish;
    private const int SkirmishObstacleBase = 10000;
    private readonly List<Node3D> _buildingViews = [];
    private int _skirmishResourceCount;
    private (int Population,int Capacity,int Food,int Ore) _startState;

    /// <summary>Workers start on connected ground; a body-clearance miss moves the spawn to the nearest free standing cell.</summary>
    private int SkirmishSpawnCell(UnitDefinition definition,int cell)
    {
        var body = _world!.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
        var taken = _world.Units.Where(u => u.Health > 0).Select(u => u.Node).ToHashSet();
        int anchor = _world.Units.FirstOrDefault()?.Node ?? cell;
        int resolved = body.CanStand(cell) && !taken.Contains(cell) && body.IsConnected(cell,anchor)
            ? cell : body.Nearest(cell,taken,connectedTo: anchor);
        if (resolved < 0) throw new InvalidOperationException("No free ground for a start worker");
        return resolved;
    }

    private void LoadSkirmishScene(JsonElement manifest)
    {
        var data = manifest.GetProperty("skirmish");
        LoadStructureDefinitions(data);
        var files = data.GetProperty("building_files");
        foreach (var start in data.GetProperty("bases").EnumerateArray())
        {
            int team = start.GetProperty("team").GetInt32();
            var at = start.GetProperty("position");
            var root = new Node3D { Name = "Building_" + _buildingViews.Count,
                // Source metres are Z-up; the map export uses (-x, z, y) for placements.
                Position = new(-at[0].GetSingle(),at[2].GetSingle(),at[1].GetSingle()) };
            root.AddChild(LoadGlb(files.GetProperty(start.GetProperty("building").GetString()!).GetString()!,_teamColors[team]));
            AddChild(root); _buildingViews.Add(root);
            RegisterStructure(start.GetProperty("footprint_object").GetInt32(),team,start.GetProperty("building").GetString()!,root);
            _obstacleViews[start.GetProperty("footprint_object").GetInt32()] = root;
        }
        _skirmishResourceCount = data.GetProperty("resources").GetArrayLength();
        var state = data.GetProperty("start_state");
        _startState = (state.GetProperty("population").GetInt32(),state.GetProperty("population_cap").GetInt32(),
            state.GetProperty("food").GetInt32(),state.GetProperty("ore").GetInt32());
        LoadSkirmishEconomy(data);
        LoadSkirmishConstruction(data);
        LoadNeutralStructures(data);
        LoadSkirmishProduction(data);
        ApplySkirmishHud(_commands);
    }

    private void ApplySkirmishHud(CommandPanel panel)
    {
        panel.ShowResources(true);
        var bank = _world!.Economies.GetValueOrDefault(0);
        panel.SetResources(_world.PopulationUsed(0),_world.PopulationCapacity(0),bank?.Food ?? _startState.Food,bank?.Ore ?? _startState.Ore);
    }

    private void LoadSkirmishEconomy(JsonElement data)
    {
        if (!data.TryGetProperty("economy",out var economy))
            throw new InvalidOperationException("Refresh skirmish economy: python tools/skirmish_economy.py --cache converted/skirmish");
        if (economy.GetProperty("format_version").GetInt32() != 1 || economy.GetProperty("ticks_per_second").GetInt32() != SimWorld.TicksPerSecond)
            throw new InvalidOperationException("Unsupported economy cache");
        foreach (var worker in economy.GetProperty("workers").EnumerateObject())
            _world!.Workers.Add(worker.Name,new(worker.Value.GetProperty("food_capacity").GetInt32(),worker.Value.GetProperty("ore_capacity").GetInt32()));
        foreach (var start in data.GetProperty("bases").EnumerateArray())
        {
            int team = start.GetProperty("team").GetInt32(); var at = start.GetProperty("position");
            _world!.Economies.Add(team,new(_startState.Food,_startState.Ore,economy.GetProperty("food_capacity").GetInt32(),economy.GetProperty("ore_capacity").GetInt32()));
            int x = (int)Math.Round(at[0].GetDouble()*1000),z = (int)Math.Round(at[1].GetDouble()*1000);
            _world.DropOffs.Add(new(team,x,z,SkirmishInteractionRadius(start.GetProperty("footprint_object").GetInt32(),x,z)));
        }
        foreach (var resource in data.GetProperty("resources").EnumerateArray())
        {
            string kind = resource.GetProperty("kind").GetString()!;
            var rules = economy.GetProperty("sources").GetProperty(kind); var at = resource.GetProperty("position");
            int id = resource.GetProperty("id").GetInt32();
            int x = (int)Math.Round(at[0].GetDouble()*1000),z = (int)Math.Round(at[1].GetDouble()*1000);
            _world!.Resources.Add(id,new(id,kind == "ore" ? ResourceKind.Ore : ResourceKind.Food,
                x,z,resource.GetProperty("initial").GetInt32(),rules.GetProperty("rate_milli_per_tick").GetInt32(),
                rules.GetProperty("raw_fraction_ppm").GetInt32(),SkirmishInteractionRadius(id,x,z),
                rules.GetProperty("improved_rate_milli_per_tick").GetInt32(),rules.GetProperty("improved_fraction_ppm").GetInt32()));
        }
    }

    private int SkirmishInteractionRadius(int objectId,int x,int z)
    {
        var layer = _obstacleLayers.EnumerateArray().First(o => o.GetProperty("id").GetInt32() == objectId);
        double extent = layer.GetProperty("footprints").EnumerateArray().SelectMany(b => b.GetProperty("polygon").EnumerateArray())
            .Max(p => Math.Sqrt(Math.Pow(p[0].GetDouble()*1000-x,2)+Math.Pow(p[1].GetDouble()*1000-z,2)));
        int body = _world!.Units.Where(u => _world.Workers.ContainsKey(u.Definition.Name)).Max(u => u.Definition.BodyRadiusMm);
        // Conservative collision rasterization needs a body radius and one grid cell beyond the footprint.
        return (int)Math.Ceiling(extent)+body+_world.Navigation.CellSize;
    }

    private bool TryGather(Vector2 mouse,bool queued)
    {
        if (!_skirmish || _orders.Pending != null || !_world!.Units.Any(u => u.Team == 0 && u.Health > 0
            && _selected.Contains(u.Id) && _world.Workers.ContainsKey(u.Definition.Name))) return false;
        var source = ResourceAt(mouse);
        if (source == null) return false;
        if (_world.ConstructionSites.TryGetValue(source.Id,out var site) && site.Team == 0 && !site.Complete)
        {
            _world.Enqueue(0,SelectedWorkers(),OrderKind.Build,target: source.Id,queued: queued);
            _status.Text = "Resume construction"; return true;
        }
        if (!_world.CanGather(0,source.Id) && !_world.Units.Any(u => u.Team == 0 && _selected.Contains(u.Id) && u.Health > 0 && u.CargoMilli > 0))
        { _status.Text = site is { Team: > 0 } ? "Enemy resource building" : source.Kind == ResourceKind.Food ? "Food gathering requires a Mill" : "No raw ore remains; a Foundry is required"; return true; }
        var workers = _world.Units.Where(u => u.Team == 0 && u.Health > 0 && _selected.Contains(u.Id)
            && _world.Workers.ContainsKey(u.Definition.Name)).Select(u => u.Id);
        _world.Enqueue(0,workers,OrderKind.Gather,target: source.Id,queued: queued);
        _status.Text = "Gather "+source.Kind.ToString().ToLowerInvariant()+(_paused ? " queued (paused)" : " order issued");
        return true;
    }
}
