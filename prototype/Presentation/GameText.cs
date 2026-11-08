using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reforged.Presentation;

/// <summary>Build-183: every string a player sees, in the language of the player's own install. Original text comes
/// from the install's tables (tools/game_text.py writes converted/content/good/text/original.json; the repository never
/// holds it) and is addressed as `Table:id` (`GameConsole:30`) or by its InterfaceTextIDs symbol without the
/// INTERFACE_TEXT_ID_ prefix (`TOOLTIPS_LEVEL`). Strings the original never had live in prototype/Locale/strings.csv
/// (key, original, en, ru, note): a row with `original` set uses the install's text, the others are custom text in
/// English and Russian, and an install in any other language gets the English. The language is the install's
/// (detected from the text itself); there is no setting. Developer text (debug panel, bot journal, logs) stays English
/// and does not come through here. Plain C#, so the simulation checks can use it without Godot.</summary>
public static class GameText
{
    private static readonly Dictionary<string,Dictionary<int,string>> Tables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,string> Symbols = [];
    private static readonly Dictionary<string,Dictionary<string,string>> Records = [],Maps = [],Abilities = [],GlobalPowers = [];
    private static readonly HashSet<string> Placeholders = [];
    private static readonly Dictionary<string,Row> Catalog = [];
    private static readonly HashSet<string> MissingSet = [];

    public sealed record Row(string Key,string Original,string English,string Russian,string Note);

    /// <summary>The install's language (ru, en, de...); English until the original text is loaded.</summary>
    public static string Language { get; private set; } = "en";
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;
    /// <summary>Whether the install's tables are loaded; without them original refs fall back to custom rows.</summary>
    public static bool HasOriginal { get; private set; }
    public static IReadOnlyDictionary<string,Row> Rows => Catalog;
    /// <summary>Keys, refs and record names asked for that resolved to nothing (LocaleSmoke reports them).</summary>
    public static IReadOnlyCollection<string> Missing => MissingSet;
    /// <summary>Told once per missing text (Main logs it to the Godot console).</summary>
    public static Action<string>? Warn { get; set; }

    /// <summary>Loads the original tables (null or a missing file: none) and the catalog text. Replaces what was loaded.</summary>
    public static void Load(string? originalPath,string catalogCsv)
    {
        Tables.Clear(); Symbols.Clear(); Records.Clear(); Maps.Clear(); Abilities.Clear(); GlobalPowers.Clear(); Placeholders.Clear();
        Catalog.Clear(); MissingSet.Clear(); HasOriginal = false; Language = "en";
        if (originalPath != null && File.Exists(originalPath)) LoadOriginal(File.ReadAllText(originalPath,Encoding.UTF8));
        foreach (var row in ParseCatalog(catalogCsv)) Catalog[row.Key] = row;
        try { Culture = CultureInfo.GetCultureInfo(Language); } catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
    }

