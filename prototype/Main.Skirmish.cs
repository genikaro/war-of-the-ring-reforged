using Godot;
using Reforged.Presentation;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Skirmish start on any imported multiplayer map: start buildings, resource strip and spawn placement.</summary>
public partial class Main
{
    private bool _skirmish;
    private const int SkirmishObstacleBase = 10000;
    private readonly List<Node3D> _buildingViews = [];
    private readonly List<(int Obstacle,int Team,string Name,int X,int Z,Node3D Root,Node3D Model,JsonElement Appearance)> _startBuildings = [];
    private int _skirmishResourceCount;
    private (int Population,int Capacity,int Food,int Ore) _startState;

    /// <summary>Workers start on ground connected to their side's first unit (river maps split the sides' ground);
    /// a body-clearance miss moves the spawn to the nearest free standing cell.</summary>
    private int SkirmishSpawnCell(int team,UnitDefinition definition,int cell)
    {
        var body = _world!.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
        var taken = _world.Units.Where(u => u.Health > 0).Select(u => u.Node).ToHashSet();
        // A side's first worker takes the nearest free cell; the importer already put its cell on the base's ground.
        int anchor = _world.Units.FirstOrDefault(u => u.Team == team)?.Node ?? -1;
        int resolved = body.CanStand(cell) && !taken.Contains(cell) && (anchor < 0 || body.IsConnected(cell,anchor))
            ? cell : body.Nearest(cell,taken,connectedTo: anchor);
        if (resolved < 0) throw new InvalidOperationException("No free ground for a start worker");
        return resolved;
    }

    private async Task LoadSkirmishScene(JsonElement manifest)
    {
        var data = manifest.GetProperty("skirmish");
        SeatFactions(data);
        LoadStructureDefinitions(data);
        await LoadingStep("Placing the bases",.64f);
        var files = data.GetProperty("building_files");
        // A closed slot has no base: its start footprint frees the ground.
        foreach (var start in Bases(data).Where(b => !SlotOpen(b.GetProperty("team").GetInt32())))
            _world!.Navigation.SetObstacle(start.GetProperty("footprint_object").GetInt32(),false);
        foreach (var start in StartBases(data))
        {
            int team = start.Team;
            var at = start.Position;
            var root = new Node3D { Name = "Building_" + _buildingViews.Count,
                // Source metres are Z-up; the map export uses (-x, z, y) for placements.
                Position = new(-at[0].GetSingle(),at[2].GetSingle(),at[1].GetSingle()) };
            string file = files.GetProperty(start.Building).GetString()!;
            var model = TurnModel(LoadGlb(file,_teamColors[team]),start.Building); root.AddChild(model);
            // Restored building models carry every source state; show the healthy one.
            var appearance = ReadAppearance(file);
            new Reforged.Rendering.UnitAppearance(model,appearance).Apply(BuildingStateClips(appearance)[3],0);
            AddChild(root); _buildingViews.Add(root);
            int obstacle = start.Footprint;
            RegisterStructure(obstacle,team,start.Building,root);
            _obstacleViews[obstacle] = root;
            _startBuildings.Add((obstacle,team,start.Building,start.X,start.Z,root,model,appearance));
            await LoadingStep(null,null);
        }
        _skirmishResourceCount = data.GetProperty("resources").GetArrayLength();
        var state = data.GetProperty("start_state");
        _startState = (state.GetProperty("population").GetInt32(),state.GetProperty("population_cap").GetInt32(),
            state.GetProperty("food").GetInt32(),state.GetProperty("ore").GetInt32());
        await LoadingStep("Placing resources",.72f);
        LoadSkirmishEconomy(data);
        LoadSkirmishConstruction(data);
        RegisterStartBuildings();
        await LoadingStep("Placing places of power",.78f);
        LoadPlacesOfPower(data);
        LoadNeutralStructures(data);
        await LoadingStep("Preparing training and research",.84f);
        LoadSkirmishProduction(data);
        LoadSkirmishResearch(data);
        if (_sandboxMap) { await LoadingStep("Building the Sandbox bases",.86f); PrepareSandboxMap(); }
        await LoadingStep("Preparing the bots",.9f);
        LoadSkirmishAi(data);
        StartFogOfWar();
        FrameStartBase();
        ApplySkirmishHud(_commands);
    }

    /// <summary>Build-151: whose base the camera starts on: yours, or player 1's when you watch.</summary>
    private int StartViewTeam => _observing ? _world!.Contenders.DefaultIfEmpty(0).First() : PlayerTeam;

    /// <summary>Build-151: the match starts on that base. The map's camera cell is its first base; an observer framed every
    /// unit on the map from far above, and a player in another slot started looking at someone else's base.</summary>
    private void FrameStartBase()
    {
        var world = _world!; int team = StartViewTeam;
        var starts = world.ConstructionSites.Values.Where(s => s.Prebuilt && s.Team >= 0).OrderBy(s => s.Id).ToArray();
        var own = starts.FirstOrDefault(s => s.Team == team);
        // The first base keeps the map's own camera cell, so the smokes' framing stays as it was.
        var first = starts.OrderBy(s => _squadFocus.DistanceSquaredTo(PositionOf(world.SitePosition(s).X,world.SitePosition(s).Z))).FirstOrDefault();
        if (own != null && own != first) { var (x,z) = world.SitePosition(own); _squadFocus = PositionOf(x,z); }
    }

