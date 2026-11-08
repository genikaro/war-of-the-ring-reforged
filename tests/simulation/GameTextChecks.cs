using Reforged.Presentation;

/// <summary>Build-183: the game's text (GameText) on a synthetic install table, and the repository catalog's rules: every
/// custom row has English and Russian, original rows name a table:id or a symbol, and Russian avoids the letter yo.</summary>
static class GameTextChecks
{
    private const string Original = """
        {"format_version": 1, "language": "ru", "evidence": {},
         "tables": {"GameConsole": {"30": "Гондорский мечник", "4000": "Затычка", "8300": "Уровень", "9000": "Вы будете обнаружены через {0} сек."},
                    "Menu": {"12": "Сражение"}, "MultiPlayerMaps": {"203": "Хитрый Бильбо"}},
         "symbols": {"TOOLTIPS_LEVEL": "GameConsole:8300"},
         "records": {"UNIT_G_GONDOR_SWORDSMAN": {"name": "GameConsole:30"}, "OBJECT_STUB": {"name": "GameConsole:4000"}},
         "maps": {"MP02_Bilbos_Bluffs": {"name": "MultiPlayerMaps:203"}},
         "abilities": {"-1022": {"name": "GameConsole:30"}}, "global_powers": {},
         "placeholders": ["GameConsole:4000"]}
        """;

    private const string Catalog = """
        key,original,en,ru,note
        "# a comment, with a comma"
        menu.skirmish,Menu:12,,,Skirmish
        tip.level,TOOLTIPS_LEVEL,,,Level
        tip.missing,Menu:999,Fallback,Запасной,original missing: custom text instead
        unit.count.one,,{0} unit,{0} отряд,
        unit.count.few,,,{0} отряда,
        unit.count.many,,,{0} отрядов,
        unit.count.other,,{0} units,,
        quoted,,"Say ""hi"", then
        go",Скажи,multi-line
        record.OBJECT_STUB.name,,Stub object,Заглушка,
        """;

    public static void Run(Action<bool,string> check)
    {
        string path = Path.Combine(Path.GetTempPath(),$"game-text-{Environment.ProcessId}.json");
        File.WriteAllText(path,Original);
        try
        {
            GameText.Load(path,Catalog);
            check(GameText.HasOriginal && GameText.Language == "ru" && GameText.Culture.Name == "ru","the install's language comes with its tables");
            check(GameText.T("menu.skirmish") == "Сражение" && GameText.T("tip.level") == "Уровень","catalog rows show original text by table:id and by symbol");
            check(GameText.T("tip.missing") == "Запасной","an original ref the install lacks falls back to the row's custom text");
            check(GameText.T("quoted") == "Скажи" && GameText.Rows["quoted"].English == "Say \"hi\", then\ngo","quoted CSV cells keep commas, quotes and line breaks");
            check(!GameText.Rows.ContainsKey("# a comment, with a comma"),"comment rows are skipped");
            check(GameText.Ref("GameConsole:9000",12) == "Вы будете обнаружены через 12 сек." && GameText.Format("{0} / {1}",1.5,"x") == "1,5 / x",
                "arguments fill numbered slots, decimals in the text language");
            check(GameText.Name("UNIT_G_GONDOR_SWORDSMAN","Gondor Swordsman") == "Гондорский мечник","a record's name is the install's");
            check(GameText.Name("OBJECT_STUB","Stub") == "Заглушка","a stub original name falls back to the catalog's record row");
            check(GameText.Name("UNIT_UNKNOWN","Unknown Unit") == "Unknown Unit" && GameText.Missing.Contains("UNIT_UNKNOWN"),
                "a record with no text keeps its English name and is reported missing");
            check(GameText.T("no.such.key") == "⟦no.such.key⟧" && GameText.Missing.Contains("no.such.key"),"a missing key shows itself bracketed");
            check(GameText.Map("MP02_Bilbos_Bluffs","name") == "Хитрый Бильбо" && GameText.Ability(-1022,"name") == "Гондорский мечник" && GameText.Ability(5,"name") == null,
                "maps and abilities by id");
            check(GameText.Plural("unit.count",1,1) == "1 отряд" && GameText.Plural("unit.count",3,3) == "3 отряда" && GameText.Plural("unit.count",11,11) == "11 отрядов"
                && GameText.Plural("unit.count",21,21) == "21 отряд" && GameText.Plural("unit.count",112,112) == "112 отрядов","Russian plural forms");
            check(GameText.PluralForm("en",1) == "one" && GameText.PluralForm("en",0) == "other" && GameText.PluralForm("en",2) == "other","English plural forms");
            GameText.Load(null,Catalog);
            check(!GameText.HasOriginal && GameText.Language == "en" && GameText.T("menu.skirmish") == "⟦menu.skirmish⟧" && GameText.T("tip.missing") == "Fallback"
                && GameText.Plural("unit.count",2,2) == "2 units","without an install, custom English text only");
        }
        finally { File.Delete(path); }
        CatalogRules(check);
    }

    /// <summary>The repository's catalog: complete custom rows, well-formed original refs, no yo in Russian.</summary>
    private static void CatalogRules(Action<bool,string> check)
    {
        string file = Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","prototype","Locale","strings.csv");
        var rows = GameText.ParseCatalog(File.ReadAllText(file));
        check(rows.Count > 0,"the catalog has rows");
        var custom = rows.Where(r => r.Original == "").ToArray();
        // Plural rows may leave a form a language has no use for: English has no few/many, Russian no other.
        bool Complete(GameText.Row r) => r.Key.EndsWith(".few") || r.Key.EndsWith(".many") ? r.Russian != ""
            : r.Key.EndsWith(".other") ? r.English != "" : r.English != "" && r.Russian != "";
        var incomplete = custom.Where(r => !Complete(r)).Select(r => r.Key).ToArray();
        check(incomplete.Length == 0,"every custom catalog row has English and Russian: "+string.Join(", ",incomplete));
        var refs = rows.Where(r => r.Original != "" && !System.Text.RegularExpressions.Regex.IsMatch(r.Original,@"^([A-Za-z_]+:\d+|[A-Z0-9_]+)$")).Select(r => r.Key).ToArray();
        check(refs.Length == 0,"original refs are Table:id or a symbol: "+string.Join(", ",refs));
        var yo = rows.Where(r => r.Russian.Contains('ё') || r.Russian.Contains('Ё')).Select(r => r.Key).ToArray();
        check(yo.Length == 0,"Russian catalog text avoids the letter yo, which the menu font lacks: "+string.Join(", ",yo));
    }
}
