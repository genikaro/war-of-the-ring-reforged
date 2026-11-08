using Godot;
using Reforged.Presentation;

/// <summary>Build-70: every console tooltip shows in one place, the source's parchment box flush with the right edge on top
/// of the console (references/original-game/TOOLTIPS-2026-10-21.md: worker recruitment and Hold Position), as Warcraft
/// shows its tooltips above the command card. It follows the hovered control at once and never the pointer. The text
/// stays each control's TooltipText; the box lays it out as the source does: the first line in the dark title band, a
/// cost line as food, ore, Fate and population cells, then the Description strip and the rest. The console's theme
/// hides Godot's own tooltip popup, which would otherwise open beside the pointer as well.</summary>
public partial class CommandPanel
{
    public const float TooltipWidth = 244;
    // GameConsole.ltx text 8122 is the original counter chart heading (build-184: read from the install).
    private static string AttackTypeChartTitle => GameText.T("tip.weapon_rating");
    private PanelContainer _tip = null!;
    private PanelContainer _tipTitleBand = null!, _tipDescriptionStrip = null!;
    private Label _tipTitle = null!, _tipBody = null!, _tipDescription = null!, _tipTime = null!;
    private HBoxContainer _tipCosts = null!;
    private TextureRect _tipAttackChart = null!;
    private string _tipText = "";
    private static readonly Color TipInk = new("2e2110"),TipLight = new("fff3cb");

