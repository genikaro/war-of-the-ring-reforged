using Godot;
using Reforged.Simulation;

/// <summary>Build-158: unit presentation does work only for what can be seen. A unit is drawn when it is visible to
/// the viewing side and inside the camera's view, widened by a margin for this frame's camera move and by the reach of
/// its shadow, so an off-screen unit whose shadow falls on screen still counts. Godot animates drawn units as before;
/// a unit not drawn has its AnimationPlayer switched to manual mode and banks the time it misses, applied before any
/// Play, Seek or speed change and when it comes back into view, so it reappears in the pose it would have had. Appearance, seeks, health bars and
/// selection-ring rebuilds are skipped while a unit is not drawn. The presentation loops visit only units whose
/// bodies are still on the field (living ones and unburied corpses), not the whole casualty history.</summary>
public partial class Main
{
    /// <summary>Ids of views that are not buried, ascending: the units the presentation loops visit.</summary>
    private readonly List<int> _presented = [];
    private bool _presentedChanged;
    private readonly Plane[] _viewPlanes = new Plane[6];
    private float _viewMargin;
    private Vector3 _shadowPerMeter;
    /// <summary>The interpolation fraction loop A posed units at last; a unit coming into view while paused uses it.</summary>
    private double _lastPoseFraction = 1;
    /// <summary>Clip name to animation name, and model state to clip, per model file (all its units share them).</summary>
    private readonly Dictionary<string,Dictionary<string,string?>> _animationNames = [],_stateClips = [];

    private void AddPresented(int id)
    {
        int index = _presented.BinarySearch(id);
        if (index < 0) _presented.Insert(~index,id);
    }

    private void PrunePresented()
    {
        if (!_presentedChanged) return;
        _presented.RemoveAll(id => _views[id].Buried);
        _presentedChanged = false;
    }

    /// <summary>The camera's view volume for this frame, widened by a margin that covers this frame's pan and a
    /// zoom step (the camera moves after the unit loops), and the ground offset of a shadow per meter of height.</summary>
    private void PrepareViewCulling()
    {
        var planes = _camera.GetFrustum();
        // A point on the ray through the screen's centre is inside every plane (the camera's vertical offset moves
        // the view off its own axis, so a point straight ahead of the camera need not be).
        var inside = _camera.ProjectPosition(GetViewport().GetVisibleRect().GetCenter(),_camera.Near+10);
        for (int i = 0; i < 6; i++)
        {
            var plane = planes[i];
            // Keep the convention "outside is positive distance", whichever way Godot orients them.
            _viewPlanes[i] = plane.DistanceTo(inside) > 0 ? -plane : plane;
        }
        _viewMargin = .3f*_distance+2;
        var sun = _lighting.SunDirection.Normalized();
        var flat = new Vector3(sun.X,0,sun.Z);
        float down = -sun.Y;
        _shadowPerMeter = down > .05f ? flat/down : flat.Normalized()*20;
        if (_shadowPerMeter.Length() > 20) _shadowPerMeter = _shadowPerMeter.Normalized()*20;
    }

    private bool SphereInView(Vector3 center,float radius)
    {
        foreach (var plane in _viewPlanes) if (plane.DistanceTo(center) > radius) return false;
        return true;
    }

    /// <summary>Whether the unit's body or the shadow it casts can be on screen this frame.</summary>
    private bool InView(Vector3 position,SimUnit unit)
    {
        float height = Math.Max(2f,unit.Definition.BodyHeightMm/1000f*1.5f*unit.Definition.ModelScalePpm/1_000_000f);
        float radius = height+_viewMargin;
        var body = position+Vector3.Up*(height/2);
        if (SphereInView(body,radius)) return true;
        var tip = position+_shadowPerMeter*height;
        return SphereInView(tip,radius) || SphereInView((body+tip)/2,radius);
    }

