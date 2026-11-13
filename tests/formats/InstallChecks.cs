using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Reforged.Formats;

/// <summary>Build-192: the format readers on a real install: every archive file read and CRC-checked and every
/// database table parsed, with timings. Given a fingerprint from tools/formats_fingerprint.py, every file's bytes and
/// every table's schema and values must equal what the Python readers made of the same install.</summary>
public static class InstallChecks
{
    private static readonly Dictionary<LqrType,string> TypeNames = new()
    {
        [LqrType.Int] = "int", [LqrType.Float] = "float", [LqrType.String] = "string", [LqrType.Id] = "id", [LqrType.FileName] = "filename",
    };

    public static void Run(string install,string? python,bool sequential,Action<bool,string> check)
    {
        var clock = Stopwatch.StartNew();
        using var archives = GameArchives.OpenInstall(install);
        double opened = clock.Elapsed.TotalMilliseconds;
        var entries = archives.Archives.SelectMany(a => a.Entries.Select(e => (Archive: a,Entry: e))).ToArray();
        long bytes = entries.Sum(e => (long)e.Entry.RawSize);
        Console.WriteLine($"Opened {archives.Archives.Count} archives, {entries.Length} files, {bytes/1e6:F0} MB, in {opened:F0} ms");
        check(archives.Archives.Count > 0 && entries.Length > 0,"The install's archives open in place");

        clock.Restart();
        if (sequential) foreach (var (archive,entry) in entries) archive.Read(entry);
        else Parallel.ForEach(entries,e => e.Archive.Read(e.Entry));
        double read = clock.Elapsed.TotalSeconds;
        Console.WriteLine($"Read and CRC-checked every file in {read:F1} s ({bytes/1e6/read:F0} MB/s, {(sequential ? "one thread" : "all cores")})");
        check(true,"Every archive file reads with a matching CRC");

        clock.Restart();
        var db = LqrDatabase.OpenInstall(install);
        int records = db.Files.Keys.Sum(key => db[key].Records.Count);
        Console.WriteLine($"Parsed {db.Files.Count} tables, {records} records, in {clock.Elapsed.TotalMilliseconds:F0} ms");
        check(db.Files.Count > 0 && db.Contains("Models"),"Every database table parses to the exact byte");

        if (python is null) return;
        using var fingerprint = JsonDocument.Parse(File.ReadAllBytes(python));
        var root = fingerprint.RootElement;
        var differences = new List<string>();
        void Differ(string text) { if (differences.Count < 25) differences.Add(text); else if (differences.Count == 25) differences.Add("..."); }

        var pythonArchives = root.GetProperty("archives").EnumerateObject().ToDictionary(p => p.Name,p => p.Value,StringComparer.OrdinalIgnoreCase);
        if (!pythonArchives.Keys.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(archives.Archives.Select(a => a.Name),StringComparer.OrdinalIgnoreCase))
            Differ("archive names differ: "+string.Join(",",pythonArchives.Keys)+" / "+string.Join(",",archives.Archives.Select(a => a.Name)));
        var hashes = new string[entries.Length];
        Parallel.For(0,entries.Length,i => hashes[i] = Convert.ToHexStringLower(SHA256.HashData(entries[i].Archive.Read(entries[i].Entry))));
        int index = 0;
        foreach (var archive in archives.Archives)
        {
            var listed = pythonArchives.TryGetValue(archive.Name,out var list) ? list.EnumerateArray().ToArray() : [];
            if (listed.Length != archive.Entries.Count) Differ($"{archive.Name}: {archive.Entries.Count} files, Python lists {listed.Length}");
            for (int i = 0; i < archive.Entries.Count; i++, index++)
            {
                if (i >= listed.Length) continue;
                var (entry,expected) = (archive.Entries[i],listed[i]);
                if (entry.Path != expected.GetProperty("path").GetString() || entry.RawSize != expected.GetProperty("size").GetInt32()
                    || entry.Packed != expected.GetProperty("packed").GetBoolean() || entry.Crc != expected.GetProperty("crc").GetUInt32())
                    Differ($"{archive.Name}\\{entry.Path}: record differs from Python's {expected}");
                if (hashes[index] != expected.GetProperty("sha256").GetString()) Differ($"{archive.Name}\\{entry.Path}: bytes differ");
            }
        }
        check(differences.Count == 0,$"Every archive file's path, size, packing, CRC and bytes equal the Python reader's ({entries.Length} files)"
            +string.Concat(differences.Select(d => "\n  "+d)));

        var tableIds = root.GetProperty("table_ids").EnumerateObject().ToDictionary(p => int.Parse(p.Name),p => p.Value.GetString()!);
        if (tableIds.Count != db.TableIds.Count || tableIds.Any(p => db.TableIds.GetValueOrDefault(p.Key) != p.Value)) Differ("Rings.lrp table ids differ");
        var tables = root.GetProperty("tables").EnumerateObject().ToArray();
        if (!tables.Select(t => t.Name).SequenceEqual(db.Files.Keys.Order(StringComparer.Ordinal))) Differ("table files differ");
        int values = 0;
        foreach (var table in tables.Where(t => db.Contains(t.Name))) values += CompareTable(db,db[table.Name],table.Value,Differ);
        check(differences.Count == 0,$"Every table's schema, records and values equal the Python reader's ({tables.Length} tables, {values} values)"
            +string.Concat(differences.Select(d => "\n  "+d)));
    }

