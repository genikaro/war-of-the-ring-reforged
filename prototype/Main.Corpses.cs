using Godot;
using Reforged.Simulation;

/// <summary>Bodies leave the field a minute after death: they sink into the ground over two seconds, then their
/// model, skeleton, animations and health bar are released. Only a disabled root and empty ring remain for stable
/// view lookup by simulation id. The owner chose
/// 60 s (2026-10-22); the original fades a corpse after WorldVariables.UnitCorpsePersistSeconds, 10 s. The sinking
/// is an adapter for that fade. Presentation only: the simulation keeps the dead unit, whose id is its list index.</summary>
public partial class Main
{
    private const int CorpseTicks = 60*SimWorld.TicksPerSecond,CorpseSinkTicks = 2*SimWorld.TicksPerSecond;
    private const float CorpseSinkDepth = 1.5f;
    private int _corpsesBuried;

    /// <summary>Places a dead unit's body; true once it has gone and needs no further presentation.</summary>
    private bool PresentCorpse(SimUnit unit,UnitView view,double fraction)
    {
        if (view.Buried) return true;
        if (unit.Health > 0) { view.DeathTick = -1; return false; }
        if (view.DeathTick < 0) view.DeathTick = _world!.Tick;
        double age = _world!.Tick-view.DeathTick+fraction-CorpseTicks;
        if (age <= 0) return false;
        if (age < CorpseSinkTicks)
        {
            view.Root.Position = UnitPosition(unit) with { Y = UnitPosition(unit).Y-CorpseSinkDepth*(float)(age/CorpseSinkTicks) };
            return true;
        }
        view.Buried = true; view.Root.Visible = false; view.Ring.Visible = false; view.Shown = view.WillShow = view.RingShown = view.Drawn = false;
        view.Root.ProcessMode = Node.ProcessModeEnum.Disabled;
        view.Player?.Stop();
        RetainUnitTemplates(view);
        foreach (Node child in view.Root.GetChildren())
            if (child != view.Ring) child.QueueFree();
        view.Ring.Mesh = null; view.Ring.MaterialOverride = null;
        view.Player = null; view.Appearance = null; view.Health = null; view.HeroGlow = null; view.Ghost = null; view.Summon = null;
        _corpsesBuried++;
        return true;
    }

    /// <summary>The first unit's visual is also the shared model template. Keep one disabled template per model/team
    /// outside the corpse before releasing its children, so later recruitment and transformations remain usable.</summary>
    private void RetainUnitTemplates(UnitView view)
    {
        foreach (var template in _modelTemplates.Values)
        {
            if (!view.Root.IsAncestorOf(template)) continue;
            if (_modelTemplateStore == null)
            {
                _modelTemplateStore = new Node3D {Name="ModelTemplates",Visible=false,ProcessMode=Node.ProcessModeEnum.Disabled};
                AddChild(_modelTemplateStore);
            }
            template.Reparent(_modelTemplateStore,false);
        }
    }
}
