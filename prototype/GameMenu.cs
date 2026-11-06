using Godot;

/// <summary>The in-game menu behind the console's Menu button (build-96), after the original InGame_MainMenu,
/// InGame_Options and Quit_Acknowledgement screens. The Good menu frame (background_good_mainmenu) over a dimmed
/// battlefield lists the original's Settings (Menu text 3), Exit (8006) and Resume (Continue, 4803) in the original
/// order, under the title Main Menu (4). Settings opens the original Game Options frame (Game_Settings_Screen_G_BG)
/// with the tabs Game (1600), Controls (1700) and Reforged, and the original Reset (9028), Apply (9029), Cancel (9000)
/// and OK (9015): changes wait for Apply or OK, Cancel drops them. Exit asks the original question: Exit to Main Menu
/// (9036), Exit to Windows (9037) or Cancel (9000). The original's Load, Save, Technologies, Objectives and Restart
/// are not built yet. English labels and Godot's default font stand in for the Russian text and the Immortal font.
/// Layout is in the art's own pixels, scaled with the window as the original scales its 1024x768 screens.</summary>
public partial class GameMenu : Control
{
    public event Action? ResumeChosen,ExitToMainMenuChosen,ExitToWindowsChosen;
    /// <summary>Apply or OK on the Settings page: the new settings and the interface size in percent.</summary>
    public event Action<GameSettings,double>? SettingsApplied;
    public event Action<GameSettings>? AudioPreviewed;
    public event Action? VoicePreviewChosen;
    /// <summary>Build-147: the main menu's Settings left by OK, Cancel or Esc.</summary>
    public event Action? SettingsClosed;
    /// <summary>The key and mouse reference on the Controls tab.</summary>
    public string ControlsText { get; set; } = "";

    private enum Page { Main,Settings,Exit }
    private static readonly Color Engraved = new("e6dcc4"),Highlight = new("ffffff"),Dimmed = new("8f8268"),
        Ink = new("1c140b"),InkSoft = new("33281a"),InkDim = new("5a4a32"),Shadow = new(0,0,0,.85f),Gold = new("f2d27a");
    // Art sizes: the menu frame, the Settings frame cropped to its opaque part, and the Exit box.
    private static readonly Vector2 MainSize = new(371,512),SettingsSize = new(1021,555),ExitSize = new(370,249);

    private readonly List<(Control Control,Rect2 Rect)> _places = [];
    private readonly List<(Control Control,int Size)> _fonts = [];
    private readonly List<(Control Page,Vector2 Size)> _pages = [];
    private readonly Dictionary<TextureRect,Panel> _fallbacks = [];
    private readonly ButtonGroup _tabGroup = new(),_editionGroup = new(),_cursorSizeGroup = new();
    private Control _main = null!,_settings = null!,_exit = null!,_gameTab = null!,_controlsTab = null!,_reforgedTab = null!,_audioTab = null!;
    private TextureRect _mainFrame = null!,_settingsFrame = null!,_exitFrame = null!,_capLeft = null!,_capRight = null!;
    private Button _settingsButton = null!,_exitButton = null!,_resumeButton = null!,_exitToMain = null!,_exitToWindows = null!,_exitCancel = null!;
    private Button _reset = null!,_apply = null!,_cancel = null!,_ok = null!;
    private readonly Button[] _tabs = new Button[4];
    private readonly Dictionary<int,Button> _cursorSizes = [];
    private readonly HSlider[] _volumes = new HSlider[4];
    private readonly Label[] _volumeValues = new Label[4];
    private CheckBox _mute = null!;
    private Button _testVoice = null!;
    private CheckBox _playerBars = null!,_overheadBars = null!,_blood = null!;
    private readonly CheckBox[] _editions = new CheckBox[GameSettings.Editions.Length];
    private readonly Dictionary<string,CheckBox> _options = [];
    private HSlider _interfaceScale = null!;
    private Label _scaleValue = null!,_controls = null!;
    private Page _page;
    private float _scale = 1;
    private bool _artApplied,_settingsOnly;
    private GameSettings _current = new(),_draft = new();
    private double _currentScale = 100,_draftScale = 100;