    private static bool Same(JsonElement expected,object actual) => actual switch
    {
        int number => expected.ValueKind == JsonValueKind.Number && expected.TryGetInt64(out long value) && value == number,
        float number => expected.ValueKind == JsonValueKind.Number && expected.GetDouble() == number,
        string text => expected.ValueKind == JsonValueKind.String && expected.GetString() == text,
        _ => false,
    };

    private static int CompareTable(LqrDatabase db,LqrTable table,JsonElement expected,Action<string> differ)
    {
        string where = Path.GetFileName(table.Path);
        int? tableId = db.TableIds.Where(p => p.Value.Equals(Path.GetFileNameWithoutExtension(table.Path),StringComparison.OrdinalIgnoreCase))
            .Select(p => (int?)p.Key).LastOrDefault();
        var id = expected.GetProperty("table_id");
        if (expected.GetProperty("name").GetString() != table.Name || expected.GetProperty("prefix").GetString() != table.Prefix
            || expected.GetProperty("source").GetString() != where || (id.ValueKind == JsonValueKind.Null ? tableId is not null : id.GetInt32() != tableId))
            differ($"{where}: name, prefix, source or table id differ");
        var columns = expected.GetProperty("columns").EnumerateArray().ToArray();
        if (columns.Length != table.Columns.Count) { differ($"{where}: {table.Columns.Count} columns, Python has {columns.Length}"); return 0; }
        for (int c = 0; c < columns.Length; c++)
        {
            var (column,e) = (table.Columns[c],columns[c]);
            bool same = e.GetProperty("name").GetString() == column.Name && e.GetProperty("type").GetString() == TypeNames[column.Type]
                && e.GetProperty("kind").GetInt32() == column.Kind && Same(e.GetProperty("default"),column.Default)
                && e.GetProperty("description").GetString() == column.Description && e.GetProperty("file_ext").GetString() == column.FileExtension
                && e.GetProperty("group").GetString() == column.Group;
            if (column.Kind == 1)
            {
                var reference = e.TryGetProperty("reference",out var r) ? r : default;
                string? referenced = db.TableIds.GetValueOrDefault(column.ReferenceTable);
                same &= reference.ValueKind == JsonValueKind.Object && reference.GetProperty("table_id").GetInt32() == column.ReferenceTable
                    && (reference.GetProperty("table").ValueKind == JsonValueKind.Null ? referenced is null : reference.GetProperty("table").GetString() == referenced);
            }
            var labels = e.TryGetProperty("enum",out var list) ? list.EnumerateArray().Select(x => (x.GetProperty("value").GetInt32(),x.GetProperty("label").GetString()!)).ToArray() : [];
            same &= labels.SequenceEqual(column.Enum.OrderBy(p => p.Key).Select(p => (p.Key,p.Value)));
            if (!same) differ($"{where}: column {c} {column.Name} differs from Python's {e}");
        }
        var records = expected.GetProperty("records").EnumerateArray().ToArray();
        if (records.Length != table.Records.Count) { differ($"{where}: {table.Records.Count} records, Python has {records.Length}"); return 0; }
        int distinctColumns = table.Columns.Select(c => c.Name).Distinct().Count(), values = 0;
        for (int i = 0; i < records.Length; i++)
        {
            var (record,e) = (table.Records[i],records[i]);
            if (e.GetProperty("id").GetInt32() != record.Id || e.GetProperty("name").GetString() != record.Name || e.GetProperty("folder").GetString() != record.Folder)
                differ($"{where}: record {i} id, name or folder differ ({record.Id} {record.Name})");
            var fields = e.GetProperty("values").EnumerateObject().ToArray();
            if (fields.Length != distinctColumns) differ($"{where}: {record.Name} has {fields.Length} values in Python, {distinctColumns} columns here");
            foreach (var field in fields)
            {
                values++;
                if (!table.HasColumn(field.Name) || !Same(field.Value,record[field.Name]))
                    differ($"{where}: {record.Name}.{field.Name} is {(table.HasColumn(field.Name) ? record[field.Name] : "missing")}, Python has {field.Value}");
            }
        }
        return values;
    }
}
