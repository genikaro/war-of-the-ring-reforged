using Godot;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Reforged.Presentation;

/// <summary>The launcher (build-188): the exported game's first screen. The player picks the folder the original War of
/// the Ring is installed in; the launcher checks that it parses (tools/import_install.py --check), imports it into the
/// game data (extracted/ and converted/: archives, content pack, menu art, every map) with a progress bar and a log,
/// and offers Start Reforged once a complete import is there (converted/install.json). Game data made earlier with the
/// developer tools, without a record, counts as imported. Start Reforged opens the main menu in the same window.
/// Shown when the exported game starts with no arguments, or with --launcher (run.ps1 -Launcher); --launcher-smoke
/// checks it end to end (--launcher-folder=, --launcher-import, --capture=).
/// Build-191: the import's progress is a LauncherProgress (the steps, a bar that keeps moving, the percentage) with the
/// time taken and an estimate of the time left, also shown in the window title; Cancel asks for a second click; on a
/// first start the launcher looks for the original game in the usual install folders.</summary>
public partial class Launcher : Control
{
    /// <summary>Start Reforged was pressed: the main scene opens the menu instead of the launcher.</summary>
    public static bool Started { get; private set; }

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

    private readonly ConcurrentQueue<(string? Line,int? Exit)> _output = new();
    private Process? _process;
    private Job _job;
    private enum Job { None,Check,Import }
    private CheckReport? _check;
    private string _checkedFolder = "";
    private double _stepStart,_stepEnd;
    private int _step,_steps;
    private string _stepTitle = "",_importError = "";
    private long _importStarted;

    private LineEdit _folder = null!;
    private Button _browse = null!,_import = null!,_start = null!,_exit = null!;
    private Label _checkStatus = null!,_dataStatus = null!,_progressText = null!,_timeText = null!;
    private LauncherProgress _progress = null!;
    private RichTextLabel _log = null!;
    private FileDialog? _dialog;

    private sealed record CheckReport(bool Ok,string Language,string LanguageName,int Tables,int Archives,int Files,int AiPlayers,
        List<(string Code,string Message,string Detail)> Errors,List<string> Warnings);

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
        // Import stays offered only for the folder last checked.
        _folder.TextChanged += _ => Refresh();
        row.AddChild(_folder);
        _browse = ActionButton(GameText.T("launcher.browse"),false); _browse.Pressed += OpenFolderDialog; row.AddChild(_browse);
        _checkStatus = Caption("",15,Dim); _checkStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(_checkStatus);

        var importRow = new HBoxContainer(); importRow.AddThemeConstantOverride("separation",14); column.AddChild(importRow);
        _import = ActionButton(GameText.T("launcher.import"),false); _import.Pressed += ImportOrCancel; importRow.AddChild(_import);
        _progressText = Caption("",14,Dim);
        _progressText.SizeFlagsHorizontal = SizeFlags.ExpandFill; _progressText.ClipText = true; _progressText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _progressText.VerticalAlignment = VerticalAlignment.Center; importRow.AddChild(_progressText);
        _timeText = Caption("",14,Dim); _timeText.VerticalAlignment = VerticalAlignment.Center; _timeText.HorizontalAlignment = HorizontalAlignment.Right;
        importRow.AddChild(_timeText);
        _progress = new LauncherProgress(); column.AddChild(_progress);

        column.AddChild(Caption(GameText.T("launcher.log"),14,Gold));
        _log = new RichTextLabel { CustomMinimumSize = new(0,170),ScrollFollowing = true,SelectionEnabled = true,FitContent = false };
        _log.AddThemeFontSizeOverride("normal_font_size",13);
        _log.AddThemeStyleboxOverride("normal",Box(new Color(.015f,.02f,.03f),new Color(.3f,.27f,.2f),1,8));
        column.AddChild(_log);

        column.AddChild(new HSeparator());
        column.AddChild(Caption(GameText.T("launcher.game_data"),18,Gold));
        _dataStatus = Caption("",15,Cream); _dataStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(_dataStatus);
        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; bottom.AddThemeConstantOverride("separation",12); column.AddChild(bottom);
        _exit = ActionButton(GameText.T("launcher.exit"),false); _exit.Pressed += () => GetTree().Quit(); bottom.AddChild(_exit);
        _start = ActionButton(GameText.T("launcher.start"),true); _start.Pressed += StartGame; bottom.AddChild(_start);