    private static void LoadOriginal(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("format_version").GetInt32() != 1) throw new FormatException("Unsupported original text format");
        Language = root.GetProperty("language").GetString() ?? "en";
        foreach (var table in root.GetProperty("tables").EnumerateObject())
        {
            var entries = new Dictionary<int,string>();
            foreach (var entry in table.Value.EnumerateObject()) entries[int.Parse(entry.Name,CultureInfo.InvariantCulture)] = entry.Value.GetString() ?? "";
            Tables[table.Name] = entries;
        }
        foreach (var symbol in root.GetProperty("symbols").EnumerateObject()) Symbols[symbol.Name] = symbol.Value.GetString()!;
        void Fields(string name,Dictionary<string,Dictionary<string,string>> into)
        {
            if (!root.TryGetProperty(name,out var section)) return;
            foreach (var item in section.EnumerateObject())
                into[item.Name] = item.Value.EnumerateObject().ToDictionary(f => f.Name,f => f.Value.GetString()!);
        }
        Fields("records",Records); Fields("maps",Maps); Fields("abilities",Abilities); Fields("global_powers",GlobalPowers);
        foreach (var item in root.GetProperty("placeholders").EnumerateArray()) Placeholders.Add(item.GetString()!);
        HasOriginal = true;
    }

    /// <summary>A catalog string; ⟦key⟧ when the key has no text.</summary>
    public static string T(string key,params object?[] args) => TryT(key,args) ?? Lost(key);

    /// <summary>A catalog string, or null when the key has no text in this language.</summary>
    public static string? TryT(string key,params object?[] args)
    {
        if (!Catalog.TryGetValue(key,out var row)) return null;
        string? text = row.Original != "" ? Original(row.Original) : null;
        text ??= Language == "ru" && row.Russian != "" ? row.Russian : row.English != "" ? row.English : null;
        return text == null ? null : Format(text,args);
    }

    /// <summary>Original text by `Table:id` or symbol; ⟦ref⟧ when the install has none.</summary>
    public static string Ref(string reference,params object?[] args) => TryRef(reference,args) ?? Lost(reference);

    public static string? TryRef(string? reference,params object?[] args) => reference is { Length: > 0 } && Original(reference) is { } text ? Format(text,args) : null;

    /// <summary>The install's text of a reference, skipping the stubs the shipped tables still hold (placeholders).</summary>
    private static string? Original(string reference)
    {
        if (!reference.Contains(':') && !Symbols.TryGetValue(reference,out reference!)) return null;
        int colon = reference.IndexOf(':');
        if (Placeholders.Contains(reference) || !Tables.TryGetValue(reference[..colon],out var table)
            || !int.TryParse(reference[(colon+1)..],NumberStyles.Integer,CultureInfo.InvariantCulture,out int id)) return null;
        return table.TryGetValue(id,out var text) ? text : null;
    }

    /// <summary>A database record's text field (name, title, description, effect, hint) by its symbolic name
    /// (UNIT_G_GONDOR_SWORDSMAN), else the catalog row record.SYMBOL.field, else null.</summary>
    public static string? Record(string symbol,string field)
        => (Records.TryGetValue(symbol,out var fields) && fields.TryGetValue(field,out var reference) ? TryRef(reference) : null)
            ?? TryT($"record.{symbol}.{field}");

    /// <summary>A record's name, or the English internal name (noted as missing) when neither the install nor the catalog has one.</summary>
    public static string Name(string symbol,string fallback) => Record(symbol,"name") ?? Note(symbol,fallback);

    /// <summary>An exported ability's text by its id (BattleGear id, or -1000 - technique id); catalog row ability.ID.field.</summary>
    public static string? Ability(int id,string field)
        => (Abilities.TryGetValue(id.ToString(CultureInfo.InvariantCulture),out var fields) && fields.TryGetValue(field,out var reference) ? TryRef(reference) : null)
            ?? TryT($"ability.{id}.{field}");

    public static string? GlobalPower(int id,string field)
        => (GlobalPowers.TryGetValue(id.ToString(CultureInfo.InvariantCulture),out var fields) && fields.TryGetValue(field,out var reference) ? TryRef(reference) : null)
            ?? TryT($"global_power.{id}.{field}");

    /// <summary>A multiplayer map's name or description by file stem (MP02_Bilbos_Bluffs), from MultiPlayerMaps.ltx.</summary>
    public static string? Map(string stem,string field)
        => (Maps.TryGetValue(stem,out var fields) && fields.TryGetValue(field,out var reference) ? TryRef(reference) : null)
            ?? TryT($"map.{stem}.{field}");

    /// <summary>The plural form of a catalog key for a count: key.one / key.few / key.many in Russian, key.one /
    /// key.other in English; a form the catalog lacks falls back to key.other, then key.many.</summary>
    public static string Plural(string key,long count,params object?[] args)
    {
        string form = PluralForm(Language,count);
        return TryT($"{key}.{form}",args) ?? TryT($"{key}.other",args) ?? TryT($"{key}.many",args) ?? Lost($"{key}.{form}");
    }

    public static string PluralForm(string language,long count)
    {
        long n = Math.Abs(count);
        if (language is "ru" or "uk")
            return n%10 == 1 && n%100 != 11 ? "one" : n%10 is >= 2 and <= 4 && n%100 is < 12 or > 14 ? "few" : "many";
        return n == 1 ? "one" : "other";
    }

    /// <summary>A number in the text language (1,5 in Russian, 1.5 in English) with a .NET format such as "0.#".</summary>
    public static string Number(decimal value,string format = "0.##") => value.ToString(format,Culture);

    /// <summary>"Label: value" with a catalog label, the shape Russian text needs to keep names undeclined.</summary>
    public static string Labeled(string key,object? value) => $"{T(key)}: {(value is decimal d ? Number(d) : value)}";

    private static readonly Regex Argument = new(@"\{(\d)\}",RegexOptions.Compiled);

    /// <summary>{0}..{9} replaced by the arguments, numbers in the text language's culture. No other brace syntax:
    /// original text may hold braces of its own.</summary>
    public static string Format(string text,params object?[] args)
        => args.Length == 0 ? text : Argument.Replace(text,m => int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture) is var i && i < args.Length
            ? args[i] is IFormattable value ? value.ToString(null,Culture) : args[i]?.ToString() ?? "" : m.Value);

    private static string Lost(string key) { Note(key,""); return $"⟦{key}⟧"; }

    private static string Note(string key,string fallback)
    {
        if (MissingSet.Add(key)) Warn?.Invoke($"No text for {key}");
        return fallback;
    }

    /// <summary>RFC 4180 CSV with a header row naming the columns key, original, en, ru, note (any order); quoted fields may
    /// hold commas, quotes ("") and line breaks. Rows whose key is empty or starts with # are comments.</summary>
    public static List<Row> ParseCatalog(string csv)
    {
        var records = ReadCsv(csv);
        if (records.Count == 0) return [];
        var header = records[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Column(string name) => header.IndexOf(name) is var i and >= 0 ? i : throw new FormatException($"strings.csv has no '{name}' column");
        int key = Column("key"),original = Column("original"),en = Column("en"),ru = Column("ru"),note = header.IndexOf("note");
        var rows = new List<Row>();
        var seen = new HashSet<string>();
        foreach (var record in records.Skip(1))
        {
            string Cell(int index) => index >= 0 && index < record.Count ? record[index] : "";
            string id = Cell(key).Trim();
            if (id == "" || id.StartsWith('#')) continue;
            if (!seen.Add(id)) throw new FormatException($"strings.csv repeats the key {id}");
            rows.Add(new(id,Cell(original).Trim(),Cell(en),Cell(ru),Cell(note)));
        }
        return rows;
    }

    private static List<List<string>> ReadCsv(string csv)
    {
        var records = new List<List<string>>(); var record = new List<string>(); var cell = new StringBuilder();
        bool quoted = false,any = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (quoted)
            {
                if (c == '"' && i+1 < csv.Length && csv[i+1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; any = true; break;
                case ',': record.Add(cell.ToString()); cell.Clear(); any = true; break;
                case '\r': break;
                case '\n': record.Add(cell.ToString()); cell.Clear(); records.Add(record); record = []; any = false; break;
                default: cell.Append(c); any = true; break;
            }
        }
        if (any || cell.Length > 0 || record.Count > 0) { record.Add(cell.ToString()); records.Add(record); }
        if (quoted) throw new FormatException("strings.csv ends inside a quoted field");
        return records;
    }
}
