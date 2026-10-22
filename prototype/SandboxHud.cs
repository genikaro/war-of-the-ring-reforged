using Godot;

public partial class SandboxHud : Control
{
    public Rect2? SelectionBox;
    public Vector2 Destination;
    public float DestinationTime;
    /// <summary>Screen-space legs between queued order points, and pennants where no flag model is loaded.</summary>
    public readonly List<(Vector2 From,Vector2 To,Color Color)> Waypoints = [];
    public readonly List<Vector2> WaypointMarks = [];
    public override void _Draw()
    {
        foreach (var (from,to,color) in Waypoints)
        {
            DrawLine(from,to,new Color(0,0,0,.4f),3.5f);
            DrawDashedLine(from,to,color,2,9);
        }
        foreach (var mark in WaypointMarks)
        {
            var top = mark+new Vector2(0,-18);
            DrawLine(mark,top,new Color("fff3cb"),2);
            DrawColoredPolygon([top,top+new Vector2(11,4),top+new Vector2(0,8)],new Color("7bd65a"));
        }
        if (SelectionBox is { } rect)
        {
            DrawRect(rect, new Color(0.2f, 0.85f, 0.65f, 0.1f));
            DrawRect(rect, new Color("63ddba"), false, 1.5f);
        }
        if (DestinationTime > 0) DrawArc(Destination, 12, 0, Mathf.Tau, 24, new Color("d8b574"), 2);
    }
}