    public Button ResumeButton => _resumeButton;
    public Button SettingsButton => _settingsButton;
    public Button ExitButton => _exitButton;
    public Button ExitToMainMenuButton => _exitToMain;
    public Button ApplyButton => _apply;
    public Button OkButton => _ok;
    public Button TabButton(int tab) => _tabs[tab];
    public Button CursorSizeButton(int percent) => _cursorSizes[percent];
    public HSlider VolumeSlider(int channel) => _volumes[channel];
    public CheckBox MuteBox => _mute;
    public Button VoicePreviewButton => _testVoice;
    public Button CancelButton => _cancel;
    public Button ResetButton => _reset;
    public CheckBox PlayerBarsBox => _playerBars;
    public CheckBox OverheadBarsBox => _overheadBars;
    public CheckBox BloodBox => _blood;
    public CheckBox EditionBox(GameSettings.Edition edition) => _editions[(int)edition];
    public bool SettingsOpen => Visible && _page == Page.Settings;
    public bool ExitOpen => Visible && _page == Page.Exit;

    public override void _Ready()
    {
        Name = "GameMenu"; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0,0,0,.5f),MouseFilter = MouseFilterEnum.Ignore };
        AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _main = NewPage(MainSize); _mainFrame = Frame(_main,MainSize);
        Place(_main,Caption("Main Menu",17,Highlight,HorizontalAlignment.Center),88,84,195,26);
        _settingsButton = Item(_main,"Settings",228,() => ShowPage(Page.Settings));
        _exitButton = Item(_main,"Exit",265,() => ShowPage(Page.Exit));
        _resumeButton = Item(_main,"Resume",302,() => ResumeChosen?.Invoke());

        _exit = NewPage(ExitSize); _exitFrame = Frame(_exit,ExitSize);
        _exitToMain = Item(_exit,"Exit to Main Menu",99,() => ExitToMainMenuChosen?.Invoke(),56,258);
        _exitToWindows = Item(_exit,"Exit to Windows",134,() => ExitToWindowsChosen?.Invoke(),56,258);
        _exitCancel = Item(_exit,"Cancel",169,() => ShowPage(Page.Main),56,258);

        _settings = NewPage(SettingsSize); _settingsFrame = Frame(_settings,SettingsSize);
        Place(_settings,Caption("Settings",20,Highlight,HorizontalAlignment.Center),360,70,301,34);
        string[] tabs = ["Game","Controls","Reforged","Audio"];
        for (int tab = 0; tab < tabs.Length; tab++)
        {
            int index = tab;
            _tabs[tab] = BoxButton(tabs[tab],() => ShowTab(index)); _tabs[tab].ToggleMode = true; _tabs[tab].ButtonGroup = _tabGroup;
            Place(_settings,_tabs[tab],132+tab*142,124,132,32);
        }
        _gameTab = Place(_settings,new Control { MouseFilter = MouseFilterEnum.Ignore },132,170,757,266);
        _controlsTab = Place(_settings,new Control { MouseFilter = MouseFilterEnum.Ignore },132,170,757,266);
        _reforgedTab = Place(_settings,new Control { MouseFilter = MouseFilterEnum.Ignore },132,170,757,266);
        _audioTab = Place(_settings,new Control { MouseFilter = MouseFilterEnum.Ignore },132,170,757,266);
        _reset = Place(_settings,BoxButton("Reset",ResetDraft),132,448,130,34);
        _apply = Place(_settings,BoxButton("Apply",ApplyDraft),475,448,130,34);
        _cancel = Place(_settings,BoxButton("Cancel",() => ShowPage(Page.Main)),617,448,130,34);
        _ok = Place(_settings,BoxButton("OK",() => { ApplyDraft(); ShowPage(Page.Main); }),759,448,130,34);

        Place(_gameTab,Caption("Health bars",17,Ink),0,0,360,26);
        _playerBars = Place(_gameTab,Check("Player-colored health bars","Use each player's map color for health bars over units and buildings.",
            on => _draft.PlayerColoredBars = on),0,32,360,34);
        _overheadBars = Place(_gameTab,Check("Overhead health bars","Place damaged units' and buildings' health bars above their models.",
            on => _draft.OverheadBars = on),0,70,360,34);
        Place(_gameTab,Caption("Violence",17,Ink),0,124,360,26);
        _blood = Place(_gameTab,Check("Blood","Show blood when units are hit or killed, blood pools and bloodied weapons.",
            on => _draft.Blood = on),0,156,360,34);
        Place(_gameTab,Caption("Interface size",17,Ink),400,0,357,26);
        _capLeft = Place(_gameTab,new TextureRect { MouseFilter = MouseFilterEnum.Ignore,StretchMode = TextureRect.StretchModeEnum.KeepCentered },400,38,14,24);
        _interfaceScale = Place(_gameTab,new HSlider { MinValue = 75,MaxValue = 150,Step = 5,Value = 100,FocusMode = FocusModeEnum.None },416,36,260,28);
        _capRight = Place(_gameTab,new TextureRect { MouseFilter = MouseFilterEnum.Ignore,StretchMode = TextureRect.StretchModeEnum.KeepCentered },678,38,14,24);
        _scaleValue = Place(_gameTab,Caption("100%",15,Ink),700,36,57,28);
        _interfaceScale.ValueChanged += value => { _draftScale = value; _scaleValue.Text = $"{value:0}%"; };
        Place(_gameTab,Caption("Cursor size",17,Ink),400,86,357,26);
        foreach (int percent in GameSettings.CursorSizes)
        {
            var button = BoxButton($"{percent}%",() => _draft.CursorSizePercent = percent);
            button.ToggleMode = true; button.ButtonGroup = _cursorSizeGroup;
            _cursorSizes.Add(percent,Place(_gameTab,button,400+_cursorSizes.Count*100,118,90,32));
        }

        string[] channels = ["Master volume","Music","Sound effects","Unit speech"];
        for (int i=0;i<channels.Length;i++)
        {
            int channel = i;
            Place(_audioTab,Caption(channels[i],16,Ink),0,i*46,195,30);
            _volumes[i] = Place(_audioTab,new HSlider { MinValue = 0,MaxValue = 100,Step = 1,FocusMode = FocusModeEnum.None },210,i*46,390,28);
            _volumeValues[i] = Place(_audioTab,Caption("",15,Ink),620,i*46,65,28);
            _volumes[i].ValueChanged += value => {
                switch (channel) { case 0: _draft.MasterVolume = value; break; case 1: _draft.MusicVolume = value; break;
                    case 2: _draft.EffectsVolume = value; break; case 3: _draft.VoiceVolume = value; break; }
                _volumeValues[channel].Text = $"{value:0}%"; AudioPreviewed?.Invoke(_draft.Copy());
            };
        }
        _mute = Place(_audioTab,Check("Mute all audio","Silence every audio channel without changing its volume.",
            on => { _draft.Muted = on; AudioPreviewed?.Invoke(_draft.Copy()); }),0,188,330,34);
        _testVoice = Place(_audioTab,BoxButton("Test unit speech",() => VoicePreviewChosen?.Invoke()),470,188,215,34);
        Place(_audioTab,Caption("Volume changes preview immediately. Apply or OK saves; Cancel restores the saved mix.",13,InkSoft,wrap: true),0,234,757,30);

        var scroll = Place(_controlsTab,new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled },0,0,757,266);
        _controls = Caption("",14,Ink,wrap: true); _controls.SizeFlagsHorizontal = SizeFlags.ExpandFill; scroll.AddChild(_controls);

        Place(_reforgedTab,Caption("Edition",17,Ink),0,0,360,26);
        for (int i = 0; i < GameSettings.Editions.Length; i++)
        {
            var (edition,name,description) = GameSettings.Editions[i];
            _editions[i] = Place(_reforgedTab,Check(name,"",on => { if (on) { _draft.ReforgedEdition = edition; RefreshSettings(); } }),0,32+i*66,360,30);
            _editions[i].ButtonGroup = _editionGroup;
            Place(_reforgedTab,Caption(description,13,InkSoft,wrap: true),58,60+i*66,300,36);
        }
        Place(_reforgedTab,Caption("Improvements",17,Ink),400,0,357,26);
        if (GameSettings.ReforgedOptions.Length == 0) Place(_reforgedTab,Caption("No Reforged improvements yet.",14,InkSoft),400,36,357,26);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Place(_reforgedTab,new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled },400,32,357,234).AddChild(list);
        foreach (var option in GameSettings.ReforgedOptions)
        {
            string key = option.Key;
            var box = Check(option.Name,option.Description,on => { if (on) _draft.CustomOn.Add(key); else _draft.CustomOn.Remove(key); });
            _options[key] = box; list.AddChild(box);
        }
        ShowPage(Page.Main); Visible = false;
        Resize(); GetViewport().SizeChanged += Resize;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;

    /// <summary>Opens on the Main Menu page with the settings in force; the original art is applied on first use.</summary>
    public void Open(Func<string,Texture2D?> art,GameSettings settings,double interfaceScale,bool canExitToMainMenu)
    {
        if (!_artApplied) { ApplyArt(art); _artApplied = true; }
        _current = settings.Copy(); _currentScale = interfaceScale; _controls.Text = ControlsText;
        _exitToMain.Disabled = !canExitToMainMenu;
        _exitToMain.TooltipText = canExitToMainMenu ? "" : "This match did not start from the setup screen.";
        _settingsOnly = false;
        ShowPage(Page.Main); Visible = true;
    }

    /// <summary>Build-147: the Settings page alone, for the main menu; leaving it closes the menu.</summary>
    public void OpenSettings(Func<string,Texture2D?> art,GameSettings settings,double interfaceScale)
    {
        if (!_artApplied) { ApplyArt(art); _artApplied = true; }
        _current = settings.Copy(); _currentScale = interfaceScale; _controls.Text = ControlsText;
        _page = Page.Main; _settingsOnly = true;
        ShowPage(Page.Settings); Visible = true;
    }

    public void Close() { AudioPreviewed?.Invoke(_current.Copy()); Visible = false; }

    /// <summary>Esc: Settings drops its changes, Exit returns to the Main Menu page, and the Main Menu resumes.</summary>
    public void Back()
    {
        if (_page == Page.Main) ResumeChosen?.Invoke();
        else ShowPage(Page.Main);
    }

    public void ShowSettings() => ShowPage(Page.Settings);

    /// <summary>In a match Main routes Esc here; on the main menu's Settings the page takes it itself.</summary>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!_settingsOnly || !Visible || input is not InputEventKey { Pressed: true,Echo: false,Keycode: Key.Escape }) return;
        Back(); GetViewport().SetInputAsHandled();
    }

    private void ShowPage(Page page)
    {
        if (_page == Page.Settings && page != Page.Settings) AudioPreviewed?.Invoke(_current.Copy());
        if (_settingsOnly && page != Page.Settings) { _settingsOnly = false; _page = Page.Main; Visible = false; SettingsClosed?.Invoke(); return; }
        _page = page;
        _main.Visible = page == Page.Main; _settings.Visible = page == Page.Settings; _exit.Visible = page == Page.Exit;
        if (page != Page.Settings) return;
        _draft = _current.Copy(); _draftScale = _currentScale;
        RefreshSettings(); ShowTab(0);
    }

    private void ShowTab(int tab)
    {
        _tabs[tab].SetPressedNoSignal(true);
        _gameTab.Visible = tab == 0; _controlsTab.Visible = tab == 1; _reforgedTab.Visible = tab == 2;
        _audioTab.Visible = tab == 3;
    }

    private void RefreshSettings()
    {
        _playerBars.SetPressedNoSignal(_draft.PlayerColoredBars);
        _overheadBars.SetPressedNoSignal(_draft.OverheadBars);
        _blood.SetPressedNoSignal(_draft.Blood);
        _interfaceScale.SetValueNoSignal(_draftScale); _scaleValue.Text = $"{_draftScale:0}%";
        foreach (var (percent,button) in _cursorSizes) button.SetPressedNoSignal(_draft.CursorSizePercent == percent);
        double[] volumes = [_draft.MasterVolume,_draft.MusicVolume,_draft.EffectsVolume,_draft.VoiceVolume];
        for (int i=0;i<volumes.Length;i++) { _volumes[i].SetValueNoSignal(volumes[i]); _volumeValues[i].Text = $"{volumes[i]:0}%"; }
        _mute.SetPressedNoSignal(_draft.Muted);
        foreach (var (edition,_,_) in GameSettings.Editions) _editions[(int)edition].SetPressedNoSignal(_draft.ReforgedEdition == edition);
        bool custom = _draft.ReforgedEdition == GameSettings.Edition.Custom;
        foreach (var (key,box) in _options) { box.Disabled = !custom; box.SetPressedNoSignal(_draft.IsOn(key)); }
    }

    private void ResetDraft() { _draft = new(); _draftScale = 100; RefreshSettings(); AudioPreviewed?.Invoke(_draft.Copy()); }

    private void ApplyDraft()
    {
        _current = _draft.Copy(); _currentScale = _draftScale;
        SettingsApplied?.Invoke(_current.Copy(),_currentScale);
    }

    private void Resize()
    {
        var viewport = GetViewportRect().Size;
        // The menu frame takes 60% of the window height, as in the original at 1080p; the Settings frame shares its scale.
        _scale = Math.Clamp(Math.Min(viewport.Y*.6f/MainSize.Y,viewport.X*.92f/SettingsSize.X),.4f,4);
        foreach (var (page,size) in _pages) { page.Size = size*_scale; page.Position = ((viewport-page.Size)/2).Floor(); }
        foreach (var (control,rect) in _places) { control.Position = rect.Position*_scale; control.Size = rect.Size*_scale; }
        foreach (var (control,size) in _fonts) control.AddThemeFontSizeOverride("font_size",Math.Max(8,Mathf.RoundToInt(size*_scale)));
    }

    private Control NewPage(Vector2 size)
    {
        var page = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(page); _pages.Add((page,size));
        return page;
    }

    /// <summary>The original frame art, over a plain parchment panel for caches exported before build-96.</summary>
    private TextureRect Frame(Control page,Vector2 size)
    {
        var fallback = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        fallback.AddThemeStyleboxOverride("panel",new StyleBoxFlat { BgColor = new Color("6f5d43"),BorderColor = new Color("c9b98a"),
            BorderWidthLeft = 3,BorderWidthRight = 3,BorderWidthTop = 3,BorderWidthBottom = 3 });
        Place(page,fallback,0,0,size.X,size.Y);
        var frame = new TextureRect { MouseFilter = MouseFilterEnum.Ignore,ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale };
        _fallbacks[frame] = fallback;
        return Place(page,frame,0,0,size.X,size.Y);
    }

    private T Place<T>(Control parent,T control,float x,float y,float width,float height) where T : Control
    {
        parent.AddChild(control); _places.Add((control,new Rect2(x,y,width,height)));
        return control;
    }

    /// <summary>A label; a wrapping one wraps from the start, since a label first sized to its unwrapped text keeps that width.</summary>
    private Label Caption(string text,int size,Color color,HorizontalAlignment align = HorizontalAlignment.Left,bool wrap = false)
    {
        var label = new Label { AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,Text = text,
            HorizontalAlignment = align,VerticalAlignment = wrap ? VerticalAlignment.Top : VerticalAlignment.Center,MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color",color);
        if (color == Highlight) { label.AddThemeColorOverride("font_shadow_color",Shadow); label.AddThemeConstantOverride("shadow_offset_x",2); label.AddThemeConstantOverride("shadow_offset_y",2); }
        _fonts.Add((label,size));
        return label;
    }

    /// <summary>An engraved menu line, as in the original menu: no box, white under the pointer.</summary>
    private Button Item(Control page,string text,float top,Action action,float left = 94,float width = 184)
    {
        var button = new Button { Text = text,Flat = true,FocusMode = FocusModeEnum.None };
        foreach (string style in new[] { "normal","hover","pressed","hover_pressed","disabled","focus" }) button.AddThemeStyleboxOverride(style,new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color",Engraved); button.AddThemeColorOverride("font_hover_color",Highlight);
        button.AddThemeColorOverride("font_pressed_color",Gold); button.AddThemeColorOverride("font_hover_pressed_color",Gold);
        button.AddThemeColorOverride("font_disabled_color",Dimmed);
        button.AddThemeColorOverride("font_shadow_color",Shadow); button.AddThemeConstantOverride("shadow_offset_x",2); button.AddThemeConstantOverride("shadow_offset_y",2);
        button.Pressed += action; _fonts.Add((button,22));
        return Place(page,button,left,top,width,34);
    }

    /// <summary>A gold-edged black button (control_yellowbox), as the original Settings screen's.</summary>
    private Button BoxButton(string text,Action action)
    {
        var button = new Button { Text = text,FocusMode = FocusModeEnum.None };
        var fallback = new StyleBoxFlat { BgColor = Colors.Black,BorderColor = new Color("c9a24a"),
            BorderWidthLeft = 2,BorderWidthRight = 2,BorderWidthTop = 2,BorderWidthBottom = 2 };
        foreach (string style in new[] { "normal","hover","pressed","hover_pressed","disabled","focus" }) button.AddThemeStyleboxOverride(style,fallback);
        button.AddThemeColorOverride("font_color",Engraved); button.AddThemeColorOverride("font_hover_color",Highlight);
        button.AddThemeColorOverride("font_pressed_color",Gold); button.AddThemeColorOverride("font_hover_pressed_color",Gold);
        button.AddThemeColorOverride("font_focus_color",Engraved);
        button.Pressed += action; _fonts.Add((button,15));
        return button;
    }

    private CheckBox Check(string text,string tooltip,Action<bool> toggled)
    {
        var box = new CheckBox { Text = text,TooltipText = tooltip,FocusMode = FocusModeEnum.None };
        foreach (string color in new[] { "font_color","font_pressed_color","font_focus_color" }) box.AddThemeColorOverride(color,Ink);
        foreach (string color in new[] { "font_hover_color","font_hover_pressed_color" }) box.AddThemeColorOverride(color,Colors.Black);
        box.AddThemeColorOverride("font_disabled_color",InkDim);
        box.Toggled += on => toggled(on); _fonts.Add((box,15));
        return box;
    }

    private void ApplyArt(Func<string,Texture2D?> art)
    {
        foreach (var (frame,name) in new[] { (_mainFrame,"background_good_mainmenu"),(_settingsFrame,"game_settings_screen_g_bg"),
            (_exitFrame,"console_dialog_quit_acknowledgement_g_box") })
        {
            frame.Texture = art(name); _fallbacks[frame].Visible = frame.Texture == null;
        }
        StyleBox? Box(string name) => art(name) is { } texture ? new StyleBoxTexture { Texture = texture,
            TextureMarginLeft = 4,TextureMarginRight = 4,TextureMarginTop = 4,TextureMarginBottom = 4,
            ContentMarginLeft = 8,ContentMarginRight = 8,ContentMarginTop = 2,ContentMarginBottom = 2 } : null;
        if (Box("control_yellowbox_regular") is { } normal)
        {
            var hover = Box("control_yellowbox_highlighted") ?? normal; var pressed = Box("control_yellowbox_pressed") ?? hover;
            foreach (var button in _tabs.Concat(_cursorSizes.Values).Concat([_reset,_apply,_cancel,_ok,_testVoice]))
            {
                button.AddThemeStyleboxOverride("normal",normal); button.AddThemeStyleboxOverride("focus",normal);
                button.AddThemeStyleboxOverride("disabled",normal); button.AddThemeStyleboxOverride("hover",hover);
                // The chosen tab stays lit; a pressed button shows the original pressed box.
                button.AddThemeStyleboxOverride("pressed",button.ToggleMode ? hover : pressed);
                button.AddThemeStyleboxOverride("hover_pressed",button.ToggleMode ? hover : pressed);
            }
        }
        foreach (var box in _options.Values.Concat([_playerBars,_overheadBars,_mute]).Concat(_editions))
        {
            if (art("control_checkbox_checked") is { } check) { box.AddThemeIconOverride("checked",check); box.AddThemeIconOverride("checked_disabled",check); }
            if (art("control_checkbox_empty") is { } empty) { box.AddThemeIconOverride("unchecked",empty); box.AddThemeIconOverride("unchecked_disabled",empty); }
            if (art("control_radiobutton_check1") is { } radio) { box.AddThemeIconOverride("radio_checked",radio); box.AddThemeIconOverride("radio_checked_disabled",radio); }
            if (art("control_radiobutton_empty") is { } ring) { box.AddThemeIconOverride("radio_unchecked",ring); box.AddThemeIconOverride("radio_unchecked_disabled",ring); }
        }
        foreach (var slider in _volumes.Append(_interfaceScale))
        {
            if (art("control_golden_panel_thin") is { } track)
                slider.AddThemeStyleboxOverride("slider",new StyleBoxTexture { Texture = track,TextureMarginLeft = 6,TextureMarginRight = 6,
                    ContentMarginTop = 3,ContentMarginBottom = 4 });
            foreach (string area in new[] { "grabber_area","grabber_area_highlight" }) slider.AddThemeStyleboxOverride(area,new StyleBoxEmpty());
            if (art("control_sliderball_regular") is { } ball)
                foreach (string icon in new[] { "grabber","grabber_highlight","grabber_disabled" }) slider.AddThemeIconOverride(icon,ball);
        }
        _capLeft.Texture = art("control_slider_end_cap_left"); _capRight.Texture = art("control_slider_end_cap_right");
    }
}
