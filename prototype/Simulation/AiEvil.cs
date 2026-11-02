namespace Reforged.Simulation;

/// <summary>Bots of the Evil side (build-134, adapter rules; the source decision code is not recovered). The Fortress is the tech
/// building, the Slavemaster supplies population and lays War Posts, and every building but the Slaughterhouse, Smelter and War
/// Post stands on corrupted ground: a bot whose next building finds no corrupted room has a Slavemaster lay a War Post at the edge
/// of its ground, away from the enemy, and an expansion Hovel gets a War Post beside its site first.</summary>
public sealed partial class SimWorld
{
    /// <summary>The building whose upgrade line is the side's technology: the Citadel, the Fortress of Mordor.</summary>
    public Dictionary<string,string> TechRoots { get; } = [];
    public string? TechRoot(int team) => TechRoots.GetValueOrDefault(FactionOf(team)) ?? StartStructures.FirstOrDefault(s => s.Team == team)?.Building;

    /// <summary>Source corruption makers: the War Post.</summary>
    private bool AiIsPost(string building) => Corruption?.Creators.ContainsKey(building) == true;
    private string? WarPost => Corruption?.Creators.Keys.FirstOrDefault();

    /// <summary>The unit that lays War Posts for the bot, nearest the spot: idle or walking, never one already laying.</summary>
    private SimUnit[] AiPostLayers(AiPlayer ai,string post) => [.. ActiveUnits.Where(u => u.Team == ai.Team && u.Present
        && Palettes.TryGetValue(u.Definition.Name,out var palette) && palette.Contains(post) && !Workers.ContainsKey(u.Definition.Name))];

    /// <summary>A free, legal spot for a War Post on a ring around (x, z), the safe side (away from the nearest enemy building)
    /// first when <paramref name="away"/>, else the side facing the enemy; or -1.</summary>
    private int AiWarPostCell(AiPlayer ai,string post,int x,int z,int first,int last,bool away)
    {
        var enemy = AiEnemyPoint(ai,x,z) ?? (x+1000,z);
        double facing = Math.Atan2(enemy.Z-z,enemy.X-x)+(away ? Math.PI : 0);
        for (int ring = first; ring <= last; ring += 2000)
            for (int step = 0; step < 24; step++)
            {
                double angle = facing+(step%2 == 0 ? 1 : -1)*((step+1)/2)*Math.Tau/24;
                int cell = Navigation.Cell(x+(int)Math.Round(ring*Math.Cos(angle)),z+(int)Math.Round(ring*Math.Sin(angle)));
                if (cell < 0 || ai.RefusedCells.Contains(cell)) continue;
                var (cx,cz) = Navigation.Center(cell);
                if (Resources.Values.Any(r => DistanceSquared(r.X,r.Z,cx,cz) < (long)AiResourceClearanceMm*AiResourceClearanceMm)) continue;
                if (CanPlaceBuilding(ai.Team,post,cell,out _,ignoreUnits: true)) return cell;
            }
        return -1;
    }

    /// <summary>Has a Slavemaster lay a War Post around (x, z): the edge of the base's ground (expansion false), or beside an
    /// expansion Hovel's site. Null when no spot exists; otherwise the plan text and the ore reserved.</summary>
    private (string Plan,int Food,int Ore)? AiLayWarPost(AiPlayer ai,bool expansion,int x,int z)
    {
        string? post = WarPost;
        if (post == null || !BuildingRules.TryGetValue(post,out var rules)) return null;
        var layers = AiPostLayers(ai,post);
        // A post already going down: wait for it.
        if (layers.Any(l => l.Order is OrderKind.PlaceBuilding or OrderKind.Build) || ConstructionSites.Values.Any(s => s.Team == ai.Team && s.Building == post && !s.Complete))
            return ("laying a War Post",0,rules.OreCost);
        // Training supplies a Slavemaster when there is none (AiTrain).
        if (layers.Length == 0) return ("waiting for a Slavemaster",0,0);
        var bank = Economies[ai.Team];
        if (bank.OreMilli < rules.OreCost*1000L) return ("saving for a War Post",0,rules.OreCost);
        var layer = layers.OrderBy(l => DistanceSquared(l.X,l.Z,x,z)).ThenBy(l => l.Id).First();
        int cell = AiWarPostCell(ai,post,x,z,expansion ? 6000 : 22000,expansion ? 14000 : 34000,away: !expansion);
        if (cell < 0) return null;
        AiOrder(ai,[layer.Id],OrderKind.PlaceBuilding,cell,PlaceableBuildings.IndexOf(post));
        // An order that did not take (no worker route to the spot) must not be tried again.
        if (layer.Order is not (OrderKind.PlaceBuilding or OrderKind.Build)) ai.RefusedCells.Add(cell);
        return ("laying a War Post",0,rules.OreCost);
    }

    /// <summary>The slot of a unit that adds population or lays War Posts (the Slavemaster) the producer should train now: one
    /// when the team has none, and another whenever fewer than six places remain and none is already in training.</summary>
    private int AiSupportSlot(AiPlayer ai,ProductionBuilding producer)
    {
        if (PopulationByUnit.Count == 0) return -1;
        for (int slot = 0; slot < producer.Units.Length; slot++)
        {
            string unit = producer.Units[slot];
            if (!PopulationByUnit.ContainsKey(unit) || AtUnitLimit(ai.Team,unit)) continue;
            bool queued = Producers.Values.Any(p => p.Team == ai.Team && p.Queue.Any(q => q.Unit == unit));
            bool alive = ActiveUnits.Any(u => u.Team == ai.Team && u.Health > 0 && u.Definition.Name == unit);
            int room = PopulationCapacity(ai.Team)-PopulationUsed(ai.Team);
            if (!queued && (!alive || room < 6 && PopulationCapacity(ai.Team) < MaxPopulation))
                return CanTrain(ai.Team,producer.Id,slot,out _) ? slot : -1;
        }
        return -1;
    }
}
