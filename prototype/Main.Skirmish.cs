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
        int resolved = body.CanStand(cell) && !taken.Contains(cell) ? cell : body.Nearest(cell,taken);
        if (resolved < 0) throw new InvalidOperationException("No free ground for a start worker");
        return resolved;
    }

    private void LoadSkirmishScene(JsonElement manifest)
    {
        var data = manifest.GetProperty("skirmish");
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
            _obstacleViews[start.GetProperty("footprint_object").GetInt32()] = root;
        }
        _skirmishResourceCount = data.GetProperty("resources").GetArrayLength();
        var state = data.GetProperty("start_state");
        _startState = (state.GetProperty("population").GetInt32(),state.GetProperty("population_cap").GetInt32(),
            state.GetProperty("food").GetInt32(),state.GetProperty("ore").GetInt32());
        ApplySkirmishHud(_commands);
    }

    private void ApplySkirmishHud(CommandPanel panel)
    {
        panel.ShowResources(true);
        panel.SetResources(_startState.Population,_startState.Capacity,_startState.Food,_startState.Ore);
    }
}
