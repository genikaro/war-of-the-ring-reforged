using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Building training controls, rally points and the source rally banner.</summary>
public partial class Main
{
    private Node3D? _rallyBanner;

    private void LoadSkirmishProduction(JsonElement data)
    {
        if (!data.TryGetProperty("production",out var production))
            throw new InvalidOperationException("Refresh production: python tools/skirmish_production.py --cache converted/skirmish");
        if (production.GetProperty("format_version").GetInt32() != 1 || production.GetProperty("ticks_per_second").GetInt32() != SimWorld.TicksPerSecond)
            throw new InvalidOperationException("Unsupported production cache");
        var world = _world!;
        foreach (var unit in production.GetProperty("units").EnumerateObject())
        {
            var rules = unit.Value;
            world.ProductionRules.Add(unit.Name,new(unit.Name,rules.GetProperty("display_name").GetString()!,rules.GetProperty("food_cost").GetInt32(),
                rules.GetProperty("ore_cost").GetInt32(),rules.GetProperty("population").GetInt32(),rules.GetProperty("duration_ticks").GetInt32(),
                rules.TryGetProperty("unique",out var unique) && unique.GetBoolean()));
        }
        foreach (var building in production.GetProperty("population_provided").EnumerateObject()) world.PopulationProvided.Add(building.Name,building.Value.GetInt32());
        world.InitialPopulationCapacity = production.GetProperty("initial_population_capacity").GetInt32();
        world.MaxPopulation = production.GetProperty("max_population").GetInt32();
        var producers = production.GetProperty("producers");
        foreach (var producer in producers.EnumerateObject())
            world.ProducedUnits.Add(producer.Name,producer.Value.GetProperty("units").EnumerateArray().Select(u => u.GetString()!).ToArray());
        foreach (var start in data.GetProperty("bases").EnumerateArray())
        {
            if (!producers.TryGetProperty(start.GetProperty("building").GetString()!,out var producer)) continue;
            int team = start.GetProperty("team").GetInt32(),id = start.GetProperty("footprint_object").GetInt32(); var at = start.GetProperty("position");
            // Start workers already stand on ground connected to the rest of the map; exits must reach the same region.
            world.Producers.Add(id,new(id,team,(int)Math.Round(at[0].GetDouble()*1000),(int)Math.Round(at[1].GetDouble()*1000),
                world.Units.First(u => u.Team == team).Node,producer.GetProperty("units").EnumerateArray().Select(u => u.GetString()!).ToArray()));
        }
        _waypointFlagFile = production.GetProperty("rally_banner").GetString()!;
        _rallyBanner = LoadGlb(_waypointFlagFile,_teamColors[0]);
        _rallyBanner.Visible = false; AddChild(_rallyBanner);
    }

    private ProductionBuilding? SelectedProducer() => _selectedStructure is int id && _world!.Producers.TryGetValue(id,out var producer) && producer.Team == PlayerTeam ? producer : null;

    private void ChooseTraining(int slot)
    {
        if (SelectedProducer() is not { } producer) return;
        if (!_world!.CanTrain(PlayerTeam,producer.Id,slot,out string reason)) { Notify(reason); return; }
        _world.Enqueue(PlayerTeam,[],OrderKind.Train,slot,producer.Id);
        _status.Text = "Train "+_world.ProductionRules[producer.Units[slot]].DisplayName+(_paused ? " queued (paused)" : " order issued");
    }

    /// <summary>Queue index from the selection panel, or -1 for Backspace (the last queued unit).</summary>
    private void CancelTraining(int index)
    {
        if (SelectedProducer() is not { Queue.Count: > 0 } producer) return;
        _world!.Enqueue(PlayerTeam,[],OrderKind.CancelTraining,index,producer.Id);
        _status.Text = "Training cancellation queued";
    }

    private bool TrySetRallyPoint(Vector2 mouse)
    {
        if (SelectedProducer() is not { } producer) return false;
        // A well, an ore pile, or the Mill or Foundry standing on one: new workers go straight to gathering.
        if (StructureAt(mouse) is { } structure && _structureViews[structure.Id].Resource is int resource and >= 0)
        {
            _world!.Enqueue(PlayerTeam,[],OrderKind.SetRallyResource,resource,producer.Id);
            _status.Text = "Rally point set on "+_structureDefinitions[_structureViews[structure.Id].Type].GetProperty("display_name").GetString()+(_paused ? " (paused)" : "");
            _hud.Destination = mouse; _hud.DestinationTime = 0.7f;
            return true;
        }
        Vector3 origin = _camera.ProjectRayOrigin(mouse),direction = _camera.ProjectRayNormal(mouse);
        var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
        int cell = hit == null ? -1 : _world!.Navigation.Nearest(hit.Node);
        if (cell < 0) { _status.Text = "No walkable space at this point"; return true; }
        _world!.Enqueue(PlayerTeam,[],OrderKind.SetRallyPoint,cell,producer.Id);
        _status.Text = "Rally point set"+(_paused ? " (paused)" : "");
        _hud.Destination = mouse; _hud.DestinationTime = 0.7f;
        return true;
    }

    private void PresentProduction()
    {
        var producer = SelectedProducer();
        _commands.RefreshProduction(_world!,producer);
        if (_rallyBanner == null) return;
        _rallyBanner.Visible = producer is { RallyCell: >= 0 };
        if (!_rallyBanner.Visible) return;
        var (x,z) = _world!.Navigation.Center(producer!.RallyCell);
        _rallyBanner.Position = new(-x/1000f,_world.Navigation.NodeHeight(producer.RallyCell)/1000f,z/1000f);
    }
}
