using Godot;

/// <summary>Live session information, independent of selection and simulation pause.</summary>
public partial class BuildOverlay : Label
{
    private double _refresh;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        AddThemeColorOverride("font_color",new Color("fff3cb"));
        AddThemeColorOverride("font_shadow_color",Colors.Black);
        AddThemeConstantOverride("shadow_offset_x",1);
        AddThemeConstantOverride("shadow_offset_y",1);
        ResizeOverlay(); GetViewport().SizeChanged += ResizeOverlay;
        Refresh();
    }

    private void ResizeOverlay()
    {
        float scale = Math.Clamp(GetViewportRect().Size.Y/800f,.75f,3);
        OffsetLeft = -360*scale; OffsetRight = -12*scale;
        OffsetTop = 8*scale; OffsetBottom = 36*scale;
        AddThemeFontSizeOverride("font_size",Mathf.RoundToInt(16*scale));
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= ResizeOverlay;

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh < .25) return;
        _refresh = 0; Refresh();
    }

    private void Refresh() => Text = $"{BuildInfo.Label}  ·  {DateTime.Now:yyyy-MM-dd}  ·  {Engine.GetFramesPerSecond():0} FPS";
}
