using Godot;

/// <summary>Where the game's own data folder is (build-147): the developer tools' caches (converted/, extracted/) and the
/// paths the game names its install-built files by. Run from the project (run.ps1, the editor binary) it is the
/// repository above the project folder. The exported game (build/WarOfTheRingReforged.exe) looks for converted/ or
/// the repository (prototype/project.godot; build-214, the launcher's import is retired) beside its executable and then
/// in each folder above it, else uses its own folder; --data=&lt;folder&gt; names the folder outright. Nothing from the
/// original game is packed into the game or needs to be there: it is read from the install.</summary>
public static class GamePaths
{
    private static string? _root;

    public static string Root => _root ??= FindRoot();
    public static string Converted => System.IO.Path.Combine(Root,"converted");
    public static string Extracted => System.IO.Path.Combine(Root,"extracted");
    public static string Data(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root,relative));
    public static bool Exported => !OS.HasFeature("editor");

    /// <summary>Build-193: the original game the data is read from at startup, or null when none is known: --install=
    /// on the command line (run.ps1 -Install), the folder the launcher started with this session (build-214), the folder
    /// the launcher kept, the install an earlier import read (converted/install.json), REFORGED_INSTALL, then the tools'
    /// default `TLOTR - War of the Ring` beside the data.
    /// The first that has Archives and Database folders wins. Looked up anew each time, since the launcher can change it.</summary>
    public static string? FindInstall()
    {
        IEnumerable<string?> Candidates()
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--install=")) yield return arg[10..];
            yield return Launcher.Chosen;
            yield return Launcher.SavedFolder();
            yield return InstallRecordFolder();
            yield return System.Environment.GetEnvironmentVariable("REFORGED_INSTALL");
            yield return System.IO.Path.Combine(Root,"TLOTR - War of the Ring");
        }
        foreach (string? folder in Candidates())
            if (folder is { Length: > 0 } && IsInstall(folder)) return System.IO.Path.GetFullPath(folder);
        return null;
    }

    public static bool IsInstall(string folder)
        => System.IO.Directory.Exists(System.IO.Path.Combine(folder,"Archives")) && System.IO.Directory.Exists(System.IO.Path.Combine(folder,"Database"));

    private static string? InstallRecordFolder()
    {
        try
        {
            string record = System.IO.Path.Combine(Converted,"install.json");
            if (!System.IO.File.Exists(record)) return null;
            using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(record));
            return document.RootElement.TryGetProperty("install",out var install) ? install.GetString() : null;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or System.IO.IOException or InvalidOperationException) { return null; }
    }

    private static string FindRoot()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--data=")) return System.IO.Path.GetFullPath(arg[7..]);
        if (!Exported) return System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://.."));
        string start = System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;
        for (var folder = new System.IO.DirectoryInfo(start); folder != null; folder = folder.Parent)
            if (System.IO.Directory.Exists(System.IO.Path.Combine(folder.FullName,"converted"))
                || System.IO.File.Exists(System.IO.Path.Combine(folder.FullName,"prototype","project.godot"))) return folder.FullName;
        return start;
    }
}
