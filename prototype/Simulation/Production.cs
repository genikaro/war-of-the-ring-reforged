namespace Reforged.Simulation;

public sealed record ProductionDefinition(string Unit,string DisplayName,int FoodCost,int OreCost,int Population,int DurationTicks);
public sealed record QueuedUnit(string Unit,int Sequence);

public sealed class ProductionBuilding(int id,int team,int x,int z,int exitAnchor,string[] units)
{
    public int Id { get; } = id;
    public int Team { get; } = team;
    public int X { get; } = x;
    public int Z { get; } = z;
    public int ExitAnchor { get; } = exitAnchor;
    public string[] Units { get; } = units;
    public List<QueuedUnit> Queue { get; } = [];
    public int ProgressTicks { get; internal set; }
    public int RallyCell { get; internal set; } = -1;
}

/// <summary>Training queues: payment and population reservation on request, fixed-tick progress, spawning outside the producer.</summary>
public sealed partial class SimWorld
{
    // ponytail: the original queue length is unverified; five matches the selection-panel slots used here.
    public const int ProductionQueueLimit = 5;
    public Dictionary<string,ProductionDefinition> ProductionRules { get; } = [];
    public SortedDictionary<int,ProductionBuilding> Producers { get; } = [];
    public Dictionary<string,int> PopulationProvided { get; } = [];
    public int MaxPopulation { get; set; } = 100;
    public int InitialPopulationCapacity { get; set; }

    public int PopulationCapacity(int team) => Math.Min(MaxPopulation,InitialPopulationCapacity
        + (StartBuildings.GetValueOrDefault(team) ?? []).Sum(name => PopulationProvided.GetValueOrDefault(name))
        + ConstructionSites.Values.Where(s => s.Team == team && s.Complete)
            .Sum(s => PopulationProvided.GetValueOrDefault(SiteRules(s).Name)));

    /// <summary>Living units plus queued training: the original reserves population when a unit is queued.</summary>
    public int PopulationUsed(int team) => Units.Where(u => u.Team == team && u.Health > 0)
            .Sum(u => ProductionRules.GetValueOrDefault(u.Definition.Name)?.Population ?? 0)
        + Producers.Values.Where(p => p.Team == team).Sum(p => p.Queue.Sum(q => ProductionRules[q.Unit].Population));

    public bool CanTrain(int team,int building,int slot,out string reason)
    {
        reason = "Unknown producer";
        if (!Producers.TryGetValue(building,out var producer) || producer.Team != team || slot < 0 || slot >= producer.Units.Length
            || !ProductionRules.TryGetValue(producer.Units[slot],out var rules) || !Economies.TryGetValue(team,out var bank)) return false;
        if (producer.Queue.Count >= ProductionQueueLimit) { reason = "Training queue full"; return false; }
        if (bank.FoodMilli < rules.FoodCost*1000L || bank.OreMilli < rules.OreCost*1000L) { reason = "Insufficient resources"; return false; }
        if (PopulationUsed(team)+rules.Population > PopulationCapacity(team)) { reason = "Population limit reached"; return false; }
        reason = "Ready to train"; return true;
    }

    private void BeginTraining(SimCommand command)
    {
        if (!CanTrain(command.Team,command.Target,command.Cell,out _)) return;
        var producer = Producers[command.Target]; var rules = ProductionRules[producer.Units[command.Cell]]; var bank = Economies[command.Team];
        bank.FoodMilli -= rules.FoodCost*1000L; bank.OreMilli -= rules.OreCost*1000L;
        producer.Queue.Add(new(rules.Unit,command.Sequence));
    }

    private void CancelTraining(SimCommand command)
    {
        if (!Producers.TryGetValue(command.Target,out var producer) || producer.Team != command.Team) return;
        int index = command.Cell < 0 ? producer.Queue.Count-1 : command.Cell;
        if (index < 0 || index >= producer.Queue.Count) return;
        var rules = ProductionRules[producer.Queue[index].Unit]; var bank = Economies[command.Team];
        // ponytail: no source field states a training refund; cancellation returns the full cost, capped by storage.
        bank.FoodMilli = Math.Min(bank.FoodCapacityMilli,bank.FoodMilli+rules.FoodCost*1000L);
        bank.OreMilli = Math.Min(bank.OreCapacityMilli,bank.OreMilli+rules.OreCost*1000L);
        producer.Queue.RemoveAt(index);
        if (index == 0) producer.ProgressTicks = 0;
    }

    private void SetRallyPoint(SimCommand command)
    {
        if (Producers.TryGetValue(command.Target,out var producer) && producer.Team == command.Team && command.Cell < Navigation.NodeCount)
            producer.RallyCell = Math.Max(-1,command.Cell);
    }

    private void UpdateProduction()
    {
        foreach (var producer in Producers.Values.Where(p => p.Queue.Count > 0))
        {
            var rules = ProductionRules[producer.Queue[0].Unit];
            if (producer.ProgressTicks < rules.DurationTicks) producer.ProgressTicks++;
            if (producer.ProgressTicks < rules.DurationTicks) continue;
            var definition = Rules.Units[rules.Unit];
            int cell = ProductionExit(producer,definition);
            if (cell < 0) continue; // A finished unit waits until there is room outside the building.
            var unit = Spawn(producer.Team,cell,definition);
            int sequence = producer.Queue[0].Sequence;
            producer.Queue.RemoveAt(0); producer.ProgressTicks = 0;
            if (producer.RallyCell >= 0) Apply(new(Tick,sequence,producer.Team,[unit.Id],producer.RallyCell,Kind: OrderKind.Move),activating: true);
        }
    }

    private int ProductionExit(ProductionBuilding producer,UnitDefinition definition)
    {
        var body = Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
        var taken = OccupiedNodes(-1);
        int center = Navigation.Cell(producer.X,producer.Z);
        // ponytail: the nearest free standing cell to the building center, not a recovered door position.
        // Sixteen candidates bound the per-tick cost while a crowd blocks the exit.
        for (int attempt = 0; attempt < 16; attempt++)
        {
            int cell = body.Nearest(center,taken,connectedTo: producer.ExitAnchor);
            if (cell < 0) return -1;
            if (CanPlace(SpawnCandidate(producer.Team,cell,definition))) return cell;
            taken.Add(cell);
        }
        return -1;
    }

    private string ProductionDigest() => ":production-rules:"+string.Join(';',ProductionRules.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Value))
        + ":producers:"+string.Join(';',Producers.Values.Select(p => $"{p.Id},{p.Team},{p.X},{p.Z},{p.ExitAnchor},{string.Join('/',p.Units)},{p.ProgressTicks},{p.RallyCell},{string.Join('/',p.Queue.Select(q => q.Unit+"#"+q.Sequence))}"))
        + ":population:"+MaxPopulation+","+InitialPopulationCapacity+","+string.Join(';',PopulationProvided.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+p.Value));
}
