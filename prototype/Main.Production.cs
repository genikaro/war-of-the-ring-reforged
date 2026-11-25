using Godot;
using Reforged.Simulation;
using System.Text.Json;
using Reforged.Presentation;

/// <summary>Building training controls, rally points and the source rally banner.</summary>
public partial class Main
{
    private Node3D? _rallyBanner;

    private void LoadSkirmishProduction(JsonElement data)
    {
        if (!data.TryGetProperty("production",out var production))
            throw new InvalidOperationException("The rules give no production");
        if (production.GetProperty("format_version").GetInt32() != 1 || production.GetProperty("ticks_per_second").GetInt32() != SimWorld.TicksPerSecond)
            throw new InvalidOperationException("Unsupported production cache");
        var world = _world!;
        foreach (var unit in production.GetProperty("units").EnumerateObject().Where(u => Loaded(u.Name)))
        {
            var rules = unit.Value;
            world.ProductionRules.Add(unit.Name,new(unit.Name,GameText.Name(unit.Name,rules.GetProperty("display_name").GetString()!),rules.GetProperty("food_cost").GetInt32(),
                rules.GetProperty("ore_cost").GetInt32(),rules.GetProperty("population").GetInt32(),rules.GetProperty("duration_ticks").GetInt32(),
                rules.TryGetProperty("unique",out var unique) && unique.GetBoolean(),
                // Build-44 exports each unit's source BuildingRequired list.
                rules.TryGetProperty("prerequisites",out var required) ? required.EnumerateArray().Select(p => p.GetString()!).ToArray()
                    : throw new InvalidOperationException("The rules give no production"),
                // Build-58 adds the Fate heroes and their group.
                rules.TryGetProperty("fate_cost",out var fate) ? fate.GetInt32() : 0,
                rules.TryGetProperty("unique_group",out var group) ? group.GetInt32() : -1));
        }
        if (production.TryGetProperty("unique_group_capacity",out var capacity))
            foreach (var building in capacity.EnumerateObject().Where(b => Loaded(b.Name)))
                world.UniqueGroupCapacity.Add(building.Name,(building.Value.GetProperty("group").GetInt32(),building.Value.GetProperty("amount").GetInt32()));
        if (production.TryGetProperty("max_fate",out var maxFate)) world.MaxFate = maxFate.GetInt32();
        // Build-76: battle earns Fate by the source WorldVariables, at the Medium Power Acquisition rate.
        if (production.TryGetProperty("fate",out var earning))
            world.FateRules = new(earning.GetProperty("level_per_point").GetInt32(),earning.GetProperty("by_damage_ppm").GetInt32(),
                earning.GetProperty("reduction_ppm").GetInt32(),earning.GetProperty("max_distance_ppm").GetInt32(),
                earning.GetProperty("acquisition_ppm").GetProperty("medium").GetInt32(),
                earning.GetProperty("town_centers").EnumerateArray().Select(t => t.GetString()!).ToArray())
                { WarpostPpm = earning.TryGetProperty("warpost_ppm",out var warpost) ? warpost.GetInt32() : 0 };
        foreach (var building in production.GetProperty("population_provided").EnumerateObject()) world.PopulationProvided.Add(building.Name,building.Value.GetInt32());
        world.InitialPopulationCapacity = production.GetProperty("initial_population_capacity").GetInt32();
        world.MaxPopulation = production.GetProperty("max_population").GetInt32();
        var producers = production.GetProperty("producers");
        // Build-133: the Goblin Slavemaster adds population while it lives, up to its per-player limit.
        if (production.TryGetProperty("population_by_unit",out var byUnit))
            foreach (var unit in byUnit.EnumerateObject().Where(u => Loaded(u.Name)))
                world.PopulationByUnit[unit.Name] = (unit.Value.GetProperty("added").GetInt32(),unit.Value.GetProperty("max").GetInt32());
        foreach (var producer in producers.EnumerateObject().Where(p => Loaded(p.Name)))
            world.ProducedUnits.Add(producer.Name,producer.Value.GetProperty("units").EnumerateArray().Select(u => u.GetString()!).ToArray());
        foreach (var start in StartBases(data))
        {
            if (!producers.TryGetProperty(start.Building,out var producer)) continue;
            int team = start.Team,id = start.Footprint;
            // Start workers already stand on ground connected to the rest of the map; exits must reach the same region.
            world.Producers.Add(id,new(id,team,start.X,start.Z,
                world.Units.First(u => u.Team == team).Node,producer.GetProperty("units").EnumerateArray().Select(u => u.GetString()!).ToArray()));
        }
        _waypointFlagFile = production.GetProperty("rally_banner").GetString()!;
        _rallyBanner = LoadGlb(_waypointFlagFile,TeamColor(Math.Max(0,PlayerTeam)));
        _rallyBanner.Visible = false; AddChild(_rallyBanner);
    }

    private ProductionBuilding? SelectedProducer() => _selectedStructure is int id && _world!.Producers.TryGetValue(id,out var producer) && producer.Team == PlayerTeam ? producer : null;

    /// <summary>The player's own selected building, finished or not.</summary>
    private ConstructionSite? SelectedOwnSite() => _selectedStructure is int id
        ? _world!.ConstructionSites.Values.FirstOrDefault(s => s.Obstacle == id && s.Team == PlayerTeam) : null;

