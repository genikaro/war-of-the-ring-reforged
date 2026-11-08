namespace Reforged.Presentation;

/// <summary>Build-184: a console tooltip as the original lays it out (references/original-game/TOOLTIPS-2026-10-21.md):
/// a title band, a row of food / ore / Fate / population cells, the "Description" strip, then the body. Controls keep it
/// in their TooltipText; the cost row travels as a marked line of numbers, so the tooltip box no longer reads costs out
/// of English words and every language lays out the same.</summary>
public static class HudTip
{
    /// <summary>Starts the cost line: resource=amount pairs separated by ';' (food, ore, fate, population).</summary>
    public const char CostMark = '\u001F';
    public static readonly string[] Resources = ["food","ore","fate","population"];

    /// <summary>Title, then the cost line when there is one, then every non-empty body line.</summary>
    public static string Make(string title,string? costs = null,params string?[] body)
        => string.Join("\n",new[] {title,costs}.Concat(body).Where(line => !string.IsNullOrEmpty(line)));

    /// <summary>The title with the button's hotkey, as the console shows it: "Казармы [Q]".</summary>
    public static string Keyed(string title,string key) => $"{title} [{key}]";

    /// <summary>Food and ore always; Fate when it costs any; population when given (the original's recruitment row).</summary>
    public static string Costs(int food,int ore,int fate = 0,int population = -1)
        => CostMark+$"food={food};ore={ore}"+(fate > 0 ? $";fate={fate}" : "")+(population >= 0 ? $";population={population}" : "");

    public static string FateCost(int fate) => CostMark+$"fate={fate}";

    /// <summary>The cells of a cost line in display order, or null when the line is not one.</summary>
    public static (string Resource,string Amount)[]? ParseCosts(string line)
    {
        if (line.Length == 0 || line[0] != CostMark) return null;
        var cells = line[1..].Split(';',StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=',2))
            .Where(pair => pair.Length == 2 && Resources.Contains(pair[0])).Select(pair => (pair[0],pair[1])).ToArray();
        return [.. cells.OrderBy(cell => Array.IndexOf(Resources,cell.Item1))];
    }

    /// <summary>The tooltip as readable text: the cost line written out with the resource labels (logs, checks).</summary>
    public static string Plain(string text)
        => string.Join("\n",text.Split('\n').Select(line => ParseCosts(line) is { } cells
            ? string.Join(" · ",cells.Select(c => $"{c.Amount} {GameText.T("resource."+c.Resource)}")) : line));
}
