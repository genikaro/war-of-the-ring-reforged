using System.Text;
using Reforged.Formats;

/// <summary>Build-192: the format readers on data written here, so they run without the original game.</summary>
public static class SyntheticChecks
{
    // blast.c's own example: "AIAIAIAIAIAIA" imploded.
    internal static readonly byte[] Imploded = [0x00, 0x04, 0x82, 0x24, 0x25, 0x8f, 0x80, 0x7f];
    internal static readonly byte[] Exploded = "AIAIAIAIAIAIA"u8.ToArray();

    public static void Run(Action<bool,string> check)
    {
        BlastChecks(check);
        TextChecks.Prefixes(check,Imploded,Exploded);
        CrcChecks(check);
        string folder = Path.Combine(Path.GetTempPath(),"reforged-formats-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            ArchiveChecks(check,folder);
            TableChecks(check,folder);
            TextChecks.Synthetic(check);
            RulesChecks.Synthetic(check);
            ModelChecks.Synthetic(check);
        }
        finally { Directory.Delete(folder,true); }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or KeyNotFoundException) { return true; }
    }

    private static void BlastChecks(Action<bool,string> check)
    {
        var output = new byte[Exploded.Length];
        Blast.Explode(Imploded,output);
        check(output.AsSpan().SequenceEqual(Exploded),"DCL explode decodes blast.c's example");
        check(Throws(() => Blast.Explode(Imploded,new byte[Exploded.Length+1])),"DCL explode rejects data that ends before the expected size");
        check(Throws(() => Blast.Explode(Imploded,new byte[Exploded.Length-1])),"DCL explode rejects data that runs past the expected size");
        check(Throws(() => Blast.Explode([0x02, 0x04, 0, 0],new byte[4])),"DCL explode rejects a bad literal flag");
        check(Throws(() => Blast.Explode([0x00, 0x07, 0, 0],new byte[4])),"DCL explode rejects a bad dictionary size");
    }

    private static uint BitwiseCrc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    private static void CrcChecks(Action<bool,string> check)
    {
        check(Crc32.Compute("123456789"u8) == 0xCBF43926u,"CRC-32 gives the standard check value");
        var random = new Random(192);
        var data = new byte[1000];
        random.NextBytes(data);
        bool same = true;
        for (int length = 0; length <= 40; length++) same &= Crc32.Compute(data.AsSpan(0,length)) == BitwiseCrc(data.AsSpan(0,length));
        same &= Crc32.Compute(data) == BitwiseCrc(data);
        check(same,"CRC-32 by eight bytes equals the bit-by-bit CRC for every length");
    }

    private static byte[] StringBlob(params string[] strings)
    {
        var text = Encoding.Unicode.GetBytes(string.Concat(strings.Select(s => s+"\0")));
        var blob = new List<byte>();
        blob.AddRange(BitConverter.GetBytes(strings.Length)); blob.AddRange(BitConverter.GetBytes(text.Length)); blob.AddRange(text);
        // Stored: compressed size equals raw size.
        return [.. BitConverter.GetBytes(blob.Count), .. BitConverter.GetBytes(blob.Count), .. BitConverter.GetBytes(Crc32.Compute([.. blob])), .. blob];
    }

    /// <summary>An archive with a stored file in a folder, an empty slot and a packed file at the root.</summary>
    private static byte[] Archive(uint version = 6,uint? helloCrc = null)
    {
        var banner = Encoding.ASCII.GetBytes("Liquid H2O test archive\r\n\x1a");
        byte[] directories = StringBlob("Units\\Good"), names = StringBlob("hello.txt","ai.txt");
        var hello = "hello"u8.ToArray();
        byte[] packed = [.. BitConverter.GetBytes(Imploded.Length), .. BitConverter.GetBytes(Exploded.Length), .. BitConverter.GetBytes(Crc32.Compute(Exploded)), .. Imploded];
        long data = banner.Length+24+3*40+directories.Length+names.Length;
        var file = new List<byte>(banner);
        file.AddRange(BitConverter.GetBytes(version)); file.AddRange(BitConverter.GetBytes(3u));
        file.AddRange(BitConverter.GetBytes((ulong)(hello.Length+packed.Length))); file.AddRange(BitConverter.GetBytes((ulong)(hello.Length+Exploded.Length)));
        void Record(uint flags,int directory,int name,int raw,int compressed,long offset,uint crc)
        {
            file.AddRange(BitConverter.GetBytes(flags)); file.AddRange(BitConverter.GetBytes(directory)); file.AddRange(BitConverter.GetBytes(name));
            file.AddRange(BitConverter.GetBytes(0u)); file.AddRange(BitConverter.GetBytes(raw)); file.AddRange(BitConverter.GetBytes(compressed));
            file.AddRange(BitConverter.GetBytes((ulong)offset)); file.AddRange(BitConverter.GetBytes(crc)); file.AddRange(BitConverter.GetBytes(0u));
        }
        Record(0,0,0,hello.Length,hello.Length,data,helloCrc ?? Crc32.Compute(hello));
        Record(0,-1,-1,0,0,0,0);
        Record(1,-1,1,Exploded.Length,Imploded.Length,data+hello.Length,Crc32.Compute(Exploded));
        file.AddRange(directories); file.AddRange(names); file.AddRange(hello); file.AddRange(packed);
        return [.. file];
    }

    private static void ArchiveChecks(Action<bool,string> check,string folder)
    {
        string archives = Path.Combine(folder,"Archives");
        Directory.CreateDirectory(archives);
        string path = Path.Combine(archives,"Test.H2O");
        File.WriteAllBytes(path,Archive());
        using (var archive = new H2OArchive(path))
        {
            check(archive.Name == "Test" && archive.Entries.Select(e => e.Path).SequenceEqual(["Units\\Good\\hello.txt","ai.txt"]),
                "H2O lists its files with their folders and skips empty slots");
            check(archive.Entries[0] is { Packed: false, RawSize: 5 } && archive.Entries[1] is { Packed: true, RawSize: 13, CompressedSize: 8 },
                "H2O records keep sizes and packing");
            check(archive.Read("Units/Good/hello.txt").AsSpan().SequenceEqual("hello"u8),"H2O reads a stored file, CRC-checked");
            check(archive.Read("ai.txt").AsSpan().SequenceEqual(Exploded),"H2O reads a packed file, CRC-checked");
            check(archive.Contains("units\\GOOD\\Hello.TXT") && !archive.Contains("Units\\hello.txt"),"H2O paths match in any case, with either separator");
            check(Throws(() => archive.Read("missing.txt")),"H2O reports a missing file");
            check(archive.ReadPrefix(archive.Entries[1],5).AsSpan().SequenceEqual("AIAIA"u8) && archive.ReadPrefix(archive.Entries[1],100).Length == 13
                && archive.ReadPrefix(archive.Entries[0],2).AsSpan().SequenceEqual("he"u8),"H2O reads the start of a packed or stored file");
        }
        using (var game = GameArchives.OpenInstall(folder))
        {
            check(game.Archives.Count == 1 && game.Read("test/Units/Good/hello.txt").AsSpan().SequenceEqual("hello"u8)
                && game.Contains("Test\\ai.txt") && !game.Contains("Other/ai.txt") && !game.Contains("ai.txt"),
                "Install archives read by extracted/ path: archive name first");
        }
        File.WriteAllBytes(path,Archive(helloCrc: 1234));
        using (var archive = new H2OArchive(path))
            check(Throws(() => archive.Read("Units\\Good\\hello.txt")) && archive.Read(archive.Entries[0],verify: false).Length == 5,
                "H2O rejects a file whose CRC does not match, unless asked not to check");
        File.WriteAllBytes(path,Archive(version: 5));
        check(Throws(() => new H2OArchive(path).Dispose()),"H2O rejects other versions");
        File.WriteAllBytes(path,Archive()[..^4]);
        using (var archive = new H2OArchive(path))
            check(Throws(() => archive.Read("ai.txt")),"H2O rejects a file cut short");
    }

    /// <summary>Writes a .lqr table: the layout in tools/lqr.py's docstring.</summary>
    private sealed class TableWriter
    {
        private readonly List<string> _strings = [];
        public int S(string value)
        {
            int index = _strings.IndexOf(value);
            if (index >= 0) return index;
            _strings.Add(value);
            return _strings.Count-1;
        }

        public static void Text(BinaryWriter w,string value)
        {
            w.Write((byte)0); w.Write(value.Length+1); w.Write(Encoding.Unicode.GetBytes(value+"\0"));
        }

        public byte[] Write(Action<BinaryWriter> records,Action<BinaryWriter> names,Action<BinaryWriter> editor)
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(LqrTable.Version); w.Write(0xDEADBEEFu);
            w.Write(0x28UL); w.Write(0UL); w.Write(0UL); w.Write(0UL);
            records(w);
            long nameOffset = stream.Position; names(w);
            long editorOffset = stream.Position; editor(w);
            long stringOffset = stream.Position;
            var text = Encoding.Unicode.GetBytes(string.Concat(_strings.Select(s => s+"\0")));
            w.Write(_strings.Count); w.Write(text.Length); w.Write(text);
            stream.Position = 16; w.Write((ulong)nameOffset); w.Write((ulong)editorOffset); w.Write((ulong)stringOffset);
            return stream.ToArray();
        }
    }

    /// <summary>Units: an int, a float, a string, a reference, an enum and a file name column; two used slots around an
    /// unused one. <paramref name="extraSlot"/> claims one slot more than the block holds.</summary>
    private static byte[] UnitsTable(bool extraSlot = false)
    {
        var t = new TableWriter();
        (string Name,LqrType Type,uint Kind,int Reference,uint Default,string DefaultText,string Description,string Group)[] columns =
        [
            ("Hp",LqrType.Int,0,-1,unchecked((uint)-1),"","Hit points","Stats"),
            ("Speed",LqrType.Float,0,-1,BitConverter.SingleToUInt32Bits(0.5f),"","",""),
            ("Label",LqrType.String,0,-1,0,"Без имени","",""),
            ("Model",LqrType.Id,1,7,unchecked((uint)-1),"","Unit model","Resources"),
            ("Mode",LqrType.Id,2,-1,unchecked((uint)-1),"","",""),
            ("Icon",LqrType.FileName,0,-1,0,"","",""),
        ];
        return t.Write(w =>
        {
            w.Write(0x34UL+4UL*(ulong)columns.Length); w.Write(columns.Length);
            foreach (var c in columns) w.Write((uint)c.Type);
            w.Write(2); w.Write(extraSlot ? 4 : 3);
            w.Write(0); w.Write((byte)1); w.Write(123); w.Write(0.8f); TableWriter.Text(w,"Герой"); w.Write(42); w.Write(2); TableWriter.Text(w,"Textures\\hero.dds");
            w.Write(1); w.Write((byte)0);
            w.Write(2); w.Write((byte)1); w.Write(-1); w.Write(0f); TableWriter.Text(w,""); w.Write(-1); w.Write(-1); TableWriter.Text(w,"");
        },w =>
        {
            w.Write(new byte[9]); w.Write(t.S("UNIT_")); w.Write(t.S("Unit")); w.Write(2); w.Write(columns.Length);
            w.Write(t.S("UNIT_HERO")); w.Write(t.S("UNIT_UNUSED"));
            foreach (var c in columns) w.Write(t.S(c.Name));
        },w =>
        {
            w.Write((byte)0); w.Write(0);
            foreach (var c in columns)
            {
                w.Write(c.Kind); w.Write(c.Reference); w.Write(0); w.Write(c.Default); w.Write(t.S(c.DefaultText)); w.Write(0);
                w.Write(t.S("")); w.Write(t.S("")); w.Write(t.S(c.Type == LqrType.FileName ? "dds" : "")); w.Write(t.S(c.Description));
                w.Write(c.Group == "" ? -1 : t.S(c.Group));
            }
            foreach (var c in columns)
            {
                if (c.Name != "Mode") { w.Write(0); continue; }
                w.Write(3); w.Write(2); w.Write(t.S("Attack")); w.Write(-1); w.Write(t.S("None")); w.Write(2); w.Write(t.S("Charge"));
            }
            w.Write(0); w.Write(0);
            w.Write(0); w.Write(0); w.Write(t.S("Root\\Units\\Good"));
            w.Write(0); w.Write(0); w.Write(t.S(""));
            w.Write(0);
            w.Write(0); w.Write(-1);
        });
    }

    private static void TableChecks(Action<bool,string> check,string folder)
    {
        string database = Path.Combine(folder,"Database");
        Directory.CreateDirectory(database);
        File.WriteAllBytes(Path.Combine(database,"Units.LQR"),UnitsTable());
        var db = new LqrDatabase(database);
        var units = db["UNITS"];
        check(units.Name == "Unit" && units.Prefix == "UNIT_" && db.Files.Keys.SequenceEqual(["units"]) && db.TableIds.Count == 0,
            "LQR table name, prefix and case-insensitive lookup; no Rings.lrp, no table ids");
        check(units.Records.Count == 2 && units.Records[0] is { Id: 0, Name: "UNIT_HERO", Folder: "Root\\Units\\Good" }
            && units.Records[1] is { Id: 2, Name: "UNIT_UNUSED", Folder: "" },"LQR records skip unused slots and keep their slot ids");
        var hero = units.Get("UNIT_HERO")!;
        check(hero.Int("Hp") == 123 && hero.Float("Speed") == 0.8f && hero.Text("Label") == "Герой" && hero.Int("Model") == 42
            && hero.Int("Mode") == 2 && hero.Text("Icon") == "Textures\\hero.dds" && units.Get(2) == units.Records[1],
            "LQR values by type, UTF-16 strings, lookup by id and name");
        var columns = units.Columns;
        check(columns[0] is { Default: -1, Description: "Hit points", Group: "Stats" } && (float)columns[1].Default == 0.5f
            && (string)columns[2].Default == "Без имени" && columns[3] is { Kind: 1, ReferenceTable: 7 } && columns[5].FileExtension == "dds",
            "LQR column defaults, descriptions, groups and references");
        check(columns[4].Enum.Count == 2 && columns[4].Enum[2] == "Charge" && columns[4].Enum[-1] == "None",
            "LQR enum labels; a value listed twice keeps its last label");
        check((string)db.Resolve(columns[4],2) == "Charge" && (int)db.Resolve(columns[3],42) == 42,
            "LQR resolves enums; a reference without Rings.lrp stays a number");
        check(Throws(() => _ = hero["Missing"]) && Throws(() => _ = db["Missing"]),"LQR reports a missing column or table");
        File.WriteAllBytes(Path.Combine(database,"Broken.lqr"),UnitsTable(extraSlot: true));
        check(Throws(() => _ = new LqrDatabase(database)["broken"]),"LQR rejects a record block that does not end where the header says");
        var badVersion = UnitsTable();
        badVersion[0] = 0x69;
        check(Throws(() => _ = new LqrTable(badVersion,"bad.lqr")),"LQR rejects other versions");
    }
}
