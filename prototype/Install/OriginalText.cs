using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-193: the original game's text as GameText reads it, built straight from the player's install at
/// startup: a port of tools/game_text.py's build(), giving what it writes to the content pack's text/original.json
/// (tests/formats compares the two). Text.H2O's 49 LIQDTEXT tables, with `&lt;br&gt;` made a newline and printf
/// arguments numbered {0}, {1}...; the InterfaceTextIDs symbols and the records, multiplayer maps, abilities and Fate
/// powers that name a text, all as `Table:id`; the stubs the shipped tables still hold; and the language, read from the
/// text itself. game_text.py's docstring explains the indirection and the language evidence.</summary>
public sealed class OriginalText
{
    public const int FormatVersion = 1;
    /// <summary>Where the import writes it in the content pack, for a game started without the install.</summary>
    public const string PackFile = "text/original.json";
    public const string SymbolPrefix = "INTERFACE_TEXT_ID_";
    /// <summary>A unit ability a technique grants without gear of its own has id -1000 - technique id (Elven Cloaks -1022).</summary>
    public const int TechniqueAbilityBase = -1000;

    public string Language { get; private init; } = "en";
    public JsonObject Evidence { get; private init; } = [];
    public string Source { get; private init; } = "";
    public string SourceSha256 { get; private init; } = "";
    /// <summary>Runtime text by table name and id.</summary>
    public SortedDictionary<string,SortedDictionary<int,string>> Tables { get; } = new(StringComparer.Ordinal);
    /// <summary>`Table:id` by InterfaceTextIDs symbol without the INTERFACE_TEXT_ID_ prefix.</summary>
    public SortedDictionary<string,string> Symbols { get; } = new(StringComparer.Ordinal);
    /// <summary>{field: `Table:id`} by record symbol, map file stem, ability id and Fate power id.</summary>
    public SortedDictionary<string,Dictionary<string,string>> Records { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,Dictionary<string,string>> Maps { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,Dictionary<string,string>> Abilities { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string,Dictionary<string,string>> GlobalPowers { get; } = new(StringComparer.Ordinal);
    public List<string> DanglingSymbols { get; } = [];
    public List<string> Placeholders { get; } = [];

    // ---- Reading the install ---------------------------------------------------------------------------------------

    public static OriginalText FromInstall(string install)
    {
        using var archives = GameArchives.OpenInstall(install);
        return Build(install,archives,LqrDatabase.OpenInstall(install));
    }

    public static OriginalText Build(string install,GameArchives archives,LqrDatabase db)
    {
        var archive = archives.Archive("Text") ?? throw new FileNotFoundException($"No Archives\\Text.H2O in {install}");
        var raw = new SortedDictionary<string,Dictionary<int,string>>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries.Where(e => e.Path.EndsWith(".ltx",StringComparison.OrdinalIgnoreCase)))
            raw[Path.GetFileNameWithoutExtension(entry.Path.Replace('\\','/'))] = Ltx.Parse(archive.Read(entry));
        string digest;
        using (var stream = File.OpenRead(archive.FilePath)) digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        var (language,evidence) = DetectLanguage(raw,IniLanguage(install));
        var text = new OriginalText { Language = language,Evidence = evidence,Source = Path.GetFileName(archive.FilePath),SourceSha256 = digest };
        foreach (var (name,entries) in raw)
        {
            var table = text.Tables[name] = [];
            foreach (var (id,value) in entries)
            {
                table[id] = RuntimeText(value);
                if (IsPlaceholder(value)) text.Placeholders.Add($"{name}:{id}");
            }
        }
        text.Placeholders.Sort(StringComparer.Ordinal);
        bool Exists(string reference)
        {
            int colon = reference.IndexOf(':');
            return raw.TryGetValue(reference[..colon],out var table) && table.ContainsKey(int.Parse(reference[(colon+1)..],CultureInfo.InvariantCulture));
        }

        var byRecord = new Dictionary<int,string>();
        foreach (var record in db["InterfaceTextIDs"].Records)
        {
            string reference = $"{TableForFolder(record.Folder,record.Name)}:{record.Int("InterfaceTextIDNumber")}";
            byRecord[record.Id] = reference;
            text.Symbols[record.Name.StartsWith(SymbolPrefix,StringComparison.Ordinal) ? record.Name[SymbolPrefix.Length..] : record.Name] = reference;
        }
        text.DanglingSymbols.AddRange(text.Symbols.Where(p => !Exists(p.Value)).Select(p => p.Key));

        foreach (var (table,columns) in RecordColumns)
            foreach (var record in db[table].Records)
            {
                var fields = new Dictionary<string,string>();
                foreach (var (field,column) in columns)
                    if (byRecord.TryGetValue(Value(record,column),out string? reference) && Exists(reference)) fields[field] = reference;
                if (fields.Count > 0) text.Records[record.Name] = fields;
            }
        foreach (var (table,(textTable,columns)) in DirectColumns)
            foreach (var record in db[table].Records)
            {
                var fields = new Dictionary<string,string>();
                foreach (var (field,column) in columns)
                    if (Value(record,column) is int id and >= 0 && raw.TryGetValue(textTable,out var entries) && entries.ContainsKey(id)) fields[field] = $"{textTable}:{id}";
                if (fields.Count > 0) text.Records[record.Name] = fields;
            }

        if (archives.Archive("Levels") is { } levels)
            foreach (var entry in levels.Entries)
            {
                string folder = Path.GetDirectoryName(entry.Path.Replace('\\','/')) ?? "", file = Path.GetFileName(entry.Path.Replace('\\','/'));
                if (!folder.Equals("Multiplayer",StringComparison.OrdinalIgnoreCase) || !file.StartsWith("MP",StringComparison.OrdinalIgnoreCase)
                    || !file.EndsWith(".wor",StringComparison.OrdinalIgnoreCase)) continue;
                var (name,description) = WorTextIds(levels.ReadPrefix(entry,4096),entry.Path);
                var fields = new Dictionary<string,string>();
                var maps = raw.GetValueOrDefault("MultiPlayerMaps") ?? [];
                if (maps.ContainsKey(name)) fields["name"] = $"MultiPlayerMaps:{name}";
                if (maps.ContainsKey(description)) fields["description"] = $"MultiPlayerMaps:{description}";
                if (fields.Count > 0) text.Maps[Path.GetFileNameWithoutExtension(file)] = fields;
            }

        foreach (var record in db["BattleGear"].Records)
            if (text.Records.TryGetValue(record.Name,out var fields)) text.Abilities[record.Id.ToString(CultureInfo.InvariantCulture)] = fields;
        foreach (var record in db["Techniques"].Records)
            if (text.Records.TryGetValue(record.Name,out var fields)) text.Abilities[(TechniqueAbilityBase-record.Id).ToString(CultureInfo.InvariantCulture)] = fields;
        foreach (var record in db["GlobalSpells"].Records)
            if (text.Records.TryGetValue(record.Name,out var fields)) text.GlobalPowers[record.Id.ToString(CultureInfo.InvariantCulture)] = fields;
        return text;
    }

    /// <summary>A record's int column, -1 when the table has no such column (record.get(column, -1)).</summary>
    private static int Value(LqrRecord record,string column) => record.Table.HasColumn(column) ? (int)record[column] : -1;

    /// <summary>Database tables whose records name their text through InterfaceTextIDs: field -> column. Spells are
    /// left out: their names are stale placeholders; hero abilities take BattleGear's.</summary>
    private static readonly (string Table,(string Field,string Column)[] Columns)[] RecordColumns =
    [
        ("Units",[("name","InterfaceTextIDUnitName"),("title","InterfaceTextIDUnitSubName"),("description","InterfaceTextIDUnitDescription"),("hint","InterfaceTextIDHint")]),
        ("Buildings",[("name","InterfaceTextIDName"),("description","InterfaceTextIDDescription"),("hint","InterfaceTextIDHint")]),
        ("Techniques",[("name","InterfaceTextIDName"),("description","InterfaceTextIDDescription"),("hint","InterfaceTextIDHint")]),
        ("BattleGear",[("name","InterfaceTextIDName"),("description","InterfaceTextIDDescription"),("effect","InterfaceTextIDBenefit"),("hint","InterfaceTextIDHint")]),
        ("GlobalSpells",[("name","InterfaceTextIDName"),("description","InterfaceTextIDDescription"),("hint","InterfaceTextIDHint")]),
        ("Objects",[("name","InterfaceTextIDName"),("description","InterfaceTextIDDescription")]),
        ("PlacesOfPower",[("effect","InterfaceTextIDBenefit")]),
        ("Missions",[("name","InterfaceTextIDName"),("description","InterfaceTextIDDescription"),("epilogue","InterfaceTextIDEpilogue")]),
    ];

    /// <summary>Columns that hold an .ltx id directly (-1 none): table -> (text table, field -> column).</summary>
    private static readonly (string Table,(string TextTable,(string Field,string Column)[] Columns))[] DirectColumns =
    [
        ("MultiplayerGameModes",("Menu",[("name","LocalizedNameTextID")])),
        ("StatsScreenAwards",("Menu",[("name","AwardTextID")])),
        ("Hotkeys",("Hotkeys_Text",[("name","InterfaceTextID")])),
        ("HotKeyGroups",("Hotkeys_Text",[("name","InterfaceTextID")])),
        ("Actors",("Game",[("name","ActorNameTextID")])),
        ("MusicTracks",("Game",[("name","TextID")])),
    ];

    /// <summary>The .ltx table an InterfaceTextIDs record points into, from its editor folder (game_text.py
    /// table_for_folder, verified against every record).</summary>
    public static string TableForFolder(string folder,string symbol = "")
    {
        string[] parts = folder.Split('\\');
        string top = parts.Length > 1 ? parts[1] : "";
        switch (top)
        {
            case "Units" or "Buildings" or "Techniques" or "Battlegear" or "Objects" or "Global Spells" or "Console": return "GameConsole";
            case "Hints" or "Game Messages" or "Voice Message" or "In Game Menus": return "Game";
            case "Errors": return "RasterizerErrors";
            // Objectives, tutorial steps and team names live in Game.ltx; mission names, briefings and epilogues in Menu.ltx.
            case "Missions": return symbol.Contains("_OBJECTIVE",StringComparison.Ordinal) || parts[^1].Replace(" ","") == "TeamNames" ? "Game" : "Menu";
            default: throw new InvalidDataException($"No text table known for folder {folder}");
        }
    }

    /// <summary>(name id, description id) in MultiPlayerMaps.ltx from a .wor header: two u32 versions, the editor's
    /// English name and description as world strings, then the two ids.</summary>
    public static (int Name,int Description) WorTextIds(byte[] header,string where)
    {
        int position = 8;
        void Need(int bytes) { if (position+bytes > header.Length) throw new InvalidDataException($"{where}: header ends at 0x{header.Length:x}"); }
        for (int i = 0; i < 2; i++)
        {
            Need(5);
            if (header[position] != 0) throw new InvalidDataException($"{where}: invalid world string marker at 0x{position:x}");
            long count = BitConverter.ToUInt32(header,position+1);
            position += 5;
            Need((int)Math.Min(int.MaxValue,2*count));
            if (count > 0 && (header[position+(int)(2*count)-2] != 0 || header[position+(int)(2*count)-1] != 0))
                throw new InvalidDataException($"{where}: unterminated world string at 0x{position:x}");
            position += (int)(2*count);
        }
        Need(8);
        return ((int)BitConverter.ToUInt32(header,position),(int)BitConverter.ToUInt32(header,position+4));
    }

    // ---- Text forms ------------------------------------------------------------------------------------------------

    private static readonly Regex LineBreak = new("<br>",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Printf = new(@"%%|%(?:\.\d+)?[sdf]",RegexOptions.CultureInvariant);

    /// <summary>The form GameText reads: `&lt;br&gt;` is a newline and printf arguments are numbered {0}, {1}... in order.</summary>
    public static string RuntimeText(string text)
    {
        int index = 0;
        return Printf.Replace(LineBreak.Replace(text,"\n"),m => m.Value == "%%" ? "%" : "{"+(index++).ToString(CultureInfo.InvariantCulture)+"}");
    }

    private static readonly Regex Placeholder = new(@"^\s*$|^\s*(?:xxx|ххх)|Затычка|Place Of Power Buff|^\s*(?:Empty description|Placeholder)\s*$|"
        +@"^<[^<>]*(?:Benefit|Description|Name|Название|Описание)[^<>]*>$",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Stub entries the shipped tables still carry (game_text.py is_placeholder): empty, "xxx"/"ххх", "Затычка",
    /// `&lt;Spell name&gt;` style names, "&lt;... Benefit&gt;", "Place Of Power Buff N", "Empty description", "Placeholder".</summary>
    public static bool IsPlaceholder(string text) => Placeholder.IsMatch(text);

    // ---- Language ----------------------------------------------------------------------------------------------------

    private static readonly (string Language,string[] Words)[] StopWords =
    [
        ("en",["the","of","and","to","your","is","with","for","this","can","units","enemy"]),
        ("de",["der","die","und","das","ist","mit","den","zu","nicht","ein","eine","für"]),
        ("fr",["le","la","les","et","des","du","est","une","pour","vos","votre","avec"]),
        ("es",["el","los","las","y","del","que","con","una","para","por","sus","su"]),
        ("it",["il","di","e","che","per","con","gli","una","della","delle","sono","nemico"]),
        ("pl",["i","w","z","na","się","nie","do","jest","oraz","jednostki","twoje","przez"]),
    ];
    private static readonly Dictionary<string,string> IniLanguages = new() { ["KOR"] = "ko", ["TCH"] = "zh-Hant", ["SCH"] = "zh-Hans" };

    private static string ScriptOf(char c) => c switch
    {
        >= '\u0400' and <= '\u04FF' => "cyrillic",
        >= '\uAC00' and <= '\uD7AF' or >= '\u1100' and <= '\u11FF' => "hangul",
        >= '\u3040' and <= '\u30FF' => "kana",
        >= '\u4E00' and <= '\u9FFF' => "han",
        < '\u0080' or >= '\u00C0' and <= '\u024F' => "latin",
        _ => "other",
    };

    /// <summary>Python's [^\W\d_]: letters and the numeric characters that are not decimal digits.</summary>
    private static bool WordChar(char c) => char.IsLetter(c) || char.IsNumber(c) && !char.IsDigit(c);

    /// <summary>(language code, evidence) from what the player-facing tables (Menu, GameConsole, Game) say
    /// (game_text.py detect_language): Cyrillic is Russian, or Ukrainian when і/ї/є/ґ words pass 5%; Hangul Korean, kana
    /// Japanese, Han Chinese (Rings.ini's TCH/SCH picks the script); Latin text by stop words.</summary>
    public static (string Language,JsonObject Evidence) DetectLanguage(IReadOnlyDictionary<string,Dictionary<int,string>> tables,string iniLanguage)
    {
        var letters = new SortedDictionary<string,long>(StringComparer.Ordinal);
        var words = new Dictionary<string,long>();
        foreach (string name in new[] { "Menu","GameConsole","Game" })
        {
            if (!tables.TryGetValue(name,out var table)) continue;
            foreach (string value in table.Values)
            {
                foreach (char c in value)
                    if (char.IsLetter(c)) { string script = ScriptOf(c); letters[script] = letters.GetValueOrDefault(script)+1; }
                string lower = value.ToLowerInvariant();
                for (int i = 0; i < lower.Length;)
                {
                    if (!WordChar(lower[i])) { i++; continue; }
                    int start = i;
                    while (i < lower.Length && WordChar(lower[i])) i++;
                    string word = lower[start..i];
                    words[word] = words.GetValueOrDefault(word)+1;
                }
            }
        }
        long total = Math.Max(1,letters.Values.Sum());
        // round(count / total, 4): the exact quotient rounded half to even, as Python rounds the double.
        var shares = letters.ToDictionary(p => p.Key,p => (double)Math.Round((decimal)p.Value/total,4,MidpointRounding.ToEven));
        var shareNode = new JsonObject();
        foreach (var (script,share) in shares) shareNode[script] = share;
        var evidence = new JsonObject { ["script_shares"] = shareNode,["ini_language"] = iniLanguage };
        string? ini = IniLanguages.GetValueOrDefault(iniLanguage.Trim().ToUpperInvariant());
        double Share(string script) => shares.GetValueOrDefault(script);
        if (Share("cyrillic") >= .5)
        {
            long ukrainian = words.Where(p => p.Key.IndexOfAny(['і','ї','є','ґ']) >= 0).Sum(p => p.Value);
            evidence["ukrainian_words"] = ukrainian;
            return (ukrainian > words.Values.Sum()*.05 ? "uk" : "ru",evidence);
        }
        if (Share("hangul") >= .3) return ("ko",evidence);
        if (Share("kana") >= .1) return ("ja",evidence);
        if (Share("han") >= .3) return (ini ?? "zh-Hans",evidence);
        if (ini != null) return (ini,evidence);
        var votes = new JsonObject();
        (string Language,long Votes) best = ("",-1);
        foreach (var (language,stop) in StopWords)
        {
            long count = stop.Sum(w => words.GetValueOrDefault(w));
            votes[language] = count;
            if (count > best.Votes) best = (language,count);
        }
        evidence["stopword_votes"] = votes;
        return (best.Votes > 0 ? best.Language : "en",evidence);
    }

    /// <summary>Rings.ini [Localization] Language, read as Latin-1; empty without one.</summary>
    public static string IniLanguage(string install)
    {
        string path = Path.Combine(install,"Rings.ini");
        if (!File.Exists(path)) return "";
        string section = "";
        foreach (string raw in File.ReadAllText(path,Encoding.Latin1).ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) section = line.Trim('[',']').Trim().ToLowerInvariant();
            else if (section == "localization" && line.IndexOf('=') is int equals and >= 0
                && line[..equals].Trim().Equals("language",StringComparison.OrdinalIgnoreCase)) return line[(equals+1)..].Trim();
        }
        return "";
    }

    // ---- JSON: the content pack's text/original.json -----------------------------------------------------------------

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true,IndentSize = 1,Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public JsonObject ToJsonNode()
    {
        static JsonObject Fields(SortedDictionary<string,Dictionary<string,string>> items)
        {
            var node = new JsonObject();
            foreach (var (key,fields) in items) node[key] = new JsonObject(fields.Select(f => KeyValuePair.Create(f.Key,(JsonNode?)f.Value)));
            return node;
        }
        var tables = new JsonObject();
        foreach (var (name,entries) in Tables)
            tables[name] = new JsonObject(entries.Select(e => KeyValuePair.Create(e.Key.ToString(CultureInfo.InvariantCulture),(JsonNode?)e.Value)));
        return new JsonObject
        {
            ["format_version"] = FormatVersion,["language"] = Language,["evidence"] = Evidence.DeepClone(),["source"] = Source,["source_sha256"] = SourceSha256,
            ["tables"] = tables,["symbols"] = new JsonObject(Symbols.Select(s => KeyValuePair.Create(s.Key,(JsonNode?)s.Value))),
            ["records"] = Fields(Records),["maps"] = Fields(Maps),["abilities"] = Fields(Abilities),["global_powers"] = Fields(GlobalPowers),
            ["dangling_symbols"] = new JsonArray([.. DanglingSymbols.Select(s => (JsonNode?)s)]),
            ["placeholders"] = new JsonArray([.. Placeholders.Select(s => (JsonNode?)s)]),
        };
    }

    public string ToJson() => ToJsonNode().ToJsonString(Indented);

    public static OriginalText FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("format_version").GetInt32() != FormatVersion) throw new FormatException("Unsupported original text format");
        string Text(string name) => root.TryGetProperty(name,out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var text = new OriginalText
        {
            Language = root.TryGetProperty("language",out var language) ? language.GetString() ?? "en" : "en",
            Evidence = root.TryGetProperty("evidence",out var evidence) ? JsonNode.Parse(evidence.GetRawText())!.AsObject() : [],
            Source = Text("source"),SourceSha256 = Text("source_sha256"),
        };
        foreach (var table in root.GetProperty("tables").EnumerateObject())
        {
            var entries = text.Tables[table.Name] = [];
            foreach (var entry in table.Value.EnumerateObject()) entries[int.Parse(entry.Name,CultureInfo.InvariantCulture)] = entry.Value.GetString() ?? "";
        }
        foreach (var symbol in root.GetProperty("symbols").EnumerateObject()) text.Symbols[symbol.Name] = symbol.Value.GetString()!;
        void Fields(string name,SortedDictionary<string,Dictionary<string,string>> into)
        {
            if (!root.TryGetProperty(name,out var section)) return;
            foreach (var item in section.EnumerateObject()) into[item.Name] = item.Value.EnumerateObject().ToDictionary(f => f.Name,f => f.Value.GetString()!);
        }
        Fields("records",text.Records); Fields("maps",text.Maps); Fields("abilities",text.Abilities); Fields("global_powers",text.GlobalPowers);
        if (root.TryGetProperty("dangling_symbols",out var dangling)) text.DanglingSymbols.AddRange(dangling.EnumerateArray().Select(s => s.GetString()!));
        text.Placeholders.AddRange(root.GetProperty("placeholders").EnumerateArray().Select(s => s.GetString()!));
        return text;
    }
}
