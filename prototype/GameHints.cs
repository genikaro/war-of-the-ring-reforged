using Godot;

/// <summary>Build-224: every Godot tooltip in the game (the main menu, the setup screens, Settings, the in-game menu, the
/// observer's overview) is drawn as the console's own (HudTooltip.cs): the parchment backing, the first line in the dark
/// title band in light letters, the rest in dark ink beneath it, in the console's font. Godot builds a tooltip as a
/// PopupPanel (theme type TooltipPanel) holding one Label (TooltipLabel) and adds it under the hovered control; the scene
/// tree's node_added reaches the panel before it is sized and shown, so its label's text is laid out again there. The
/// console hides Godot's tooltip (its theme's TooltipPanel is empty) and keeps its own box above the command card.</summary>
public static class GameHints
{
    private static readonly Color Ink = new("2e2110"),Light = new("fff3cb"),Shade = new("201b12");
    /// <summary>The widest a hint's text runs before it wraps, in pixels.</summary>
    public const float MaxWidth = 320;
    public const int TitleSize = 16,BodySize = 14;
    private static readonly StringName TooltipPanel = "TooltipPanel",TooltipLabel = "TooltipLabel";
    private static bool _installed;
    private static StyleBox? _backing,_band;

    public static void Install(SceneTree tree)
    {
        if (_installed) return;
        _installed = true;
        tree.NodeAdded += Restyle;
    }

    private static void Restyle(Node node)
    {
        if (node is not PopupPanel panel || panel.ThemeTypeVariation != TooltipPanel) return;
        // The console's hidden tooltip, or a control's own tooltip (make_custom_tooltip): left as they are.
        if (panel.GetThemeStylebox("panel") is StyleBoxEmpty) return;
        if (panel.GetChildren().OfType<Label>().FirstOrDefault(l => l.ThemeTypeVariation == TooltipLabel) is not { } label) return;
        var lines = label.Text.Split('\n');
        // A short first line is the title, in the band (the console's tooltips name their control); a hint that is one
        // sentence is all body text.
        bool titled = lines.Length > 1 || Width(lines[0],TitleSize) <= MaxWidth*.75f;
        if (!titled) lines = ["",..lines];
        panel.AddThemeStyleboxOverride("panel",_backing ?? Frame("console_tooltip_g_right_backing",new Color("b49b6f"),7,10,9,ref _backing,tile: true));
        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; rows.AddThemeConstantOverride("separation",4);
        var band = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        band.AddThemeStyleboxOverride("panel",_band ?? Frame("console_tooltip_g_right_title_inset",new Color("6e5e45"),4,8,3,ref _band));
        var title = Line(lines[0],TitleSize,Light); title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_shadow_color",Shade);
        title.AddThemeConstantOverride("shadow_offset_x",1); title.AddThemeConstantOverride("shadow_offset_y",1);
        band.AddChild(title); if (titled) rows.AddChild(band);
        string text = string.Join("\n",lines.Skip(1)).StripEdges();
        if (text != "")
        {
            var body = Line(text,BodySize,Ink); body.AddThemeConstantOverride("line_spacing",-1);
            // Wrapped at MaxWidth: the label is laid out at that width now, so the panel takes its full height when shown.
            if (Width(text,BodySize) > MaxWidth) { body.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.CustomMinimumSize = new(MaxWidth,0); body.Size = new(MaxWidth,0); }
            rows.AddChild(body);
        }
        label.Text = ""; label.Visible = false;
        panel.AddChild(rows);
    }

    private static float Width(string text,int size) => ThemeDB.FallbackFont.GetMultilineStringSize(text,HorizontalAlignment.Left,-1,size).X;

    private static Label Line(string text,int size,Color color)
    {
        var label = new Label { Text = text,MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font",ThemeDB.FallbackFont); label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",color); label.AddThemeColorOverride("font_shadow_color",Colors.Transparent);
        label.AddThemeColorOverride("font_outline_color",Colors.Transparent);
        return label;
    }

    /// <summary>A console tooltip piece (HudTooltip.TipFrame) from the install's interface art, kept once made; a flat
    /// stand-in until a screen has loaded the menu art (the launcher, before an install is open).</summary>
    private static StyleBox Frame(string art,Color fallback,float margin,float left,float top,ref StyleBox? kept,bool tile = false)
    {
        if (FrontEndArt.Loaded?.Art(art) is not { } texture)
        {
            var flat = new StyleBoxFlat { BgColor = fallback,BorderColor = fallback.Darkened(.35f) }; flat.SetBorderWidthAll(1);
            flat.ContentMarginLeft = flat.ContentMarginRight = left; flat.ContentMarginTop = flat.ContentMarginBottom = top;
            return flat;
        }
        var box = new StyleBoxTexture { Texture = texture,AxisStretchVertical = tile ? StyleBoxTexture.AxisStretchMode.Tile : StyleBoxTexture.AxisStretchMode.Stretch };
        box.SetTextureMarginAll(margin);
        box.ContentMarginLeft = box.ContentMarginRight = left; box.ContentMarginTop = box.ContentMarginBottom = top;
        return kept = box;
    }
}
