namespace Reforged.Simulation;

public sealed partial class SimWorld
{
    private static void ResetCombatTraffic(SimUnit unit)
    {
        unit.CombatMotionTick=-1; unit.CombatMotionTravel=0;
        unit.CombatTrafficQueued=false; unit.CombatTrafficPauseUntil=-1;
    }

    private void MonitorCombatTraffic()
    {
        foreach(var unit in Units)
        {
            if (!unit.Present || unit.Target<0 || unit.Attacking || HoldsPosition(unit)
                || unit.Definition.BodyRadiusMm<=350 || unit.Node>=Navigation.GroundCount
                || unit.TransitNode>=0 && !unit.GroundSegment || unit.Order is OrderKind.Move or OrderKind.Gather or OrderKind.Build
                    or OrderKind.PlaceBuilding or OrderKind.AttackStructure or OrderKind.UseAbility)
            { ResetCombatTraffic(unit); continue; }
            var first=unit.Movement[0];
            unit.CombatMotionTravel+=IntegerMotion.Length(unit.X-first.X,0,unit.Z-first.Z)/1000;
            if (unit.CombatMotionTick>=0 && Tick-unit.CombatMotionTick<TicksPerSecond) continue;
            if (unit.CombatMotionTick>=0)
            {
                long stride=Math.Max(1,unit.MovementBudget/1000);
                long net=DistanceSquared(unit.CombatMotionX,unit.CombatMotionZ,unit.X,unit.Z);
                if (unit.CombatMotionTravel>=stride*6 && net<stride*stride*9)
                {
                    // A second of walking inside three strides is traffic oscillation, not a useful flank.
                    // Rest briefly and require forward local steps until a route makes real progress again.
                    unit.CombatTrafficQueued=true; unit.CombatTrafficPauseUntil=Tick+6;
                }
                else if (net>=stride*stride*9) unit.CombatTrafficQueued=false;
            }
            unit.CombatMotionTick=Tick; unit.CombatMotionX=unit.X; unit.CombatMotionZ=unit.Z; unit.CombatMotionTravel=0;
        }
    }
}
