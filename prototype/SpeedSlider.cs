using Godot;

/// <summary>Notched game-speed slider in the console's gold: click or drag along the track, or turn the mouse
/// wheel over it, to pick one of the fixed speeds. The owner sets Index every frame, so keys and buttons
/// elsewhere move the handle too.</summary>
public partial class SpeedSlider : Control
{
    public event Action<int>? Chosen;
    public string[] Labels { get; set; } = [];
    public int Index { get => _index; set { if (_index != value) { _index = value; QueueRedraw(); } } }
    public bool Paused { get => _paused; set { if (_paused != value) { _paused = value; QueueRedraw(); } } }
    private int _index;
    private bool _paused, _dragging;
    private const float Inset = 12,TrackY = 11;

    public override void _Ready() { MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.None; MouseDefaultCursorShape = CursorShape.PointingHand; }

    private float NotchX(int index) => Labels.Length < 2 ? Size.X/2 : Inset+(Size.X-2*Inset)*index/(Labels.Length-1);
    /// <summary>A notch's position in global coordinates, for scripted clicks.</summary>
    public Vector2 NotchGlobal(int index) => GetGlobalTransform()*new Vector2(NotchX(index),TrackY);
    private int Nearest(float x) => Enumerable.Range(0,Labels.Length).MinBy(i => Math.Abs(NotchX(i)-x));

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            _dragging = button.Pressed;
            if (button.Pressed) Choose(Nearest(button.Position.X));
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && _dragging) { Choose(Nearest(motion.Position.X)); AcceptEvent(); }
        else if (input is InputEventMouseButton { Pressed: true,ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
        {
            Choose(Math.Clamp(Index+(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1),0,Labels.Length-1));
            AcceptEvent();
        }
    }

    private void Choose(int index)
    {
        if (index == Index) return;
        Index = index; Chosen?.Invoke(index);
    }

    public override void _Draw()
    {
        if (Labels.Length == 0) return;
        var gold = _paused ? new Color("9c8a5c") : new Color("f7d16a");
        var edge = new Color("6f6040"); var dark = new Color("1d1910"); var text = new Color("fff3cb");
        float left = NotchX(0),right = NotchX(Labels.Length-1),x = NotchX(Index);
        DrawRect(new Rect2(left-3,TrackY-3,right-left+6,6),dark);
        DrawRect(new Rect2(left,TrackY-1.5f,x-left,3),gold);
        var font = GetThemeDefaultFont();
        for (int i = 0; i < Labels.Length; i++)
        {
            float at = NotchX(i);
            DrawCircle(new(at,TrackY),3.5f,i <= Index ? gold : edge);
            var position = new Vector2(at-20,TrackY+24);
            DrawString(font,position+Vector2.One,Labels[i],HorizontalAlignment.Center,40,12,dark);
            DrawString(font,position,Labels[i],HorizontalAlignment.Center,40,12,i == Index ? gold : text.Darkened(.25f));
        }
        // The handle: a diamond on the current notch.
        Vector2[] diamond = [new(x,TrackY-8),new(x+6,TrackY),new(x,TrackY+8),new(x-6,TrackY)];
        DrawColoredPolygon(diamond,gold);
        DrawPolyline([..diamond,diamond[0]],dark,1.5f,true);
    }
}
