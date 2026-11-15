using Godot;
using System.Diagnostics;
using System.Text.Json;
using Reforged.Install;
using Reforged.Presentation;

/// <summary>The launcher (build-188): the exported game's first screen. The player picks the folder the original War of
/// the Ring is installed in and the launcher checks that the game can read it. Build-214: the game reads the install in
/// place (builds 192-213), so there is nothing to import: the check runs in C# (InstallCheck: every database table, every
/// archive's directory, the text language and the AI players), and Start Reforged opens the main menu as soon as the
/// folder checks out. The folder is kept for the next start; on a first start the launcher looks for the original game
/// in the usual install folders. Shown when the exported game starts with no arguments, or with --launcher
/// (run.ps1 -Launcher); --launcher-smoke checks it end to end (--launcher-folder=, --capture=).</summary>
public partial class Launcher : Control
{
    /// <summary>Start Reforged was pressed: the main scene opens the menu instead of the launcher.</summary>
    public static bool Started { get; private set; }

    /// <summary>Build-214: the install folder Start Reforged was pressed with, for this session (GamePaths.FindInstall).</summary>
    public static string? Chosen { get; private set; }

    /// <summary>Whether the main scene should hand the window to the launcher.</summary>
    public static bool Wanted
    {
        get
        {
            if (Started) return false;
            var args = OS.GetCmdlineUserArgs();
            return args.Contains("--launcher") || args.Contains("--launcher-smoke") || (GamePaths.Exported && NothingChosen);
        }
    }

    /// <summary>Started with nothing that chooses a scene (the exported game, the editor's Play button); --data= only
    /// says where the game data is.</summary>
    public static bool NothingChosen => OS.GetCmdlineUserArgs().All(a => a.StartsWith("--data="));

    private const string SettingsPath = "user://launcher.cfg";
    private static readonly Color Gold = new("d8b574"),Cream = new("fff3cb"),Dim = new("b9ad90"),Good = new("9fd37a"),
        Bad = new("ff8a6e"),Warn = new("f0c75e"),Panel = new(.035f,.055f,.07f,.92f);

    private Task<InstallCheck.Report>? _checking;
    private InstallCheck.Report? _check;
    private string _checkedFolder = "";

    private LineEdit _folder = null!;
    private Button _browse = null!,_start = null!,_exit = null!;
    private Label _checkStatus = null!,_dataStatus = null!;
    private RichTextLabel _log = null!;
    private FileDialog? _dialog;

    public override void _Ready()
    {
        Main.LoadGameText();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color("0b0e12"),MouseFilter = MouseFilterEnum.Ignore,AnchorRight = 1,AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1,AnchorBottom = 1 }; AddChild(center);
        var frame = new PanelContainer { CustomMinimumSize = new(900,0) };
        frame.AddThemeStyleboxOverride("panel",Box(Panel,Gold,2,24));
        center.AddChild(frame);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation",10); frame.AddChild(column);

        var title = Caption(GameText.T("launcher.title"),40,Gold); title.HorizontalAlignment = HorizontalAlignment.Center; column.AddChild(title);
        var subtitle = Caption(GameText.T("launcher.subtitle"),22,Cream); subtitle.HorizontalAlignment = HorizontalAlignment.Center; column.AddChild(subtitle);
        column.AddChild(new HSeparator());

