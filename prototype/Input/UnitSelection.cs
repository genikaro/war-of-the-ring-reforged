using Reforged.Simulation;

namespace Reforged.Input;

public static class UnitSelection
{
    public static void Click(HashSet<int> selected,IReadOnlyList<SimUnit> units,SimUnit? hit,bool append,bool sameType)
    {
        selected.RemoveWhere(id => id < 0 || id >= units.Count || units[id].Health <= 0);
        if (hit == null || hit.Health <= 0) { if (!append) selected.Clear(); return; }
        if (!append || selected.Any(id => units[id].Team != hit.Team)) selected.Clear();
        var matches = sameType
            ? units.Where(u => u.Health > 0 && u.Team == hit.Team && u.Definition.Id == hit.Definition.Id).Select(u => u.Id).ToArray()
            : [hit.Id];
        if (append && matches.All(selected.Contains)) selected.ExceptWith(matches);
        else selected.UnionWith(matches);
    }
}
