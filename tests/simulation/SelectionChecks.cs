using Reforged.Input;
using Reforged.Simulation;

static class SelectionChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world = new SimWorld(new(20,2,1000,Enumerable.Repeat(true,40).ToArray(),new int[63]));
        var a = world.Spawn(0,0); var b = world.Spawn(0,2);
        var other = world.Spawn(0,4,UnitDefinition.Probe with { Id = 99 });
        var enemy = world.Spawn(1,6); var dead = world.Spawn(0,8); dead.Health = 0;
        HashSet<int> selected = [];
        void Click(SimUnit? hit,bool shift = false,bool same = false) => UnitSelection.Click(selected,world.Units,hit,shift,same);
        Click(a); check(selected.SetEquals([a.Id]),"plain click replaces selection");
        Click(b,true); check(selected.SetEquals([a.Id,b.Id]),"shift click adds a unit");
        Click(a,true); check(selected.SetEquals([b.Id]),"shift click removes a selected unit");
        Click(other); Click(a,same:true);
        check(selected.SetEquals([a.Id,b.Id]),"same-type selection includes all living matching allies and excludes other types enemies and dead units");
        Click(other); Click(a,true,true);
        check(selected.SetEquals([a.Id,b.Id,other.Id]),"shift same-type selection adds the type to other selected units");
        Click(a,true,true); check(selected.SetEquals([other.Id]),"shift same-type selection removes an entirely selected type");
        Click(a,true); Click(a,true,true);
        check(selected.SetEquals([a.Id,b.Id,other.Id]),"shift same-type selection completes a partially selected type");
        Click(enemy,true); check(selected.SetEquals([enemy.Id]),"selection never mixes friendly and enemy teams");
        Click(enemy,true); check(selected.Count == 0,"shift click also removes an inspected enemy");
        Click(a); Click(null,true); check(selected.SetEquals([a.Id]),"shift empty ground preserves selection");
        Click(null); check(selected.Count == 0,"plain empty ground clears selection");
        selected.UnionWith([a.Id,dead.Id,999]); Click(b,true);
        check(selected.SetEquals([a.Id,b.Id]),"selection prunes dead and invalid members");
    }
}
