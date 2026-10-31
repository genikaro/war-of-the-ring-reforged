using Godot;

/// <summary>The main menu (build-147), laid out from the original Screens/Menu_Main.lsr on its 1024 × 768 screen:
/// the game's logo at the top right (CONTROL_MAIN_MENU_WOTR_LOGO, 0.504, -0.026, 0.557 × 0.386), five torn-parchment
/// buttons in a column under it (x 0.649, 0.285 × 0.0925 each, from y 0.358 one every 0.0925, art parchment_button_04,
/// 01, 02, 03, 02), and the gold bullets either side of the button under the pointer (x 0.588 and 0.939, 0.0505 ×
/// 0.026). The original's items were Single Player, Multiplayer, Load, Settings and Exit; here they are Campaign (not
/// built yet, so disabled), Skirmish, Sandbox, Settings and Exit, lettered in the Immortal font. The menu keeps the
/// screen's height and stands at the right edge, leaving the 3D menu scene the rest of a wide window. Exit asks the
/// original question (Menu text 3100) in the original box (console_dialog_quit_acknowledgement_g_box).</summary>
public partial class MainMenu : Control
{
    public event Action? SkirmishChosen,SandboxChosen,SettingsChosen,ExitChosen;

    private static readonly Vector2 ScreenSize = new(1024,768),QuitSize = new(370,249);
    private static readonly Color Ink = new("2a1a0c"),InkHover = new("7b1406"),InkPressed = new("4a0c04"),InkDisabled = new(.165f,.1f,.047f,.38f),
        Engraved = new("e6dcc4"),Highlight = new("ffffff"),Gold = new("f2d27a"),Shadow = new(0,0,0,.85f);
    private static readonly (string Text,string Art,string Tip)[] Items = [
        ("Campaign","parchment_button_04","The Good and Evil campaigns are not built yet."),
        ("Skirmish","parchment_button_01","Battle bots on any of the 32 multiplayer maps."),
        ("Sandbox","parchment_button_02","Try every unit out on an empty field, commanding both players."),
        ("Settings","parchment_button_03",""),
        ("Exit","parchment_button_02","")];
    private const float ButtonX = .6492f*1024,ButtonWidth = .2851f*1024,ButtonHeight = .0925f*768,FirstButton = .3577f*768,BulletWidth = .0505f*1024,BulletHeight = .026f*768;

    private readonly List<(Control Control,Rect2 Rect,bool Centered)> _places = [];
    private readonly List<(Control Control,int Size)> _fonts = [];
    private readonly Button[] _buttons = new Button[Items.Length];
    private readonly (TextureRect Left,TextureRect Right)[] _bullets = new (TextureRect,TextureRect)[Items.Length];
    private Control _quit = null!;
    private Button _quitOk = null!,_quitCancel = null!;

    public Button ItemButton(int index) => _buttons[index];
    public Button CampaignButton => _buttons[0];
    public Button SkirmishButton => _buttons[1];
    public Button SandboxButton => _buttons[2];
    public Button SettingsButton => _buttons[3];
    public Button ExitButton => _buttons[4];
    public Button QuitOkButton => _quitOk;
    public Button QuitCancelButton => _quitCancel;
    public bool QuitOpen => _quit.Visible;
    public bool BulletsShown(int index) => _bullets[index].Left.Visible && _bullets[index].Right.Visible;

