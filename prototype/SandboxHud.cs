using Godot;

public partial class SandboxHud : Control
{
    public Rect2? SelectionBox;
    public Vector2 Destination;
    public float DestinationTime;
    public override void _Draw()
    {
        if (SelectionBox is { } rect)
        {
            DrawRect(rect, new Color(0.2f, 0.85f, 0.65f, 0.1f));
            DrawRect(rect, new Color("63ddba"), false, 1.5f);
        }
        if (DestinationTime > 0) DrawArc(Destination, 12, 0, Mathf.Tau, 24, new Color("d8b574"), 2);
    }
}
