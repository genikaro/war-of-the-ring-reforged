using Godot;
using Reforged.Simulation;

/// <summary>Worker work presentation: the source resource states for mining, picking up, carrying and putting down a
/// sack, and the repair (hammer) state while building. Timed phases are sought from the simulation tick, so the
/// pickaxe lands on the tick that adds ore.</summary>
public partial class Main
{
    private readonly Dictionary<string,Dictionary<string,(string State,string Clip)>> _workerClips = [];

    private readonly record struct GatherAnimation(string State,string Clip,int Ticks,int Elapsed);

    /// <summary>The clip for a worker's building, gathering phase or carried sack; null when ordinary animation applies.</summary>
    private GatherAnimation? WorkerAnimation(SimUnit unit)
    {
        if (unit.Health <= 0 || unit.StrikeActive || unit.Casting || unit.Target >= 0
            || !_workerClips.TryGetValue(unit.Definition.Name,out var clips) || !_world!.Workers.TryGetValue(unit.Definition.Name,out var worker)) return null;
        string kind = unit.CargoKind == ResourceKind.Ore ? "ore" : "food";
        int elapsed = _world.Tick-1-unit.GatherPhaseTick;
        var (key,ticks) = _world.IsBuilding(unit) ? ("build",0) : unit.GatherPhase switch {
            GatherPhase.Mining => ("mine",worker.MineTicks),
            GatherPhase.PickingUp => ("ore_pick_up",worker.PickUpTicks),
            GatherPhase.PuttingDown => (kind+"_put_down",worker.PutDownTicks),
            _ => unit.CargoMilli > 0 ? (kind+(unit.Moving ? "_walk" : "_idle"),0) : ("",0) };
        if (key == "" || !clips.TryGetValue(key,out var clip)) return null;
        // Mining loops: one sim cycle per clip, so each hit stays on its tick.
        if (key == "mine") elapsed %= ticks;
        return new(clip.State,clip.Clip,ticks,elapsed);
    }

    /// <summary>Building faces the foundation, mining and picking up the source, putting down the nearest owned depot.</summary>
    private float? WorkFacing(SimUnit unit)
    {
        (int X,int Z)? target = _world!.IsBuilding(unit) ? _world.SitePosition(_world.ConstructionSites[unit.OrderTarget]) : unit.GatherPhase switch {
            GatherPhase.Mining or GatherPhase.PickingUp when _world!.Resources.TryGetValue(unit.OrderTarget,out var source) => (source.X,source.Z),
            GatherPhase.PuttingDown => _world!.DropOffs.Where(d => d.Team == unit.Team)
                .OrderBy(d => SimWorld.DistanceSquared(unit.X,unit.Z,d.X,d.Z)).Select(d => ((int,int)?)(d.X,d.Z)).FirstOrDefault(),
            _ => null };
        return target is var (x,z) ? Mathf.Atan2(-(x-unit.X),z-unit.Z) : null;
    }

    /// <summary>Seek a timed phase to the interpolated simulation time; returns false for freely looping clips.</summary>
    private bool SeekGather(SimUnit unit,UnitView view,double fraction)
    {
        if (view.Player == null || WorkerAnimation(unit) is not { Ticks: > 0 } animation) return false;
        double ticks = Math.Clamp(animation.Elapsed+fraction,0,animation.Ticks);
        view.Player.Seek(ticks/animation.Ticks*view.Player.CurrentAnimationLength,true);
        view.Appearance?.Apply(animation.Clip,ticks/animation.Ticks,animation.State);
        return true;
    }
}
