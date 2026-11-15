using Godot;

/// <summary>Where the game's own data folder is (build-147): the folder the smokes write their reports to (build-217:
/// reports/) and the developer tools' caches. Run from the project (run.ps1, the editor binary) it is the repository
/// above the project folder. The exported game (build/WarOfTheRingReforged.exe) looks for the repository
/// (prototype/project.godot) beside its executable and then in each folder above it, else uses its own folder;
/// --data=&lt;folder&gt; names the folder outright. Nothing from the original game is packed into the game or needs to be
/// there: it is read from the install.</summary>
public static class GamePaths
{
    private static string? _root;

    public static string Root => _root ??= FindRoot();
    public static string Data(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root,relative));
    public static bool Exported => !OS.HasFeature("editor");

    /// <summary>Build-217: the names the game gives the data it builds from the install: the content pack
    /// (content/good), the menu art (frontend), the maps (skirmish for Bilbo's Bluffs, skirmish-maps/&lt;stem&gt;, the
    /// Sandbox's skirmish-maps/Sandbox) and the Helm's Deep demo (prototype), laid out as the import's converted/ was.
    /// The root is never a folder on disk ('&lt;' and '&gt;' cannot be in a Windows file name), so whatever the install
    /// does not give fails to load instead of being read from an old import.</summary>
    public static string Installed(string relative = "") => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root,InstalledRoot,relative));
    private const string InstalledRoot = "<install>";

    /// <summary>Build-217: where the smokes, captures' records and the launcher write their reports (made when first written).</summary>
    public static string Report(string name)
    {
        string folder = Data("reports");
        System.IO.Directory.CreateDirectory(folder);
        return System.IO.Path.Combine(folder,name);
    }

    /// <summary>Build-193: the original game the data is read from, or null when none is known: --install= on the
    /// command line (run.ps1 -Install), the folder the launcher started with this session (build-214), the folder the
    /// launcher kept, REFORGED_INSTALL, then `TLOTR - War of the Ring` beside the data. The first that has Archives and
    /// Database folders wins. Looked up anew each time, since the launcher can change it.</summary>
    public static string? FindInstall()
    {
        IEnumerable<string?> Candidates()
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--install=")) yield return arg[10..];
            yield return Launcher.Chosen;
            yield return Launcher.SavedFolder();
            yield return System.Environment.GetEnvironmentVariable("REFORGED_INSTALL");
            yield return System.IO.Path.Combine(Root,"TLOTR - War of the Ring");
        }
        foreach (string? folder in Candidates())
            if (folder is { Length: > 0 } && IsInstall(folder)) return System.IO.Path.GetFullPath(folder);
        return null;
    }

    public static bool IsInstall(string folder)
        => System.IO.Directory.Exists(System.IO.Path.Combine(folder,"Archives")) && System.IO.Directory.Exists(System.IO.Path.Combine(folder,"Database"));

    private static string FindRoot()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--data=")) return System.IO.Path.GetFullPath(arg[7..]);
        if (!Exported) return System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://.."));
        string start = System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;
        for (var folder = new System.IO.DirectoryInfo(start); folder != null; folder = folder.Parent)
            if (System.IO.File.Exists(System.IO.Path.Combine(folder.FullName,"prototype","project.godot"))) return folder.FullName;
        return start;
    }
}
