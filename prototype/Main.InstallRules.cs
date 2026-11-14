using Godot;
using Reforged.Install;
using Reforged.Presentation;

/// <summary>Build-194: the rule sections the database gives (factions, team colours, start state, production and Fate,
/// research, places of power, AI tables) are built from the install at startup with PackRules and replace the content
/// pack's (CacheManifest.InstallRules). Without an install, or when it cannot be read, the pack's own are used.</summary>
public partial class Main
{
    /// <summary>The sections last built and what they were built from, kept while the install's tables and AI players,
    /// the text language and the data folder stay the same.</summary>
    private static (string Key,IReadOnlyDictionary<string,System.Text.Json.Nodes.JsonNode> Rules)? _installRules;

    private static void LoadInstallRules(string? install)
    {
        CacheManifest.InstallRules = null;
        if (install == null) { GD.Print("Rules: no install found; using the content pack's"); return; }
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long newest = new[] { "Database","AIPlayers" }.Select(folder => System.IO.Path.Combine(install,folder)).Where(System.IO.Directory.Exists)
                .SelectMany(System.IO.Directory.EnumerateFiles).Select(file => System.IO.File.GetLastWriteTimeUtc(file).Ticks).DefaultIfEmpty().Max();
            string key = string.Join("|",install,GameText.Language,GamePaths.Root,newest);
            bool reused = _installRules?.Key == key;
            if (!reused) _installRules = (key,PackRules.FromInstall(install,GameText.Language,GamePaths.Root));
            CacheManifest.InstallRules = _installRules!.Value.Rules;
            GD.Print($"Rules: {_installRules.Value.Rules.Count} sections {(reused ? "kept" : "built")} from the install in {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException
            or UnauthorizedAccessException or InvalidCastException or FormatException or IndexOutOfRangeException or ArgumentException)
        {
            GD.PushWarning($"Rules: cannot build them from the install {install} ({error.Message}); using the content pack's");
        }
    }
}