        column.AddChild(Caption(GameText.T("launcher.folder"),18,Gold));
        column.AddChild(Caption(GameText.T("launcher.folder.hint"),14,Dim));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation",8); column.AddChild(row);
        _folder = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill,CustomMinimumSize = new(0,36) };
        _folder.AddThemeFontSizeOverride("font_size",16);
        _folder.AddThemeStyleboxOverride("normal",Box(new Color(.02f,.03f,.04f),Dim,1,8));
        _folder.AddThemeStyleboxOverride("focus",Box(new Color(.02f,.03f,.04f),Gold,1,8));
        _folder.TextSubmitted += folder => RunCheck(folder);
        // Start stays offered only for the folder last checked.
        _folder.TextChanged += _ => Refresh();
        row.AddChild(_folder);
        _browse = ActionButton(GameText.T("launcher.browse"),false); _browse.Pressed += OpenFolderDialog; row.AddChild(_browse);
        _checkStatus = Caption("",15,Dim); _checkStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(_checkStatus);

        column.AddChild(Caption(GameText.T("launcher.log"),14,Gold));
        _log = new RichTextLabel { CustomMinimumSize = new(0,170),ScrollFollowing = true,SelectionEnabled = true,FitContent = false };
        _log.AddThemeFontSizeOverride("normal_font_size",13);
        _log.AddThemeStyleboxOverride("normal",Box(new Color(.015f,.02f,.03f),new Color(.3f,.27f,.2f),1,8));
        column.AddChild(_log);

        column.AddChild(new HSeparator());
        column.AddChild(Caption(GameText.T("launcher.game_data"),18,Gold));
        _dataStatus = Caption(GameText.T("launcher.data.in_place"),15,Cream); _dataStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(_dataStatus);
        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; bottom.AddThemeConstantOverride("separation",12); column.AddChild(bottom);
        _exit = ActionButton(GameText.T("launcher.exit"),false); _exit.Pressed += () => GetTree().Quit(); bottom.AddChild(_exit);
        _start = ActionButton(GameText.T("launcher.start"),true); _start.Pressed += StartGame; bottom.AddChild(_start);

        var overlay = new CanvasLayer { Layer = 40 }; AddChild(overlay); overlay.AddChild(new BuildOverlay());

        string? known = SmokeFolder ?? SavedFolder();
        string folder = known ?? FindInstall() ?? "";
        _folder.Text = folder;
        GD.Print($"Launcher: data root {GamePaths.Root}, folder '{folder}'");
        Refresh();
        if (known == null && folder != "") Log(GameText.T("launcher.found_install",folder),Good);
        if (folder != "") RunCheck(folder);
        else Log(GameText.T("launcher.choose_folder"),Dim);
    }

    public override void _Process(double delta)
    {
        if (_checking is { IsCompleted: true } done)
        {
            _checking = null;
            ShowCheck(done.Result);
            Refresh();
        }
        SmokeStep();
    }

    /// <summary>The original game in one of the folders it is usually installed in, for a first start: the Program Files
    /// folders (also under the publishers' names), a Games folder or the root of a fixed drive, and next to Reforged.</summary>
    private static string? FindInstall()
    {
        string[] names = ["War of the Ring","TLOTR - War of the Ring","The Lord of the Rings - War of the Ring","LOTR War of the Ring"];
        var parents = new List<string>();
        foreach (var special in new[] { System.Environment.SpecialFolder.ProgramFilesX86,System.Environment.SpecialFolder.ProgramFiles })
        {
            string programs = System.Environment.GetFolderPath(special);
            if (programs == "") continue;
            parents.Add(programs);
            foreach (string publisher in new[] { "Sierra","Sierra Entertainment","Vivendi Universal Games","VU Games","Liquid Entertainment" })
                parents.Add(System.IO.Path.Combine(programs,publisher));
        }
        try
        {
            foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => d.DriveType == System.IO.DriveType.Fixed && d.IsReady))
            {
                parents.Add(System.IO.Path.Combine(drive.RootDirectory.FullName,"Games"));
                parents.Add(drive.RootDirectory.FullName);
            }
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { }
        parents.Add(GamePaths.Root);
        if (System.IO.Path.GetDirectoryName(GamePaths.Root.TrimEnd('/','\\')) is { Length: > 0 } above) parents.Add(above);
        foreach (string parent in parents.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (string name in names)
            {
                string candidate = System.IO.Path.Combine(parent,name);
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate,"Database","Models.lqr"))
                    && System.IO.Directory.Exists(System.IO.Path.Combine(candidate,"Archives")))
                    return System.IO.Path.GetFullPath(candidate);
            }
        return null;
    }

    // ---- Layout helpers -------------------------------------------------------------------------------------------

    private static StyleBoxFlat Box(Color fill,Color edge,int border,int margin) => new()
    {
        BgColor = fill,BorderColor = edge,BorderWidthLeft = border,BorderWidthRight = border,BorderWidthTop = border,BorderWidthBottom = border,
        ContentMarginLeft = margin,ContentMarginRight = margin,ContentMarginTop = margin*.6f,ContentMarginBottom = margin*.6f,
        CornerRadiusTopLeft = 3,CornerRadiusTopRight = 3,CornerRadiusBottomLeft = 3,CornerRadiusBottomRight = 3
    };

    private static Label Caption(string text,int size,Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",color);
        return label;
    }

    private static Button ActionButton(string text,bool primary)
    {
        var button = new Button { Text = text,FocusMode = FocusModeEnum.None,CustomMinimumSize = new(primary ? 240 : 150,primary ? 48 : 36) };
        button.AddThemeFontSizeOverride("font_size",primary ? 20 : 16);
        var fill = primary ? new Color(.2f,.16f,.07f) : new Color(.06f,.08f,.1f);
        button.AddThemeStyleboxOverride("normal",Box(fill,Gold,1,14));
        button.AddThemeStyleboxOverride("hover",Box(fill.Lightened(.12f),Cream,1,14));
        button.AddThemeStyleboxOverride("pressed",Box(fill.Darkened(.2f),Gold,1,14));
        button.AddThemeStyleboxOverride("disabled",Box(new Color(.05f,.05f,.05f,.8f),new Color(.3f,.3f,.3f),1,14));
        button.AddThemeColorOverride("font_color",primary ? Cream : Gold);
        button.AddThemeColorOverride("font_hover_color",Cream);
        button.AddThemeColorOverride("font_disabled_color",new Color(.45f,.45f,.45f));
        return button;
    }

    private void Log(string text,Color? color = null)
    {
        if (_log.GetParagraphCount() > 600) _log.RemoveParagraph(0);
        _log.PushColor(color ?? new Color(.78f,.76f,.7f)); _log.AddText(text+"\n"); _log.Pop();
    }

    // ---- State ----------------------------------------------------------------------------------------------------

    /// <summary>Start needs the folder in the field to be the one last checked, and the check to have passed.</summary>
    private bool CanStart => _checking == null && _check?.Ok == true && _checkedFolder == _folder.Text.Trim();

    private void Refresh()
    {
        _folder.Editable = _checking == null; _browse.Disabled = _checking != null;
        _start.Disabled = !CanStart;
    }

    private static string LanguageName(string code) => GameText.TryT("launcher.language."+code) ?? InstallCheck.Languages.GetValueOrDefault(code) ?? code;

    // ---- Folder choice and check ----------------------------------------------------------------------------------

    /// <summary>The install folder the launcher last checked and kept (also where the game reads the original, build-193).</summary>
    internal static string? SavedFolder()
    {
        var config = new ConfigFile();
        return config.Load(SettingsPath) == Error.Ok && config.GetValue("launcher","folder","").AsString() is { Length: > 0 } folder ? folder : null;
    }

    private static void SaveFolder(string folder)
    {
        var config = new ConfigFile(); config.Load(SettingsPath);
        config.SetValue("launcher","folder",folder); config.Save(SettingsPath);
    }

    private void OpenFolderDialog()
    {
        _dialog?.QueueFree();
        _dialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir,Access = FileDialog.AccessEnum.Filesystem,UseNativeDialog = true,
            Title = GameText.T("launcher.choose_folder") };
        if (System.IO.Directory.Exists(_folder.Text.Trim())) _dialog.CurrentDir = _folder.Text.Trim();
        _dialog.DirSelected += folder => { _folder.Text = System.IO.Path.GetFullPath(folder); RunCheck(_folder.Text); };
        AddChild(_dialog);
        _dialog.PopupCentered(new Vector2I(900,600));
    }

    private void RunCheck(string folder)
    {
        folder = folder.Trim();
        if (_checking != null || folder == "") return;
        _check = null; _checkedFolder = folder;
        _checkStatus.Text = GameText.T("launcher.checking"); _checkStatus.AddThemeColorOverride("font_color",Dim);
        Log("> "+GameText.T("launcher.checking")+" "+folder,Dim);
        string root = GamePaths.Root;
        _checking = Task.Run(() => InstallCheck.Run(folder,root));
        Refresh();
    }

    private void ShowCheck(InstallCheck.Report report)
    {
        _check = report;
        if (report.Ok)
        {
            _checkStatus.Text = GameText.T("launcher.check.ok",report.Tables,report.Archives,report.Files,report.AiPlayers,LanguageName(report.Language));
            _checkStatus.AddThemeColorOverride("font_color",Good);
            Log(_checkStatus.Text,Good);
        }
        else
        {
            var lines = new List<string> { GameText.T("launcher.check.failed") };
            foreach (var error in report.Errors)
                lines.Add("• "+(GameText.TryT("launcher.error."+error.Code,error.Detail) ?? error.Message));
            _checkStatus.Text = string.Join("\n",lines);
            _checkStatus.AddThemeColorOverride("font_color",Bad);
            foreach (var error in report.Errors) Log(error.Message,Bad);
        }
        foreach (string warning in report.Warnings) Log(warning,Warn);
    }

    private void StartGame()
    {
        if (!CanStart) return;
        string folder = _folder.Text.Trim();
        // The smoke's folder serves this session only; a person's choice is kept for the next start.
        if (!AutomatedRun) SaveFolder(folder);
        Chosen = System.IO.Path.GetFullPath(folder);
        Started = true;
        GetTree().ChangeSceneToFile(ProjectSettings.GetSetting("application/run/main_scene").AsString());
    }

    // ---- Smoke (--launcher-smoke) ---------------------------------------------------------------------------------

    private static bool AutomatedRun => OS.GetCmdlineUserArgs().Contains("--launcher-smoke");
    private static string? SmokeFolder => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--launcher-folder="))?[18..];
    private static string SmokeCapture => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--launcher-capture="))?[19..] ?? "";
    /// <summary>What the launcher smoke saw; Main finishes the report once the main menu shows after Start.</summary>
    public static readonly Dictionary<string,bool> SmokeChecks = [];
    public static bool SmokeRunning => Started && AutomatedRun;
    private int _smokePhase;
    private readonly long _opened = Stopwatch.GetTimestamp();

    private void SmokeStep()
    {
        if (!AutomatedRun || _smokePhase >= 1) return;
        if (Stopwatch.GetElapsedTime(_opened).TotalMinutes > 5) { SmokeChecks["launcher_finished_in_time"] = false; FinishSmoke(null); return; }
        if (_checking != null || _check == null) return;
        SmokeChecks["install_check_parses"] = _check.Ok && _check.Tables > 100 && _check.Archives >= 20 && _check.Language != "";
        SmokeChecks["start_enabled_after_check"] = !_start.Disabled;
        _smokePhase = 1;
        if (SmokeCapture != "") _ = CaptureAndStart(); else Start();
    }

    private void Start()
    {
        _smokePhase = 2;
        if (_start.Disabled) { FinishSmoke(null); return; }
        _start.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private async Task CaptureAndStart()
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        GD.Print("CAPTURE "+SmokeCapture+" "+image.SavePng(SmokeCapture));
        Start();
    }

    /// <summary>The smoke's end: called by Main once the main menu shows after Start (null when it never started).</summary>
    public static void FinishSmoke(bool? menuOpened)
    {
        SmokeChecks["start_opens_main_menu"] = menuOpened == true;
        var report = new { build = BuildInfo.Label,checks = SmokeChecks,passed = SmokeChecks.Values.All(v => v),folder = SmokeFolder ?? "" };
        string json = JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(GamePaths.Report("launcher-smoke-report.json"),json);
        GD.Print("LAUNCHER_SMOKE "+JsonSerializer.Serialize(report));
        ((SceneTree)Engine.GetMainLoop()).Quit(report.passed ? 0 : 1);
    }
}
