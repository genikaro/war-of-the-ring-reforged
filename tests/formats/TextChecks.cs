using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Formats;
using Reforged.Install;

/// <summary>Build-193: the text tables and the original text built from them (OriginalText, a port of
/// tools/game_text.py), on data written here and, given an install and game_text.py's output, on the real thing.</summary>
public static class TextChecks
{
    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static byte[] LtxBytes(params (uint Id,string? Text)[] entries)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("LIQDTEXT"u8); w.Write(1f); w.Write(entries.Length);
        foreach (var (id,text) in entries)
        {
            w.Write(id); w.Write((byte)0);
            if (text == null) { w.Write(0); continue; }
            w.Write(text.Length+1); w.Write(Encoding.Unicode.GetBytes(text+"\0"));
        }
        return stream.ToArray();
    }

    public static void Synthetic(Action<bool,string> check)
    {
        var table = Ltx.Parse(LtxBytes((5,"Hello"),(2,null),(9,"Привет<br>%s")));
        check(table.Count == 3 && table[5] == "Hello" && table[2] == "" && table[9] == "Привет<br>%s",
            "LIQDTEXT entries by id, UTF-16 text without its NUL, an empty entry without one");
        check(Throws(() => Ltx.Parse([.. LtxBytes((1,"a")), 0])),"LIQDTEXT rejects trailing bytes");
        check(Throws(() => Ltx.Parse(LtxBytes((1,"a"),(1,"b")))),"LIQDTEXT rejects a repeated id");
        check(Throws(() => Ltx.Parse([.. "LIQDTEXX"u8, .. new byte[8]])),"LIQDTEXT rejects other files");

        check(OriginalText.RuntimeText("A<BR>b %s c %d %.2f 100%%") == "A\nb {0} c {1} {2} 100%",
            "Runtime text: <br> is a newline, printf arguments are numbered, %% is a percent sign");
        string[] stubs = ["","  ","xxx old","ххх","Затычка под описание","Place Of Power Buff 3","Empty description"," Placeholder ","<Spell name>","<Unit Description>"];
        string[] real = ["<играет>","Aragorn","Placeholder text","Name"];
        check(stubs.All(OriginalText.IsPlaceholder) && !real.Any(OriginalText.IsPlaceholder),"Placeholder text: the stubs the shipped tables hold, and nothing else");
        check(OriginalText.TableForFolder("Root\\Units\\Good") == "GameConsole" && OriginalText.TableForFolder("Root\\Hints") == "Game"
            && OriginalText.TableForFolder("Root\\Errors") == "RasterizerErrors" && OriginalText.TableForFolder("Root\\Missions\\M1") == "Menu"
            && OriginalText.TableForFolder("Root\\Missions\\M1\\Team Names") == "Game" && OriginalText.TableForFolder("Root\\Missions\\M1","M1_OBJECTIVE_2") == "Game"
            && Throws(() => OriginalText.TableForFolder("Root\\Other")),"Text table by InterfaceTextIDs editor folder");

        static (string Language,JsonObject Evidence) Detect(string menu,string ini = "")
            => OriginalText.DetectLanguage(new Dictionary<string,Dictionary<int,string>> { ["Menu"] = new() { [1] = menu } },ini);
        check(Detect("Привет мир и всё").Language == "ru" && Detect("Їжак і його друзі є").Language == "uk"
            && Detect("The enemy units of your army").Language == "en" && Detect("Der Feind und die Einheiten ist nicht").Language == "de"
            && Detect("Units","KOR").Language == "ko" && Detect("한국어 텍스트").Language == "ko" && Detect("12 34").Language == "en",
            "Language from the text: script, Ukrainian letters, stop words and Rings.ini");
        var shares = Detect(new string('ж',31)+"a").Evidence["script_shares"]!.AsObject();
        check((double)shares["latin"]! == 0.0312 && (double)shares["cyrillic"]! == 0.9688,"Script shares round half to even, as Python's round does");

        byte[] header = [.. new byte[8], 0, .. BitConverter.GetBytes(3), .. Encoding.Unicode.GetBytes("Ab\0"), 0, .. BitConverter.GetBytes(0),
            .. BitConverter.GetBytes(203u), .. BitConverter.GetBytes(204u)];
        check(OriginalText.WorTextIds(header,"test") == (203,204) && Throws(() => OriginalText.WorTextIds(header[..^1],"test")),
            "Map name and description ids from a .wor header");
    }

    /// <summary>ExplodePrefix on every cut of a source: the right bytes, or -1 (more needed), never anything else.</summary>
    public static void Prefixes(Action<bool,string> check,byte[] imploded,byte[] exploded)
    {
        bool right = true;
        for (int cut = 0; cut <= imploded.Length; cut++)
            for (int length = 1; length <= exploded.Length; length++)
            {
                var output = new byte[length];
                int written = Blast.ExplodePrefix(imploded.AsSpan(0,cut),output,cut == imploded.Length);
                right &= written == -1 && cut < imploded.Length || written == length && output.AsSpan().SequenceEqual(exploded.AsSpan(0,length));
            }
        check(right,"DCL prefix gives the first bytes from any cut of the data, or asks for more");
    }

    public static void Install(string install,string python,Action<bool,string> check)
    {
        var clock = Stopwatch.StartNew();
        var text = OriginalText.FromInstall(install);
        Console.WriteLine($"Built the original text ({text.Language}, {text.Tables.Count} tables, {text.Tables.Sum(t => t.Value.Count)} entries, "
            +$"{text.Records.Count} records, {text.Maps.Count} maps) in {clock.ElapsedMilliseconds} ms");
        var expected = JsonNode.Parse(File.ReadAllText(python,Encoding.UTF8))!;
        var differences = new List<string>();
        JsonCompare.Compare(expected,text.ToJsonNode(),"$",differences);
        var again = OriginalText.FromJson(text.ToJson()).ToJsonNode();
        JsonCompare.Compare(text.ToJsonNode(),again,"(round trip) $",differences);
        check(differences.Count == 0,"The original text equals game_text.py's: tables, symbols, records, maps, abilities, language and stubs"
            +string.Concat(differences.Take(25).Select(d => "\n  "+d)));
    }
}
