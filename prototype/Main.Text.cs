using Godot;
using Reforged.Install;
using Reforged.Presentation;

/// <summary>Build-183: the game's text (GameText) loads first, before any menu or HUD label is made: the install's own
/// tables and the repository's catalog of strings the original never had. Since build-193 the tables are read straight
/// from the install (GamePaths.FindInstall); build-217: without one, only the catalog (the launcher's).</summary>
public partial class Main
{
    public const string TextCatalog = "res://Locale/strings.csv";

    /// <summary>The text last built from an install, and what it was built from: every scene change reloads the text, and
    /// the same install's unchanged files need not be read again.</summary>
    private static (string Key,OriginalText Text)? _installText;

    /// <summary>The text, then (build-194) the rule sections the install gives (LoadInstallRules).</summary>
    public static void LoadGameText()
    {
        string? install = GamePaths.FindInstall();
        LoadText(install);
        LoadInstallRules(install);
    }

    private static void LoadText(string? found)
    {
        GameText.Warn = message => GD.PushWarning(message);
        string catalog = Godot.FileAccess.GetFileAsString(TextCatalog);
        if (found is { } install)
        {
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                string Stamp(params string[] parts) => System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine([install,.. parts])).Ticks.ToString();
                string key = string.Join("|",install,Stamp("Archives","Text.H2O"),Stamp("Archives","Levels.H2O"),Stamp("Database","InterfaceTextIDs.lqr"));
                bool reused = _installText?.Key == key;
                if (!reused) _installText = (key,OriginalText.FromInstall(install));
                GameText.LoadText(_installText!.Value.Text,catalog);
                GD.Print($"Text: {GameText.Language} {(reused ? "kept" : "read")} from the install {install} in {clock.ElapsedMilliseconds} ms ({GameText.Rows.Count} catalog rows)");
                return;
            }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException
                or UnauthorizedAccessException or InvalidCastException or FormatException)
            {
                GD.PushWarning($"Text: cannot read the install {install} ({error.Message}); English catalog only");
            }
        }
        // Build-217: no imported text is read; without an install the launcher speaks from the catalog alone.
        GameText.LoadText(null,catalog);
        if (found == null) GD.Print($"Text: no install found; English catalog only ({GameText.Rows.Count} catalog rows)");
    }

    // The smokes expect what the game shows, in whatever language the install is: a record's name by symbol, an
    // ability's by its rules id, a Fate power's by its id (the English name when the install has none).
    private static string Shown(string symbol,string english) => GameText.Name(symbol,english);
    private static string ShownAbility(int id,string english) => GameText.Ability(id,"name") ?? english;
    private static string ShownPower(int id,string english) => GameText.GlobalPower(id,"name") ?? english;

    // Build-184: tooltips as the panels make them (HudTip's title, cost line and body), for exact or prefix checks.
    // Slot marks an argument whose value a check does not pin: ShownBefore / ShownAfter give the text on either side.
    private const string Slot = "\u0001";
    private static string ShownBefore(string key,params object?[] args) => GameText.T(key,args).Split(Slot)[0];
    private static string ShownAfter(string key,params object?[] args) => GameText.T(key,args).Split(Slot)[^1];
    /// <summary>A construct, train, upgrade or research tooltip's head: "Name [Q]", the cost cells, then the time.</summary>
    private static string ShownTip(string name,string key,string costs,int ticks,string timeLabel = "tip.time")
        => HudTip.Make(HudTip.Keyed(name,key),costs,CommandPanel.Seconds(timeLabel,ticks));
}
