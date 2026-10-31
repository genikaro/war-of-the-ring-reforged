using Godot;

/// <summary>Live session information, independent of selection and simulation pause.</summary>
public partial class BuildOverlay : Label
{
    private const float LineHeight = 30;
    private double _refresh;

    /// <summary>The row at the top right, 0 at the top. In a match the debug button holds row 0 and this line row 1 (build-96).</summary>
    public int Line { get; init; }

    /// <summary>The top-right HUD's scale for a window: 1 at 800 pixels high.</summary>
    public static float HudScale(Vector2 viewport) => Math.Clamp(viewport.Y/800f,.75f,3);

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
        float scale = HudScale(GetViewportRect().Size);
        OffsetLeft = -360*scale; OffsetRight = -12*scale;
        OffsetTop = (8+LineHeight*Line)*scale; OffsetBottom = (36+LineHeight*Line)*scale;
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

/// <summary>The debug tools' own button at the top right, above the build line (build-96); it stays lit while the
/// tools are open. The console's Menu button opens the game menu instead.</summary>
public partial class DebugToggle : Button
{
    public override void _Ready()
    {
        Text = "Debug · F4"; ToggleMode = true; FocusMode = FocusModeEnum.None;
        var gold = new Color("d8b574");
        foreach (var (state,color) in new[] { ("normal",new Color(.035f,.055f,.07f,.85f)),("hover",new Color(.07f,.1f,.12f,.95f)),
            ("pressed",new Color(.2f,.16f,.07f,.95f)),("hover_pressed",new Color(.26f,.21f,.09f,.95f)) })
            AddThemeStyleboxOverride(state,new StyleBoxFlat { BgColor = color,BorderColor = gold,
                BorderWidthLeft = 1,BorderWidthRight = 1,BorderWidthTop = 1,BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4,CornerRadiusTopRight = 4,CornerRadiusBottomLeft = 4,CornerRadiusBottomRight = 4,
                ContentMarginLeft = 8,ContentMarginRight = 8 });
        AddThemeColorOverride("font_color",new Color("fff3cb")); AddThemeColorOverride("font_hover_color",Colors.White);
        AddThemeColorOverride("font_pressed_color",gold); AddThemeColorOverride("font_hover_pressed_color",gold);
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        ResizeToggle(); GetViewport().SizeChanged += ResizeToggle;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= ResizeToggle;

    private void ResizeToggle()
    {
        float scale = BuildOverlay.HudScale(GetViewportRect().Size);
        OffsetLeft = -132*scale; OffsetRight = -12*scale; OffsetTop = 8*scale; OffsetBottom = 34*scale;
        AddThemeFontSizeOverride("font_size",Mathf.RoundToInt(14*scale));
    }
}
