using Godot;
using Reforged.Presentation;

/// <summary>Build-183: the game's text (GameText) loads first, before any menu or HUD label is made: the install's own
/// tables from the content pack and the repository's catalog of strings the original never had.</summary>
public partial class Main
{
    public const string TextCatalog = "res://Locale/strings.csv";

    public static void LoadGameText()
    {
        GameText.Warn = message => GD.PushWarning(message);
        string original = GamePaths.Data("converted/content/good/text/original.json");
        GameText.Load(original,Godot.FileAccess.GetFileAsString(TextCatalog));
        GD.Print(GameText.HasOriginal ? $"Text: {GameText.Language} from the install ({GameText.Rows.Count} catalog rows)"
            : $"Text: no install text at {original} (python tools/game_text.py --pack converted/content/good); English catalog only");
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