    private void ApplySkirmishHud(CommandPanel panel)
    {
        panel.ShowResources(!_observing,armies: !_observing);
        if (_observing) return;
        var bank = _world!.Economies.GetValueOrDefault(PlayerTeam);
        panel.SetResources(_world.PopulationUsed(PlayerTeam),_world.PopulationCapacity(PlayerTeam),bank?.Food ?? _startState.Food,bank?.Ore ?? _startState.Ore,bank?.Fate ?? 0,
            bank == null ? null : FateTip(_world,PlayerTeam));
    }

    /// <summary>Build-76: the Fate cell's state, the progress toward the next point and what battle has earned.</summary>
    public static string FateTip(SimWorld world,int team)
    {
        // Build-184: in the install's language.
        var bank = world.Economies[team];
        string progress = GameText.T(world.FateRules == null ? "fate.not_earned" : bank.Fate >= world.MaxFate ? "fate.max_reached" : "fate.progress",
            world.FateProgressPercent(team));
        return HudTip.Make($"{GameText.T("resource.fate")} {bank.Fate}/{world.MaxFate}",null,progress,
            GameText.T(world.FactionOf(team) == SimWorld.Evil ? "fate.earned_evil" : "fate.earned",bank.FateEarned),GameText.T("fate.click"));
    }