    /// <summary>Decides whether this unit is drawn this frame and whether its root will show. A unit coming into
    /// view first gets the animation time it missed.</summary>
    private bool UpdateDrawn(SimUnit unit,UnitView view)
    {
        bool was = view.Drawn;
        view.WillShow = unit.InsideSite < 0 && _world!.CanSeeUnit(ViewTeam,unit) && !view.Exploded;
        view.Drawn = view.WillShow && InView(view.Root.Position,unit);
        if (view.Drawn == was || view.Player == null) return view.Drawn && !was;
        if (!view.Drawn)
        {
            // Godot skips a player that stops processing during this pass; Main banks the time from here on.
            view.Player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            view.Resuming = false;
        }
        else
        {
            // Godot left this player out of the frame's node list: Main gives it the banked time now and this
            // frame's step at the end of the frame, and hands it back to Godot from the next frame.
            FlushAnimation(view);
            view.Resuming = true;
        }
        return view.Drawn && !was;
    }

    /// <summary>The pose and pieces for this frame: the simulation-timed clips seek to the interpolated time, the
    /// others show the pieces for where their clip has got to. A unit just come into view first gets the appearance
    /// the physics frame gives every drawn unit.</summary>
    private void PresentPose(SimUnit unit,UnitView view,double fraction,bool appeared)
    {
        if (appeared && !unit.StrikeActive && !unit.Casting) view.Appearance?.Apply(view.AppearanceClip,0,view.State);
        SeekAttack(unit,view,fraction);
        SeekAbility(unit,view,fraction);
        SeekNature(unit,view,fraction);
        if (view.Summon != null) PoseSummon(unit,view,fraction);
        if (!SeekGather(unit,view,fraction) && !unit.StrikeActive && !unit.Casting && view.Player != null && !view.Player.CurrentAnimation.IsEmpty && view.Player.CurrentAnimationLength > 0)
            view.Appearance?.Apply(view.Clip,view.Player.CurrentAnimationPosition/view.Player.CurrentAnimationLength,view.State);
    }

    /// <summary>Applies the time a unit not drawn has banked, at the speed it was banked with.</summary>
    private static void FlushAnimation(UnitView view)
    {
        if (view.PendingAdvance <= 0 || view.Player == null) return;
        view.Player.Advance(view.PendingAdvance);
        view.PendingAdvance = 0;
    }

    private static void SetAnimationSpeed(UnitView view,float speed)
    {
        if (view.Player == null || view.SpeedScale == speed) return;
        FlushAnimation(view);
        view.Player.SpeedScale = speed; view.SpeedScale = speed;
    }

    /// <summary>Banks this frame's time for units not drawn, and steps a unit that came back into view this frame
    /// (Godot processes it again from the next frame). Runs last in Main's frame.</summary>
    private void AdvanceUnitAnimations(double delta)
    {
        foreach (int id in _presented)
        {
            var view = _views[id];
            if (view.Buried || view.Player == null) continue;
            if (view.Resuming)
            {
                view.Player.Advance(delta); view.Resuming = false;
                view.Player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Idle;
            }
            else if (!view.Drawn) view.PendingAdvance += delta;
        }
    }

    /// <summary>The animation a clip plays on this unit's model (clips may sit in a library: "library/clip").</summary>
    private string? AnimationName(UnitView view,string clip)
    {
        if (view.Player == null) return null;
        if (!view.AnimationNames.TryGetValue(clip,out var name))
            view.AnimationNames[clip] = name = view.Player.GetAnimationList().FirstOrDefault(a => a == clip || a.EndsWith('/'+clip));
        return name;
    }

    /// <summary>The clip the model's data names for a state (idle, death, walk...).</summary>
    private static string StateClip(UnitView view,string state)
    {
        if (!view.StateClips.TryGetValue(state,out var clip)) view.StateClips[state] = clip = view.Model.GetProperty(state).GetString();
        return clip ?? "";
    }
}