        var overlay = new CanvasLayer { Layer = 40 }; AddChild(overlay); overlay.AddChild(new BuildOverlay());

        _windowTitle = GetWindow().Title;
        string? known = SmokeFolder ?? SavedFolder() ?? ReadRecord()?.Install;
        string folder = known ?? FindInstall() ?? "";
        _folder.Text = folder;
        GD.Print($"Launcher: data root {GamePaths.Root}, game data {(DataPresent() ? "present" : "missing")}, folder '{folder}'");
        Refresh();
        if (known == null && folder != "") Log(GameText.T("launcher.found_install",folder),Good);
        if (folder != "") RunCheck(folder);
        else Log(GameText.T("launcher.choose_folder"),Dim);
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

    public override void _ExitTree() => StopProcess();

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) StopProcess();
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

    // ---- Game data state ------------------------------------------------------------------------------------------

    private sealed record ImportRecord(bool Complete,string Install,string Language,string LanguageName,int? Build,string Imported,
        int Maps,List<string> FailedMaps);

    private static string RecordPath => System.IO.Path.Combine(GamePaths.Converted,"install.json");

    private static ImportRecord? ReadRecord()
    {
        try
        {
            if (!System.IO.File.Exists(RecordPath)) return null;
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(RecordPath));
            var root = document.RootElement;
            string Text(string name) => root.TryGetProperty(name,out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
            return new(root.TryGetProperty("complete",out var complete) && complete.GetBoolean(),Text("install"),Text("language"),Text("language_name"),
                root.TryGetProperty("build",out var build) && build.ValueKind == JsonValueKind.Number ? build.GetInt32() : null,Text("imported"),
                root.TryGetProperty("maps",out var maps) && maps.ValueKind == JsonValueKind.Array ? maps.GetArrayLength() : 0,
                root.TryGetProperty("failed_maps",out var failed) && failed.ValueKind == JsonValueKind.Array ? [.. failed.EnumerateArray().Select(f => f.GetString()!)] : []);
        }
        catch (Exception error) when (error is JsonException or System.IO.IOException or InvalidOperationException) { return null; }
    }

    /// <summary>The files the menu and a match cannot start without: the content pack, the menu art and one map.</summary>
    private static bool DataPresent()
    {
        string converted = GamePaths.Converted;
        bool File(params string[] parts) => System.IO.File.Exists(System.IO.Path.Combine([converted,.. parts]));
        string maps = System.IO.Path.Combine(converted,"skirmish-maps");
        return File("content","good","content.json") && File("frontend","index.json")
            && (File("skirmish","manifest.json") || (System.IO.Directory.Exists(maps)
                && System.IO.Directory.GetDirectories(maps).Any(d => System.IO.File.Exists(System.IO.Path.Combine(d,"manifest.json")))));
    }

    private bool CanStart => _job != Job.Import && DataPresent() && ReadRecord() is not { Complete: false };

    private void Refresh()
    {
        bool running = _job != Job.None;
        _folder.Editable = !running; _browse.Disabled = running;
        _import.Text = _job == Job.Import ? GameText.T(CancelArmed ? "launcher.cancel.confirm" : "button.cancel")
            : GameText.T(DataPresent() ? "launcher.reimport" : "launcher.import");
        _import.Disabled = _job == Job.Check || (_job == Job.None && !(_check?.Ok == true && _checkedFolder == _folder.Text.Trim()));
        _start.Disabled = !CanStart;
        var record = ReadRecord();
        bool present = DataPresent();
        var lines = new List<string>();
        Color color = Cream;
        if (_job == Job.Import) { lines.Add(GameText.T("launcher.data.importing")); color = Dim; }
        else if (record is { Complete: false }) { lines.Add(GameText.T("launcher.data.incomplete")); color = Warn; }
        else if (record != null && present)
        {
            lines.Add(GameText.T("launcher.data.ready",record.Install,LanguageName(record.Language,record.LanguageName),
                record.Imported.Replace('T',' '),record.Maps));
            if (record.FailedMaps.Count > 0) lines.Add(GameText.T("launcher.data.failed_maps",string.Join(", ",record.FailedMaps)));
            if (record.Build is { } build && build < BuildInfo.Number) lines.Add(GameText.T("launcher.data.old_build",build,BuildInfo.Number));
            color = Good;
        }
        else if (present) { lines.Add(GameText.T("launcher.data.found")); color = Good; }
        else { lines.Add(GameText.T("launcher.data.none")); color = Dim; }
        _dataStatus.Text = string.Join("\n",lines);
        _dataStatus.AddThemeColorOverride("font_color",color);
    }

    private static string LanguageName(string code,string fallback) => GameText.TryT("launcher.language."+code) ?? (fallback != "" ? fallback : code);

    // ---- Folder choice and check ----------------------------------------------------------------------------------

    private static string? SavedFolder()
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
        if (_job != Job.None || folder == "") return;
        _check = null; _checkedFolder = folder;
        _checkStatus.Text = GameText.T("launcher.checking"); _checkStatus.AddThemeColorOverride("font_color",Dim);
        Log("> "+GameText.T("launcher.checking")+" "+folder,Dim);
        if (StartTool(Job.Check,["--check",folder])) Refresh();
    }

    private static CheckReport ParseCheck(JsonElement report)
    {
        int Number(string name) => report.TryGetProperty(name,out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
        string Text(string name) => report.TryGetProperty(name,out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var errors = report.GetProperty("errors").EnumerateArray().Select(e => (e.GetProperty("code").GetString() ?? "",
            e.GetProperty("message").GetString() ?? "",e.TryGetProperty("detail",out var detail) ? detail.GetString() ?? "" : "")).ToList();
        var warnings = report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString() ?? "").ToList();
        return new(report.GetProperty("ok").GetBoolean(),Text("language"),Text("language_name"),Number("tables"),Number("archives"),Number("files"),
            Number("ai_players"),errors,warnings);
    }

    private void ShowCheck(CheckReport report)
    {
        _check = report;
        if (report.Ok)
        {
            _checkStatus.Text = GameText.T("launcher.check.ok",report.Tables,report.Archives,report.Files,report.AiPlayers,
                LanguageName(report.Language,report.LanguageName));
            _checkStatus.AddThemeColorOverride("font_color",Good);
            Log(_checkStatus.Text,Good);
        }
        else
        {
            var lines = new List<string> { GameText.T("launcher.check.failed") };
            foreach (var (code,message,detail) in report.Errors)
                lines.Add("• "+(GameText.TryT("launcher.error."+code,detail) ?? message));
            _checkStatus.Text = string.Join("\n",lines);
            _checkStatus.AddThemeColorOverride("font_color",Bad);
            foreach (var (_,message,_) in report.Errors) Log(message,Bad);
        }
        foreach (string warning in report.Warnings) Log(warning,Warn);
    }

    // ---- Running the tools ----------------------------------------------------------------------------------------

    /// <summary>The Python that runs the import: REFORGED_PYTHON, the portable one in converted/tooling/python, or the
    /// one on PATH.</summary>
    private static string Python()
    {
        if (System.Environment.GetEnvironmentVariable("REFORGED_PYTHON") is { Length: > 0 } named) return named;
        string bundled = System.IO.Path.Combine(GamePaths.Converted,"tooling","python","python3.13.exe");
        return System.IO.File.Exists(bundled) ? bundled : "python";
    }

    private static string ImportScript => System.IO.Path.Combine(GamePaths.Root,"tools","import_install.py");

    private bool StartTool(Job job,IEnumerable<string> args)
    {
        if (!System.IO.File.Exists(ImportScript))
        {
            Fail(job,GameText.T("launcher.error.no_tools",ImportScript));
            return false;
        }
        var info = new ProcessStartInfo(Python())
        {
            WorkingDirectory = GamePaths.Root,UseShellExecute = false,CreateNoWindow = true,
            RedirectStandardOutput = true,RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-u"); info.ArgumentList.Add(ImportScript);
        foreach (string arg in args) info.ArgumentList.Add(arg);
        info.Environment["PYTHONIOENCODING"] = "utf-8"; info.Environment["PYTHONUNBUFFERED"] = "1";
        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_,e) => { if (e.Data != null) _output.Enqueue((e.Data,null)); };
        process.ErrorDataReceived += (_,e) => { if (e.Data != null) _output.Enqueue((e.Data,null)); };
        try { process.Start(); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            Fail(job,GameText.T("launcher.error.no_python")+" ("+error.Message+")");
            return false;
        }
        _job = job; _process = process;
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        // WaitForExit without a timeout also waits for both redirected streams to end.
        _ = Task.Run(() => { process.WaitForExit(); _output.Enqueue((null,process.ExitCode)); });
        return true;
    }

    private void Fail(Job job,string message)
    {
        Log(message,Bad);
        if (job == Job.Check) { _checkStatus.Text = message; _checkStatus.AddThemeColorOverride("font_color",Bad); }
        else _progressText.Text = message;
        Refresh();
    }

    private void StopProcess()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public override void _Process(double delta)
    {
        while (_output.TryDequeue(out var item))
        {
            if (item.Line != null) OnLine(item.Line);
            else if (item.Exit is { } exit) OnExit(exit);
        }
        if (_job == Job.Import) UpdateProgress(delta);
        if (_cancelArmedAt != 0 && !CancelArmed) { _cancelArmedAt = 0; Refresh(); }
        SmokeStep();
    }

    private void OnLine(string line)
    {
        const string prefix = "@reforged ";
        if (!line.StartsWith(prefix)) { Log(line); return; }
        try
        {
            using var document = JsonDocument.Parse(line[prefix.Length..]);
            var message = document.RootElement;
            switch (message.GetProperty("event").GetString())
            {
                case "check": ShowCheck(ParseCheck(message.GetProperty("report"))); break;
                case "plan":
                    _progress.SetPlan(message.GetProperty("steps").EnumerateArray().Select(step => (step.GetProperty("title").GetString() ?? "",
                        step.GetProperty("start").GetDouble(),step.GetProperty("end").GetDouble())).ToList());
                    break;
                case "step":
                    _step = message.GetProperty("step").GetInt32(); _steps = message.GetProperty("steps").GetInt32();
                    _stepStart = message.GetProperty("start").GetDouble(); _stepEnd = message.GetProperty("end").GetDouble();
                    _stepTitle = GameText.TryT("launcher.step."+_step) ?? message.GetProperty("title").GetString() ?? "";
                    _stepItem = ""; _stepReported = false; _stepStartedAt = Elapsed();
                    _progress.SetStep(_step); _progress.Advance(_stepStart);
                    Log("== "+_stepTitle,Gold);
                    break;
                case "progress":
                    int total = message.GetProperty("total").GetInt32(),done = message.GetProperty("done").GetInt32();
                    // Build-191: `fraction` weighs the work; older imports only count the items.
                    double part = message.TryGetProperty("fraction",out var fraction) ? fraction.GetDouble() : total > 0 ? (double)done/total : 0;
                    _stepReported = true;
                    _progress.Advance(_stepStart+(_stepEnd-_stepStart)*part);
                    string item = message.GetProperty("item").GetString() ?? "";
                    if (total > 0 || item != "") _stepItem = total > 0 ? $"{done}/{total}"+(item != "" ? "  "+item : "") : item;
                    break;
                case "warning": Log(message.GetProperty("message").GetString() ?? "",Warn); break;
                case "error": _importError = message.GetProperty("message").GetString() ?? ""; Log(_importError,Bad); break;
                case "done": _progress.Advance(1); break;
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { Log(line); }
    }

    private string _stepItem = "",_windowTitle = "";
    private bool _stepReported;
    private double _stepStartedAt,_eta = -1;
    private int _titlePercent = -1;

    private void UpdateProgress(double delta)
    {
        double elapsed = Elapsed();
        if (_step > 0 && !_stepReported)
        {
            // A step that reports nothing on its way (the content pack, the menu art) creeps on at the pace the import
            // has kept so far, slowing near its end rather than stopping, until the next step starts.
            double perUnit = _stepStart > .02 ? _stepStartedAt/_stepStart : 17*60;
            double expected = Math.Max(1,(_stepEnd-_stepStart)*perUnit),t = (elapsed-_stepStartedAt)/expected;
            double part = t < .85 ? t : .85+.14*(1-Math.Exp(-(t-.85)/.3));
            _progress.Advance(_stepStart+(_stepEnd-_stepStart)*part);
        }
        if (_step > 0)
        {
            string text = GameText.T("launcher.step",_step,_steps,_stepTitle);
            if (_stepItem != "") text += ": "+_stepItem;
            _progressText.Text = text;
        }
        // The time left from the pace so far, smoothed so it counts down steadily instead of jumping.
        double shown = _progress.Shown;
        string left;
        if (shown > .03 && elapsed > 15)
        {
            double estimate = elapsed*(1-shown)/shown;
            _eta = _eta < 0 ? estimate : Math.Max(0,_eta-delta)+(estimate-Math.Max(0,_eta-delta))*Math.Min(1,delta/4);
            left = _eta < 60 ? GameText.T("launcher.eta.minute") : GameText.T("launcher.eta",(int)Math.Ceiling(_eta/60));
        }
        else left = GameText.T("launcher.eta.estimating");
        _timeText.Text = Duration(elapsed)+"  ·  "+left;
        int percent = (int)Math.Floor(shown*100);
        if (percent != _titlePercent)
        {
            _titlePercent = percent;
            GetWindow().Title = $"{percent}% · {GameText.T("launcher.data.importing")} {_windowTitle}";
        }
    }

    private double Elapsed() => Stopwatch.GetElapsedTime(_importStarted).TotalSeconds;
    private static string Duration(double seconds) => GameText.T("launcher.duration",(int)seconds/60,(int)seconds%60);

    private void OnExit(int exit)
    {
        var job = _job;
        _job = Job.None; _process?.Dispose(); _process = null;
        if (job == Job.Check && _check == null)
        {
            // No report: Python itself did not run (missing, or without Pillow and NumPy).
            Fail(Job.Check,GameText.T("launcher.error.no_python"));
            return;
        }
        if (job == Job.Import)
        {
            _stepItem = ""; _timeText.Text = ""; _cancelArmedAt = 0;
            GetWindow().Title = _windowTitle;
            if (_cancelled) { _progress.Finish(LauncherProgress.Phase.Cancelled); _progressText.Text = GameText.T("launcher.import.cancelled"); Log(_progressText.Text,Warn); }
            else if (exit == 0) { _progress.Finish(LauncherProgress.Phase.Done); _progressText.Text = GameText.T("launcher.import.done",Duration(Elapsed())); Log(_progressText.Text,Good); }
            else
            {
                _progress.Finish(LauncherProgress.Phase.Failed);
                _progressText.Text = GameText.T("launcher.import.failed",_importError != "" ? _importError : "exit "+exit);
            }
            _progressText.AddThemeColorOverride("font_color",_cancelled ? Warn : exit == 0 ? Good : Bad);
            _cancelled = false;
            if (exit == 0)
            {
                // The import brought the install's text: the launcher speaks its language from now on.
                Main.LoadGameText();
                if (!AutomatedRun) SaveFolder(_folder.Text.Trim());
            }
        }
        Refresh();
    }

    private bool _cancelled;
    private long _cancelArmedAt;
    /// <summary>Cancel was pressed once in the last few seconds: a second press stops the import. A stray click does not
    /// throw away a quarter of an hour's work.</summary>
    private bool CancelArmed => _cancelArmedAt != 0 && Stopwatch.GetElapsedTime(_cancelArmedAt).TotalSeconds < 4;

    private void ImportOrCancel()
    {
        if (_job == Job.Import)
        {
            if (CancelArmed) { _cancelled = true; StopProcess(); }
            else { _cancelArmedAt = Stopwatch.GetTimestamp(); Refresh(); }
            return;
        }
        string folder = _folder.Text.Trim();
        if (_job != Job.None || _check?.Ok != true || folder != _checkedFolder) return;
        _importError = ""; _step = 0; _stepItem = ""; _eta = -1; _titlePercent = -1; _cancelArmedAt = 0;
        _progress.Begin(); _progressText.Text = ""; _timeText.Text = "";
        _progressText.AddThemeColorOverride("font_color",Dim);
        _importStarted = Stopwatch.GetTimestamp();
        Log("> "+GameText.T("launcher.import")+" "+folder,Dim);
        if (StartTool(Job.Import,[folder,"--build",BuildInfo.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)])) Refresh();
    }

    private void StartGame()
    {
        if (!CanStart) return;
        if (_folder.Text.Trim() is { Length: > 0 } folder && _check?.Ok == true && !AutomatedRun) SaveFolder(folder);
        Started = true;
        GetTree().ChangeSceneToFile(ProjectSettings.GetSetting("application/run/main_scene").AsString());
    }

    // ---- Smoke (--launcher-smoke) ---------------------------------------------------------------------------------

    private static bool AutomatedRun => OS.GetCmdlineUserArgs().Contains("--launcher-smoke");
    private static string? SmokeFolder => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--launcher-folder="))?[18..];
    private static bool SmokeImport => OS.GetCmdlineUserArgs().Contains("--launcher-import");
    private static string SmokeCapture => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--launcher-capture="))?[19..] ?? "";
    /// <summary>What the launcher smoke saw; Main finishes the report once the main menu shows after Start.</summary>
    public static readonly Dictionary<string,bool> SmokeChecks = [];
    public static bool SmokeRunning => Started && AutomatedRun;
    private int _smokePhase;
    private readonly long _opened = Stopwatch.GetTimestamp();

    private void SmokeStep()
    {
        if (!AutomatedRun || _smokePhase >= 3) return;
        if (Stopwatch.GetElapsedTime(_opened).TotalMinutes > 60) { SmokeChecks["launcher_finished_in_time"] = false; FinishSmoke(null); return; }
        switch (_smokePhase)
        {
            case 0 when _job == Job.None && _check != null:
                SmokeChecks["install_check_parses"] = _check.Ok && _check.Tables > 100 && _check.Archives >= 20 && _check.Language != "";
                SmokeChecks["import_offered_after_check"] = !_import.Disabled;
                if (SmokeImport && _check.Ok) { _smokePhase = 1; ImportOrCancel(); SmokeChecks["start_disabled_while_importing"] = _start.Disabled; }
                else _smokePhase = 2;
                break;
            case 1 when _job == Job.Import:
                // Build-191: a capture also pictures the import halfway, beside the final one (<capture>-importing.png).
                if (SmokeCapture != "" && !_capturedImport && _progress.Shown >= .5)
                {
                    _capturedImport = true;
                    _ = Capture(System.IO.Path.ChangeExtension(SmokeCapture,null)+"-importing.png");
                }
                break;
            case 1 when _job == Job.None:
                SmokeChecks["import_succeeds"] = ReadRecord() is { Complete: true,Maps: > 0 } && _progress.Target >= 1;
                _smokePhase = 2;
                break;
            case 2:
                SmokeChecks["start_enabled_with_game_data"] = !_start.Disabled;
                _smokePhase = 3;
                if (SmokeCapture != "") _ = CaptureAndStart(); else Start();
                break;
        }
    }

    private void Start()
    {
        _smokePhase = 4;
        if (_start.Disabled) { FinishSmoke(null); return; }
        _start.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private bool _capturedImport;

    private async Task CaptureAndStart()
    {
        await Capture(SmokeCapture);
        Start();
    }

    private async Task Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        GD.Print("CAPTURE "+path+" "+image.SavePng(path));
    }

    /// <summary>The smoke's end: called by Main once the main menu shows after Start (null when it never started).</summary>
    public static void FinishSmoke(bool? menuOpened)
    {
        SmokeChecks["start_opens_main_menu"] = menuOpened == true;
        var report = new { build = BuildInfo.Label,checks = SmokeChecks,passed = SmokeChecks.Values.All(v => v),
            import_ran = SmokeImport,folder = SmokeFolder ?? "" };
        string json = JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(System.IO.Path.Combine(GamePaths.Converted,"launcher-smoke-report.json"),json);
        GD.Print("LAUNCHER_SMOKE "+JsonSerializer.Serialize(report));
        ((SceneTree)Engine.GetMainLoop()).Quit(report.passed ? 0 : 1);
    }
}