    public override void _Ready()
    {
        Name = "MainMenu"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var art = FrontEndArt.Shared;
        Place(new TextureRect { Texture = art.Art("lotr_wotr_logo"),MouseFilter = MouseFilterEnum.Ignore,ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered },new(.5042f*1024,-.026f*768,.5566f*1024,.3861f*768));
        for (int i = 0; i < Items.Length; i++)
        {
            int index = i; float top = FirstButton+i*ButtonHeight;
            var parchment = art.Art(Items[i].Art);
            if (parchment != null) Place(new TextureRect { Texture = parchment,MouseFilter = MouseFilterEnum.Ignore,ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale },new(ButtonX,top,ButtonWidth,ButtonHeight));
            else Place(new ColorRect { Color = new Color("cbb489"),MouseFilter = MouseFilterEnum.Ignore },new(ButtonX+8,top+10,ButtonWidth-16,ButtonHeight-20));
            var button = new Button { Text = Items[i].Text,Flat = true,FocusMode = FocusModeEnum.None,TooltipText = Items[i].Tip,MouseDefaultCursorShape = CursorShape.PointingHand };
            foreach (string style in new[] { "normal","hover","pressed","hover_pressed","disabled","focus" }) button.AddThemeStyleboxOverride(style,new StyleBoxEmpty());
            button.AddThemeColorOverride("font_color",Ink); button.AddThemeColorOverride("font_hover_color",InkHover);
            button.AddThemeColorOverride("font_pressed_color",InkPressed); button.AddThemeColorOverride("font_hover_pressed_color",InkPressed);
            button.AddThemeColorOverride("font_disabled_color",InkDisabled);
            if (art.Immortal(24) is { } font) button.AddThemeFontOverride("font",font);
            // The torn edges leave the middle of the art for the lettering.
            Place(button,new(ButtonX+24,top+12,ButtonWidth-48,ButtonHeight-24)); _fonts.Add((button,28));
            var left = Place(new TextureRect { Texture = art.Art("menu_bullet_left"),Visible = false,MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered },
                new(.5875f*1024,top+.031f*768,BulletWidth,BulletHeight));
            var right = Place(new TextureRect { Texture = art.Art("menu_bullet_right"),Visible = false,MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered },
                new(.9392f*1024,top+.031f*768,BulletWidth,BulletHeight));
            _bullets[i] = (left,right); _buttons[i] = button;
            button.MouseEntered += () => ShowBullets(index,true);
            button.MouseExited += () => ShowBullets(index,false);
        }
        CampaignButton.Disabled = true;
        SkirmishButton.Pressed += () => SkirmishChosen?.Invoke();
        SandboxButton.Pressed += () => SandboxChosen?.Invoke();
        SettingsButton.Pressed += () => SettingsChosen?.Invoke();
        ExitButton.Pressed += () => ShowQuit(true);

        // The Exit question over a dimmed menu, centred on the window.
        _quit = new Control { Visible = false,MouseFilter = MouseFilterEnum.Stop }; AddChild(_quit);
        _quit.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0,0,0,.55f),MouseFilter = MouseFilterEnum.Ignore }; _quit.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var box = new Control { MouseFilter = MouseFilterEnum.Ignore }; _quit.AddChild(box); _places.Add((box,new(Vector2.Zero,QuitSize),true));
        Place(box,new TextureRect { Texture = art.Art("console_dialog_quit_acknowledgement_g_box"),MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.Scale },new(Vector2.Zero,QuitSize));
        var question = new Label { Text = "Confirm exit from the game.",HorizontalAlignment = HorizontalAlignment.Center,VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,MouseFilter = MouseFilterEnum.Ignore };
        question.AddThemeColorOverride("font_color",Highlight); question.AddThemeColorOverride("font_shadow_color",Shadow);
        question.AddThemeConstantOverride("shadow_offset_x",2); question.AddThemeConstantOverride("shadow_offset_y",2);
        if (art.Immortal(20) is { } questionFont) question.AddThemeFontOverride("font",questionFont);
        Place(box,question,new(56,78,258,48)); _fonts.Add((question,20));
        _quitOk = QuitItem(box,"OK",134,() => ExitChosen?.Invoke());
        _quitCancel = QuitItem(box,"Cancel",169,() => ShowQuit(false));
        if (!art.Available)
        {
            var missing = new Label { Text = "Menu art not exported yet: python tools/frontend_assets.py",MouseFilter = MouseFilterEnum.Ignore };
            missing.AddThemeColorOverride("font_color",Engraved); missing.AddThemeColorOverride("font_shadow_color",Shadow);
            Place(missing,new(ButtonX-200,FirstButton+5*ButtonHeight+20,ButtonWidth+220,28)); _fonts.Add((missing,15));
        }
        Resize(); GetViewport().SizeChanged += Resize;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!IsVisibleInTree() || input is not InputEventKey { Pressed: true,Echo: false,Keycode: Key.Escape }) return;
        ShowQuit(!_quit.Visible); GetViewport().SetInputAsHandled();
    }

    public void FadeIn()
    {
        Modulate = new Color(1,1,1,0);
        CreateTween().TweenProperty(this,"modulate:a",1f,.45f);
    }

    public void ShowQuit(bool open) { _quit.Visible = open; foreach (var (left,right) in _bullets) left.Visible = right.Visible = false; }

    private void ShowBullets(int index,bool shown)
    {
        bool lit = shown && !_buttons[index].Disabled && !_quit.Visible;
        _bullets[index].Left.Visible = _bullets[index].Right.Visible = lit;
    }

    /// <summary>An engraved line in the Exit box, as the in-game menu's: white under the pointer.</summary>
    private Button QuitItem(Control box,string text,float top,Action action)
    {
        var button = new Button { Text = text,Flat = true,FocusMode = FocusModeEnum.None };
        foreach (string style in new[] { "normal","hover","pressed","hover_pressed","disabled","focus" }) button.AddThemeStyleboxOverride(style,new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color",Engraved); button.AddThemeColorOverride("font_hover_color",Highlight);
        button.AddThemeColorOverride("font_pressed_color",Gold); button.AddThemeColorOverride("font_hover_pressed_color",Gold);
        button.AddThemeColorOverride("font_shadow_color",Shadow); button.AddThemeConstantOverride("shadow_offset_x",2); button.AddThemeConstantOverride("shadow_offset_y",2);
        if (FrontEndArt.Shared.Immortal(24) is { } font) button.AddThemeFontOverride("font",font);
        button.Pressed += action; _fonts.Add((button,24));
        return Place(box,button,new(56,top,258,34));
    }

    private T Place<T>(T control,Rect2 rect) where T : Control => Place(this,control,rect);

    private T Place<T>(Control parent,T control,Rect2 rect) where T : Control
    {
        parent.AddChild(control); _places.Add((control,rect,false));
        return control;
    }

    /// <summary>The 1024 × 768 layout at the window's height, against its right edge; the Exit box at its centre.</summary>
    private void Resize()
    {
        var viewport = GetViewportRect().Size;
        float scale = Math.Min(viewport.Y/ScreenSize.Y,viewport.X/ScreenSize.X);
        var origin = new Vector2(viewport.X-ScreenSize.X*scale,(viewport.Y-ScreenSize.Y*scale)/2).Floor();
        foreach (var (control,rect,centered) in _places)
        {
            bool top = control.GetParent() == this;
            control.Size = rect.Size*scale;
            control.Position = centered ? ((viewport-rect.Size*scale)/2).Floor() : top ? origin+rect.Position*scale : rect.Position*scale;
        }
        foreach (var (control,size) in _fonts) control.AddThemeFontSizeOverride("font_size",Math.Max(8,Mathf.RoundToInt(size*scale)));
    }
}