    private string? UpgradePortrait() => SelectedOwnSite() is { } site && _world!.UpgradeRules(site) is { } rules
        ? _structureDefinitions[rules.Name].GetProperty("portrait").GetString() : null;

    private void ChooseUpgrade()
    {
        if (SelectedOwnSite() is not { } site || _world!.UpgradeRules(site) is not { } rules) return;
        if (!_world.CanUpgrade(PlayerTeam,site.Id,out string reason)) { Notify(reason); return; }
        _world.Enqueue(PlayerTeam,[],OrderKind.Upgrade,target: site.Id);
        _status = "Upgrade to "+rules.DisplayName+(_paused ? " queued (paused)" : " order issued");
    }

    private void ChooseTraining(int slot)
    {
        if (SelectedProducer() is not { } producer) return;
        if (!_world!.CanTrain(PlayerTeam,producer.Id,slot,out string reason)) { Notify(reason); return; }
        _world.Enqueue(PlayerTeam,[],OrderKind.Train,slot,producer.Id);
        _status = "Train "+_world.ProductionRules[producer.Units[slot]].DisplayName+(_paused ? " queued (paused)" : " order issued");
    }

    /// <summary>Queue index from the selection panel, or -1 for Backspace (the last queued unit). An upgrade in progress
    /// heads the panel's queue and is cancelled last by Backspace.</summary>
    private void CancelTraining(int index)
    {
        // An upgrade or a research in progress heads the panel's queue.
        if (SelectedOwnSite() is { } site && (site.Upgrading || site.Research != null))
        {
            if (index == 0 || index < 0 && SelectedProducer() is not { Queue.Count: > 0 })
            {
                _world!.Enqueue(PlayerTeam,[],site.Upgrading ? OrderKind.CancelUpgrade : OrderKind.CancelResearch,target: site.Id);
                _status = (site.Upgrading ? "Upgrade" : "Research")+" cancellation queued"; return;
            }
            if (index > 0) index--;
        }
        if (SelectedProducer() is not { Queue.Count: > 0 } producer) return;
        _world!.Enqueue(PlayerTeam,[],OrderKind.CancelTraining,index,producer.Id);
        _status = "Training cancellation queued";
    }

    private bool TrySetRallyPoint(Vector2 mouse)
    {
        if (SelectedProducer() is not { } producer) return false;
        // A well, an ore pile, or the Mill or Foundry standing on one: new workers go straight to gathering.
        var structure = StructureAt(mouse);
        if (structure is { } target && _structureViews[target.Id].Resource is int resource and >= 0)
        {
            _world!.Enqueue(PlayerTeam,[],OrderKind.SetRallyResource,resource,producer.Id);
            _status = "Rally point set on "+_structureDefinitions[_structureViews[target.Id].Type].GetProperty("display_name").GetString()+(_paused ? " (paused)" : "");
            ShowStructureCommandFeedback(target.Id);
            return true;
        }
        // Build-224: the ground beside a well or an ore pile counts as the pile, as it does for a worker's gather order
        // (ResourceAt), so a rally dropped on a resource sends the new workers to gather it.
        if (structure == null && ResourceAt(mouse) is { } source)
        {
            _world!.Enqueue(PlayerTeam,[],OrderKind.SetRallyResource,source.Id,producer.Id);
            _status = "Rally point set on "+(source.Kind == ResourceKind.Food ? "food" : "ore")+(_paused ? " (paused)" : "");
            if (_structureViews.ContainsKey(source.Id)) ShowStructureCommandFeedback(source.Id);
            return true;
        }
        Vector3 origin = _camera.ProjectRayOrigin(mouse),direction = _camera.ProjectRayNormal(mouse);
        var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
        int cell = hit == null ? -1 : _world!.Navigation.Nearest(hit.Node);
        if (cell < 0) { _status = "No walkable space at this point"; return true; }
        _world!.Enqueue(PlayerTeam,[],OrderKind.SetRallyPoint,cell,producer.Id);
        _status = "Rally point set"+(_paused ? " (paused)" : "");
        if (structure is { } hitStructure) ShowStructureCommandFeedback(hitStructure.Id);
        else ShowGroundCommandFeedback(new(-hit!.Position.X,hit.Position.Y,hit.Position.Z),hit.Ground);
        return true;
    }

    private void PresentProduction()
    {
        var producer = SelectedProducer();
        var site = SelectedOwnSite();
        _commands.RefreshProduction(_world!,producer,site,UpgradePortrait(),site?.Research is { } research ? _techniquePortraits.GetValueOrDefault(research) : null);
        _commands.RefreshResearch(_world!,site,name => _techniquePortraits.GetValueOrDefault(name));
        if (_rallyBanner == null) return;
        _rallyBanner.Visible = producer is { RallyCell: >= 0 };
        if (!_rallyBanner.Visible) return;
        var (x,z) = _world!.Navigation.Center(producer!.RallyCell);
        _rallyBanner.Position = new(-x/1000f,_world.Navigation.NodeHeight(producer.RallyCell)/1000f,z/1000f);
    }
}
