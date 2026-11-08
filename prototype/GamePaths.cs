using Godot;

/// <summary>Where the owner's converted and extracted game data live (build-147). Run from the project (run.ps1, the
/// editor binary) it is the repository above the project folder. The exported game (build/WarOfTheRingReforged.exe)
/// looks for converted/ (or, before the launcher's first import, tools/import_install.py) beside its executable and
/// then in each folder above it, so a build inside the repository finds the repository's data; --data=&lt;folder&gt;
/// names the folder outright. The data is never packed into the game.</summary>
public static class GamePaths
{
    private static string? _root;

    public static string Root => _root ??= FindRoot();
    public static string Converted => System.IO.Path.Combine(Root,"converted");
    public static string Extracted => System.IO.Path.Combine(Root,"extracted");
    public static string Data(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root,relative));
    public static bool Exported => !OS.HasFeature("editor");

    private static string FindRoot()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--data=")) return System.IO.Path.GetFullPath(arg[7..]);
        if (!Exported) return System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://.."));
        string start = System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;
        for (var folder = new System.IO.DirectoryInfo(start); folder != null; folder = folder.Parent)
            if (System.IO.Directory.Exists(System.IO.Path.Combine(folder.FullName,"converted"))
                || System.IO.File.Exists(System.IO.Path.Combine(folder.FullName,"tools","import_install.py"))) return folder.FullName;
        return start;
    }
}
