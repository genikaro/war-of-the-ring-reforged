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
}