    private void LoadSkirmishEconomy(JsonElement data)
    {
        if (!data.TryGetProperty("economy",out var economy))
            throw new InvalidOperationException("The rules give no skirmish economy");
        // Format 2 (build-17) adds the worker's gathering clips and owner trip rules.
        if (economy.GetProperty("format_version").GetInt32() != 2 || economy.GetProperty("ticks_per_second").GetInt32() != SimWorld.TicksPerSecond)
            throw new InvalidOperationException("The rules give no skirmish economy");
        // Build-42 adds each source's building clearance.
        foreach (var source in economy.GetProperty("sources").EnumerateObject())
            _world!.ResourceClearanceMm[source.Name == "ore" ? ResourceKind.Ore : ResourceKind.Food] = source.Value.TryGetProperty("building_clearance_mm",out var clearance)
                ? clearance.GetInt32() : throw new InvalidOperationException("The rules give no skirmish economy");
        foreach (var worker in economy.GetProperty("workers").EnumerateObject().Where(w => Loaded(w.Name)))
        {
            _world!.Workers.Add(worker.Name,WorkerEconomy.Parse(worker.Value));
            _workerClips[worker.Name] = worker.Value.GetProperty("animations").EnumerateObject()
                .ToDictionary(c => c.Name,c => (c.Value.GetProperty("state").GetString()!,c.Value.GetProperty("clip").GetString()!));
        }
        foreach (var start in StartBases(data))
        {
            int team = start.Team;
            _world!.Economies.Add(team,new(_startState.Food,_startState.Ore,economy.GetProperty("food_capacity").GetInt32(),economy.GetProperty("ore_capacity").GetInt32()));
            int x = start.X,z = start.Z,footprint = start.Footprint;
            _world.DropOffs.Add(new(team,x,z,SkirmishInteractionRadius(footprint,x,z),footprint));
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

    /// <summary>A start building becomes a completed construction site, so it carries health, can be attacked
    /// and repaired, and shows the same world health bar as anything a worker puts up. Its view is bound here
    /// rather than created by the construction pass, which would load a second model over it.</summary>
    private void RegisterStartBuildings()
    {
        foreach (var (obstacle,team,name,x,z,root,model,appearance) in _startBuildings)
        {
            var rules = _world!.BuildingRules[name];
            var site = new ConstructionSite(obstacle,obstacle,SkirmishInteractionRadius(obstacle,x,z),name,x,z)
                { Team = team,Health = rules.MaxHealth,Complete = true,Prebuilt = true };
            _world.ConstructionSites.Add(obstacle,site);
            _world.Contenders.Add(team);
            var (barWidth,barHeight) = SiteBarSize(site);
            var bar = new Reforged.Rendering.HealthBar(barWidth,barHeight) { Position = new(0,.14f,0) };
            root.AddChild(bar);
            _constructionViews.Add(obstacle,new BuildingVisual(root,model,new Reforged.Rendering.UnitAppearance(model,appearance),
                Descendants(model).OfType<AnimationPlayer>().FirstOrDefault(),BuildingStateClips(appearance),bar,name,site));
        }
    }

    private int SkirmishInteractionRadius(int objectId,int x,int z)
    {
        // An Evil start building's footprint was laid at load (SeatFactions); the map's saved layers hold the Citadel's.
        if (_startFootprints.TryGetValue(objectId,out var polygon))
            return InteractionRadius(polygon.Max(p => Math.Sqrt(Math.Pow(p.X*1000.0-x,2)+Math.Pow(p.Y*1000.0-z,2))));
        var layer = _obstacleLayers.EnumerateArray().First(o => o.GetProperty("id").GetInt32() == objectId);
        return InteractionRadius(layer.GetProperty("footprints").EnumerateArray().SelectMany(b => b.GetProperty("polygon").EnumerateArray())
            .Max(p => Math.Sqrt(Math.Pow(p[0].GetDouble()*1000-x,2)+Math.Pow(p[1].GetDouble()*1000-z,2))));
    }

    /// <summary>Work envelope from the footprint's farthest corner (millimeters from the building center).</summary>
    private int InteractionRadius(double extent)
    {
        int body = _world!.Units.Where(u => _world.Workers.ContainsKey(u.Definition.Name)).Max(u => u.Definition.BodyRadiusMm);
        // Conservative collision rasterization needs a body radius and one grid cell beyond the footprint.
        return (int)Math.Ceiling(extent)+body+_world.Navigation.CellSize;
    }

    /// <summary>Right-clicking an enemy building sends every armed unit in the selection to break it down. An
    /// armed Attack command forces the target, so it also takes one's own buildings, as force-attack does
    /// elsewhere; a plain right-click stays contextual and leaves friendly buildings to the repair order.</summary>
    private bool TrySiege(Vector2 mouse,bool queued)
    {
        bool forced = _orders.Pending is OrderKind.Attack or OrderKind.ForceAttack;
        if (!_skirmish || !forced && _orders.Pending != null) return false;
        if (StructureAt(mouse) is not { } structure) return false;
        var site = _world!.ConstructionSites.Values.FirstOrDefault(s => s.Obstacle == structure.Id);
        if (site == null || site.Team < 0 || site.Health <= 0) return false;
        if (!forced && !_world.CanAttackSite(PlayerTeam,site.Id)) return false;
        var attackers = _world.Units.Where(u => u.Team == PlayerTeam && u.Present && _selected.Contains(u.Id)
            && u.Definition.DpsMilli > 0 && !u.RingActive).Select(u => u.Id).ToArray();
        _orders.Cancel();
        if (attackers.Length == 0) { _status = "Nothing in the selection can attack a building"; return true; }
        _world.Enqueue(PlayerTeam,attackers,OrderKind.AttackStructure,cell: forced ? SimWorld.ForcedSiege : -1,target: site.Id,queued: queued);
        ShowStructureCommandFeedback(structure.Id);
        _status = (site.Team == PlayerTeam ? "Force-attack " : "Attack ")+_world.SiteRules(site).DisplayName
            +(_paused ? " queued (paused)" : " order issued");
        return true;
    }

    private bool TryGather(Vector2 mouse,bool queued)
    {
        if (!_skirmish || _orders.Pending != null || !_world!.Units.Any(u => u.Team == PlayerTeam && u.Health > 0
            && _selected.Contains(u.Id) && _world.Workers.ContainsKey(u.Definition.Name))) return false;
        if (StructureAt(mouse) is { } structure && _world.ConstructionSites.Values.FirstOrDefault(s => s.Obstacle == structure.Id)
            is { } owned && owned.Team == PlayerTeam && (!owned.Complete || _world.NeedsRepair(owned)))
        {
            _world.Enqueue(PlayerTeam,SelectedWorkers(),OrderKind.Build,target: owned.Id,queued: queued);
            ShowStructureCommandFeedback(structure.Id);
            _status = owned.Complete ? "Repair "+_world.SiteRules(owned).DisplayName : "Resume construction";
            return true;
        }
        var source = ResourceAt(mouse);
        if (source == null) return false;
        if (_world.ConstructionSites.TryGetValue(source.Id,out var site) && site.Team == PlayerTeam && !site.Complete)
        {
            _world.Enqueue(PlayerTeam,SelectedWorkers(),OrderKind.Build,target: source.Id,queued: queued);
            ShowStructureCommandFeedback(site.Obstacle);
            _status = "Resume construction"; return true;
        }
        if (!_world.CanGather(PlayerTeam,source.Id) && !_world.Units.Any(u => u.Team == PlayerTeam && _selected.Contains(u.Id) && u.Health > 0 && u.CargoMilli > 0))
        { _status = site is { Team: > 0 } ? "Enemy resource building" : source.Kind == ResourceKind.Food ? $"Food gathering requires a {_world.ResourceRules(PlayerTeam,ResourceKind.Food)?.DisplayName ?? "Mill"}"
            : $"No raw ore remains; a {_world.ResourceRules(PlayerTeam,ResourceKind.Ore)?.DisplayName ?? "Foundry"} is required"; return true; }
        var workers = _world.Units.Where(u => u.Team == PlayerTeam && u.Health > 0 && _selected.Contains(u.Id)
            && _world.Workers.ContainsKey(u.Definition.Name)).Select(u => u.Id);
        _world.Enqueue(PlayerTeam,workers,OrderKind.Gather,target: source.Id,queued: queued);
        if (StructureAt(mouse) is { } gathered) ShowStructureCommandFeedback(gathered.Id);
        _status = "Gather "+source.Kind.ToString().ToLowerInvariant()+(_paused ? " queued (paused)" : " order issued");
        return true;
    }
}
