using Reforged.Simulation;

public partial class Main
{
    private bool TryOwnStructure(int obstacle,bool queued)
    {
        if (!_skirmish || _orders.Pending != null) return false;
        var site = _world!.ConstructionSites.Values.FirstOrDefault(s => s.Obstacle == obstacle && s.Team == PlayerTeam && s.Health > 0);
        if (site == null) return false;
        var units = _world.Units.Where(u => u.Team == PlayerTeam && u.Present && _selected.Contains(u.Id)).ToArray();
        var actions = new List<string>();
        var assigned = new HashSet<int>();
        void Send(OrderKind kind,IEnumerable<SimUnit> group,int target,string action)
        {
            int[] ids = group.Where(u => !assigned.Contains(u.Id)).Select(u => u.Id).ToArray();
            if (ids.Length == 0) return;
            _world.Enqueue(PlayerTeam,ids,kind,target: target,queued: queued); assigned.UnionWith(ids); actions.Add(action);
        }
        var workers = units.Where(u => _world.Workers.ContainsKey(u.Definition.Name)).ToArray();
        if (_world.OwnedDropOff(PlayerTeam,obstacle) != null)
            Send(OrderKind.ReturnCargo,workers.Where(u => u.CargoMilli > 0),obstacle,"Return resources");
        if (!site.Complete || _world.NeedsRepair(site))
            Send(OrderKind.Build,workers,site.Id,site.Complete ? "Repair" : "Resume construction");
        else if (_structureViews[obstacle].Resource is int resource and >= 0)
            Send(OrderKind.Gather,workers.Where(u => _world.CanGather(PlayerTeam,resource) || u.CargoMilli > 0),resource,"Gather");
        // Build-173: units that may garrison a finished tower of their own go inside (as many as it has room for).
        int room = _world.GarrisonCapacityOf(site)-_world.GarrisonCount(site);
        if (site.Complete && room > 0)
            Send(OrderKind.ApproachStructure,units.Where(u => u.Definition.CanGarrison && !assigned.Contains(u.Id)).Take(room),site.Id,"Garrison");
        Send(OrderKind.ApproachStructure,units,site.Id,"Move to");
        if (assigned.Count == 0) return false;
        ShowStructureCommandFeedback(obstacle);
        _status = string.Join(" / ",actions)+" "+_world.SiteRules(site).DisplayName+(_paused ? " (paused)" : "");
        return true;
    }
}