    private void CreateTooltip()
    {
        var hidden = new Theme();
        hidden.SetStylebox("panel","TooltipPanel",new StyleBoxEmpty());
        foreach (string color in new[] {"font_color","font_shadow_color","font_outline_color"}) hidden.SetColor(color,"TooltipLabel",Colors.Transparent);
        Theme = hidden;
        _tip = new PanelContainer { Name = "Tooltip",Visible = false,MouseFilter = MouseFilterEnum.Ignore,CustomMinimumSize = new(TooltipWidth,0) };
        var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; rows.AddThemeConstantOverride("separation",4); _tip.AddChild(rows);
        _tipTitleBand = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore }; rows.AddChild(_tipTitleBand);
        _tipTitle = TipLabel(15,TipLight); _tipTitle.HorizontalAlignment = HorizontalAlignment.Center; _tipTitleBand.AddChild(_tipTitle);
        _tipCosts = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; _tipCosts.AddThemeConstantOverride("separation",4); rows.AddChild(_tipCosts);
        // Build-185: the time stands apart from the description, between the cost cells and the Description strip.
        _tipTime = TipLabel(13,TipInk); rows.AddChild(_tipTime);
        _tipDescriptionStrip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore }; rows.AddChild(_tipDescriptionStrip);
        _tipDescription = TipLabel(13,TipInk,GameText.T("tip.description")); _tipDescriptionStrip.AddChild(_tipDescription);
        _tipBody = TipLabel(13,TipInk); _tipBody.AddThemeConstantOverride("line_spacing",-2); rows.AddChild(_tipBody);
        _tipAttackChart = new TextureRect { Name = "AttackTypeChart",Visible = false,MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        rows.AddChild(_tipAttackChart);
        _tip.Resized += PlaceTooltip;
        _content.AddChild(_tip);
    }

    private static Label TipLabel(int size,Color color,string text = "")
    {
        var label = new Label { Text = text,MouseFilter = MouseFilterEnum.Ignore,AutowrapMode = TextServer.AutowrapMode.WordSmart,VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size",size); label.AddThemeColorOverride("font_color",color);
        if (color == TipLight)
        {
            label.AddThemeColorOverride("font_shadow_color",new Color("201b12"));
            label.AddThemeConstantOverride("shadow_offset_x",1); label.AddThemeConstantOverride("shadow_offset_y",1);
        }
        return label;
    }

    /// <summary>A source tooltip piece as a stretchable frame; a plain parchment or recess when the cache predates build-70.</summary>
    private StyleBox TipFrame(string art,Color fallback,float margin,float left,float top,bool tile = false)
    {
        var padding = new[] {left,top,left,top};
        if (_art.GetValueOrDefault(art) is not { } texture)
        {
            var flat = new StyleBoxFlat { BgColor = fallback,BorderColor = fallback.Darkened(.35f) }; flat.SetBorderWidthAll(1);
            flat.ContentMarginLeft = padding[0]; flat.ContentMarginTop = padding[1]; flat.ContentMarginRight = padding[2]; flat.ContentMarginBottom = padding[3];
            return flat;
        }
        var box = new StyleBoxTexture { Texture = texture,AxisStretchVertical = tile ? StyleBoxTexture.AxisStretchMode.Tile : StyleBoxTexture.AxisStretchMode.Stretch };
        box.SetTextureMarginAll(margin);
        box.ContentMarginLeft = padding[0]; box.ContentMarginTop = padding[1]; box.ContentMarginRight = padding[2]; box.ContentMarginBottom = padding[3];
        return box;
    }

    private void LoadTooltipArt()
    {
        _tip.AddThemeStyleboxOverride("panel",TipFrame("console_tooltip_g_right_backing",new Color(EvilSkin ? "27231e" : "b49b6f"),7,10,9,tile: true));
        _tipTitleBand.AddThemeStyleboxOverride("panel",TipFrame("console_tooltip_g_right_title_inset",new Color("6e5e45"),4,6,3));
        _tipDescriptionStrip.AddThemeStyleboxOverride("panel",TipFrame("console_tooltip_g_right_text_separator_outset",new Color(EvilSkin ? "3a342e" : "c7b088"),4,7,1));
        // The Good parchment takes dark ink; Evil's stone needs light text over its textured surface.
        foreach (var label in new[] {_tipDescription,_tipBody,_tipTime})
        {
            label.AddThemeColorOverride("font_color",EvilSkin ? TipLight : TipInk);
            label.AddThemeColorOverride("font_shadow_color",EvilSkin ? new Color("15120f") : Colors.Transparent);
            label.AddThemeColorOverride("font_outline_color",new Color("15120f"));
            label.AddThemeConstantOverride("outline_size",EvilSkin ? 2 : 0);
            label.AddThemeConstantOverride("shadow_offset_x",EvilSkin ? 1 : 0);
            label.AddThemeConstantOverride("shadow_offset_y",EvilSkin ? 1 : 0);
        }
        // Cost cells own their frames too. Rebuild an already open tooltip against the newly selected skin.
        string text = _tipText; _tipText = "";
        if (text != "") ShowTooltip(text);
    }

    /// <summary>"Время: 22 Сек.": a catalog label, the seconds and the original's seconds label (GameConsole 8303), as the
    /// tooltip's time line (HudTip.Timed), which belongs right after the cost line.</summary>
    public static string Seconds(string label,int ticks) => HudTip.Timed(GameText.Labeled(label,SecondsValue(ticks)));
    public static string SecondsValue(int ticks) => $"{GameText.Number(ticks/20m,"0.#")} {GameText.T("tip.seconds")}";

    /// <summary>Above the console's right section, flush with its right edge, growing upward with the text.</summary>
    private void PlaceTooltip() => _tip.Position = new(1024-_tip.Size.X,-_tip.Size.Y);

    private void UpdateTooltip()
    {
        // The deepest control under the pointer that carries a tooltip, or its nearest console ancestor that does (as Godot's own tooltips inherit).
        string text = "";
        for (var control = GetViewport().GuiGetHoveredControl(); control != null && control != this && IsAncestorOf(control); control = control.GetParentControl())
            if (control.IsVisibleInTree() && control.GetTooltip(control.GetLocalMousePosition()) is { Length: > 0 } tip) { text = tip.StripEdges(); break; }
        if (!_tipMirrored) ShowTooltip(text);
        // A wrapped label settles its height after the container lays it out; the box then shrinks to fit.
        if (_tip.Visible && _tip.Size.Y > _tip.GetCombinedMinimumSize().Y+.5f) _tip.Size = new(TooltipWidth,0);
    }

    private bool _tipMirrored;
    /// <summary>An offscreen capture's console shows the live console's tooltip, which no pointer hovers there.</summary>
    public void MirrorTooltip(CommandPanel live) { _tipMirrored = true; ShowTooltip(live.DisplayedTooltip); }

    private void ShowTooltip(string text)
    {
        if (text == _tipText) return;
        _tipText = text; _tip.Visible = text != "";
        if (text == "") return;
        var lines = text.Split('\n').ToList();
        _tipTitle.Text = lines[0]; lines.RemoveAt(0);
        foreach (Node cell in _tipCosts.GetChildren()) { _tipCosts.RemoveChild(cell); cell.QueueFree(); }
        // Build-184: the cost line (HudTip) becomes the source's food, ore, Fate and population cells, in any language.
        if (lines.Count > 0 && HudTip.ParseCosts(lines[0]) is { } costs)
        {
            foreach (var (resource,amount) in costs) _tipCosts.AddChild(CostCell(resource,amount));
            lines.RemoveAt(0);
        }
        _tipCosts.Visible = _tipCosts.GetChildCount() > 0;
        _tipTime.Text = lines.Count > 0 && HudTip.ParseTime(lines[0]) is { } time ? time : "";
        if (_tipTime.Text != "") lines.RemoveAt(0);
        _tipTime.Visible = _tipTime.Text != "";
        _tipBody.Text = string.Join("\n",lines);
        _tipDescriptionStrip.Visible = _tipBody.Visible = lines.Count > 0;
        _tipAttackChart.Visible = text == AttackTypeChartTitle && _tipAttackChart.Texture != null;
        _tip.Size = new(TooltipWidth,0); PlaceTooltip();
    }

    private Control CostCell(string resource,string amount)
    {
        var cell = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore,SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cell.AddThemeConstantOverride("separation",1);
        var iconInset = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        iconInset.AddThemeStyleboxOverride("panel",TipFrame("console_tooltip_g_right_resource_icon_inset",new Color("6e5e45"),3,1,0));
        string icon = resource == "population" ? "console_tooltip_global_icon_population" : $"console_tooltip_g_right_resource_icon_{resource.ToLowerInvariant()}";
        iconInset.AddChild(new TextureRect { Texture = _art.GetValueOrDefault(icon),CustomMinimumSize = new(20,20),MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,Name = resource });
        var valueInset = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore,SizeFlagsHorizontal = SizeFlags.ExpandFill };
        valueInset.AddThemeStyleboxOverride("panel",TipFrame("console_tooltip_g_right_resource_cost_inset",new Color("6e5e45"),3,2,0));
        var value = TipLabel(13,TipLight,amount); value.HorizontalAlignment = HorizontalAlignment.Center; value.AutowrapMode = TextServer.AutowrapMode.Off;
        valueInset.AddChild(value); cell.AddChild(iconInset); cell.AddChild(valueInset);
        return cell;
    }

    public string DisplayedTooltip => _tip.Visible ? _tipText : "";
    public string DisplayedTooltipTitle => _tipTitle.Text;
    public string DisplayedTooltipBody => _tipBody.Visible ? _tipBody.Text : "";
    public string DisplayedTooltipTime => _tipTime.Visible ? _tipTime.Text : "";
    public Color TooltipBodyInk => _tipBody.GetThemeColor("font_color");
    public Color TooltipDescriptionInk => _tipDescription.GetThemeColor("font_color");
    public string[] DisplayedTooltipCosts => [.. _tipCosts.GetChildren().Select(c => c.GetChild(0).GetChild<TextureRect>(0).Name+" "+c.GetChild(1).GetChild<Label>(0).Text)];
    public Rect2 TooltipRect => _tip.GetGlobalRect();
    public bool DefaultTooltipsHidden => Theme?.GetStylebox("panel","TooltipPanel") is StyleBoxEmpty && Theme.GetColor("font_color","TooltipLabel").A == 0;
}
