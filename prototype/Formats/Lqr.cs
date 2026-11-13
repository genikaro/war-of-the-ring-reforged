using System.Buffers.Binary;
using System.Text;

namespace Reforged.Formats;

/// <summary>Column types: Id holds an enum, a bool or another table's record id (see <see cref="LqrColumn.Kind"/>).</summary>
public enum LqrType { Int = 0, Float = 1, String = 2, Id = 3, FileName = 4 }

/// <summary>A column and its editor descriptor. <see cref="Kind"/>: 0 plain, 1 a record id of table
/// <see cref="ReferenceTable"/> (a Rings.lrp table id), 2 a value from <see cref="Enum"/>.</summary>
public sealed class LqrColumn
{
    public required string Name { get; init; }
    public required LqrType Type { get; init; }
    public int Kind { get; init; }
    public int ReferenceTable { get; init; } = -1;
    /// <summary>An int, float or string by <see cref="Type"/>.</summary>
    public object Default { get; init; } = 0;
    public string Description { get; init; } = "";
    public string FileExtension { get; init; } = "";
    public string Group { get; init; } = "";
    /// <summary>Enum labels by value; a value listed twice keeps its last label, as tools/lqr.py does.</summary>
    public Dictionary<int,string> Enum { get; } = [];
    public bool IsText => Type is LqrType.String or LqrType.FileName;
}

/// <summary>One used slot of a table: its id (the slot index), symbolic name (UNIT_G_HERO_ARAGORN), editor folder
/// and one value per column: int for Int and Id columns, float for Float, string for String and FileName.</summary>
public sealed class LqrRecord
{
    public required LqrTable Table { get; init; }
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string Folder { get; init; } = "";
    public required object[] Values { get; init; }

    public object this[string column] => Values[Table.ColumnIndex(column)];
    public int Int(string column) => (int)this[column];
    public float Float(string column) => (float)this[column];
    public string Text(string column) => (string)this[column];
}

/// <summary>Build-192: a Liquid Entertainment .lqr database table, ported from tools/lqr.py (its docstring has the
/// layout). Parsing checks that the record and editor blocks end exactly where the header says, as the Python does.</summary>
public sealed class LqrTable
{
    public const uint Version = 0x6a;
    public string Path { get; }
    public string Name { get; }
    public string Prefix { get; }
    public IReadOnlyList<LqrColumn> Columns { get; }
    public IReadOnlyList<LqrRecord> Records { get; }
    /// <summary>The editor's conversion tools: name, executable, arguments, extension.</summary>
    public IReadOnlyList<string[]> Conversions { get; }
    private readonly Dictionary<string,int> _columns = [];
    private readonly Dictionary<int,LqrRecord> _byId = [];
    private readonly Dictionary<string,LqrRecord> _byName = [];

    public static LqrTable Load(string path) => new(File.ReadAllBytes(path),path);

    public LqrTable(byte[] data,string path)
    {
        Path = path;
        var r = new Reader(data,path,0);
        uint version = r.U32();
        if (version != Version) throw new InvalidDataException($"{path}: unsupported lqr version 0x{version:x}");
        r.Position += 4; // Uninitialised memory.
        long recordOffset = r.Offset(), nameOffset = r.Offset(), editorOffset = r.Offset(), stringOffset = r.Offset();
        r.Position = stringOffset;
        int stringCount = r.Count(), stringBytes = r.Count();
        string[] strings = Encoding.Unicode.GetString(r.Bytes(stringBytes)).Split('\0');
        if (stringCount < strings.Length) strings = strings[..stringCount];
        string S(long index) => index >= 0 && index < strings.Length ? strings[index] : "";

        // Record block.
        r.Position = recordOffset+8;
        int columnCount = r.Count();
        var types = new LqrType[columnCount];
        for (int i = 0; i < columnCount; i++) types[i] = (LqrType)r.U32();
        int recordCount = r.Count(), slotCount = r.Count();
        var raw = new List<(int Id,object[] Values)>(recordCount);
        for (int slot = 0; slot < slotCount; slot++)
        {
            int id = (int)r.U32();
            if (r.U8() == 0) continue;
            var values = new object[columnCount];
            for (int c = 0; c < columnCount; c++)
                values[c] = types[c] switch { LqrType.String or LqrType.FileName => r.Text(), LqrType.Float => r.F32(), _ => r.I32() };
            raw.Add((id,values));
        }
        if (r.Position != nameOffset || raw.Count != recordCount) throw new InvalidDataException($"{path}: record block size mismatch");

        // Name block.
        r.Position = nameOffset+9;
        Prefix = S(r.U32()); Name = S(r.U32());
        int namedRecords = r.Count(), namedColumns = r.Count();
        var recordNames = new string[namedRecords];
        for (int i = 0; i < namedRecords; i++) recordNames[i] = S(r.U32());
        var columnNames = new string[namedColumns];
        for (int i = 0; i < namedColumns; i++) columnNames[i] = S(r.U32());
        if (namedRecords != recordCount || namedColumns != columnCount) throw new InvalidDataException($"{path}: name block counts differ from the record block");

        // Editor block.
        r.Position = editorOffset;
        r.Position += 1; // has_conversions
        int conversionCount = r.Count();
        var conversions = new List<string[]>(conversionCount);
        for (int i = 0; i < conversionCount; i++) conversions.Add([S(r.U32()),S(r.U32()),S(r.U32()),S(r.U32())]);
        Conversions = conversions;
        var columns = new LqrColumn[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            uint kind = r.U32(); int reference = r.I32(); r.Position += 4;
            uint rawDefault = r.U32(), defaultText = r.U32(); r.Position += 4;
            r.Position += 8; // File dialog title and filter.
            uint extension = r.U32(), description = r.U32(); int group = r.I32();
            object value = types[c] switch
            {
                LqrType.Float => BitConverter.UInt32BitsToSingle(rawDefault),
                LqrType.String or LqrType.FileName => S(defaultText),
                _ => (int)rawDefault,
            };
            columns[c] = new LqrColumn { Name = columnNames[c], Type = types[c], Kind = (int)kind, ReferenceTable = reference, Default = value,
                Description = S(description), FileExtension = S(extension), Group = S(group) };
        }
        foreach (var column in columns)
            for (int n = r.Count(); n > 0; n--) { int value = r.I32(); column.Enum[value] = S(r.U32()); }
        r.SkipList(12); // Property groups.
        r.SkipList(12); // Property grid rows.
        var folders = new string[recordCount];
        for (int i = 0; i < recordCount; i++) { r.Position += 8; folders[i] = S(r.U32()); }
        r.SkipList(16); // Folder tree.
        r.SkipList(4,4); // List view columns and a closing -1.
        if (r.Position != stringOffset) throw new InvalidDataException($"{path}: editor block size mismatch");

        Columns = columns;
        // A column name used twice resolves to its last column, as the Python's dict of values does.
        for (int c = 0; c < columnCount; c++) _columns[columnNames[c]] = c;
        var records = new LqrRecord[recordCount];
        for (int i = 0; i < recordCount; i++)
        {
            var record = records[i] = new LqrRecord { Table = this, Id = raw[i].Id, Name = recordNames[i], Folder = folders[i], Values = raw[i].Values };
            _byId[record.Id] = record; _byName[record.Name] = record;
        }
        Records = records;
    }

    public int ColumnIndex(string column) =>
        _columns.TryGetValue(column,out int index) ? index : throw new KeyNotFoundException($"{Name} has no column {column}");

    public bool HasColumn(string column) => _columns.ContainsKey(column);

    public LqrRecord? Get(int id) => _byId.GetValueOrDefault(id);

    public LqrRecord? Get(string name) => _byName.GetValueOrDefault(name);

    internal sealed class Reader(byte[] data,string path,long position)
    {
        public long Position = position;

        private ReadOnlySpan<byte> Take(int length)
        {
            if (Position < 0 || Position+length > data.Length) throw new InvalidDataException($"{path}: data ends before offset {Position+length}");
            var span = data.AsSpan((int)Position,length);
            Position += length;
            return span;
        }

        public byte U8() => Take(1)[0];
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public long Offset() => checked((long)BinaryPrimitives.ReadUInt64LittleEndian(Take(8)));
        public byte[] Bytes(int length) => Take(length).ToArray();

        /// <summary>A u32 that counts something in the file, so it cannot exceed the file's size.</summary>
        public int Count()
        {
            uint value = U32();
            return value <= data.Length ? (int)value : throw new InvalidDataException($"{path}: count {value} at offset {Position-4} exceeds the file");
        }

        public void SkipList(int itemSize,int extra = 0) { long n = Count(); Position += itemSize*n+extra; }

        /// <summary>u8 0, u32 character count including the NUL, UTF-16LE characters; trailing NULs dropped.</summary>
        public string Text()
        {
            if (U8() != 0) throw new InvalidDataException($"{path}: bad string marker at 0x{Position-1:x}");
            int characters = Count();
            return Encoding.Unicode.GetString(Take(checked(2*characters))).TrimEnd('\0');
        }
    }
}

/// <summary>Build-192: an install's Database folder: tables by name in any case, loaded when first asked for, and the
/// Rings.lrp project file's table ids, which reference columns name their table by. Safe to share between threads.</summary>
public sealed class LqrDatabase
{
    public string Folder { get; }
    /// <summary>Table files by lower-case name without extension.</summary>
    public IReadOnlyDictionary<string,string> Files { get; }
    /// <summary>Rings.lrp: table file names by table id; empty without the project file.</summary>
    public IReadOnlyDictionary<int,string> TableIds { get; }
    private readonly Dictionary<string,Lazy<LqrTable>> _tables = [];

    public LqrDatabase(string folder)
    {
        Folder = System.IO.Path.GetFullPath(folder);
        var files = new Dictionary<string,string>();
        foreach (string file in Directory.EnumerateFiles(Folder).Where(f => System.IO.Path.GetExtension(f).Equals(".lqr",StringComparison.OrdinalIgnoreCase)))
            files[System.IO.Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = file;
        Files = files;
        foreach (var (key,file) in files) _tables[key] = new(() => LqrTable.Load(file));
        string project = System.IO.Path.Combine(Folder,"Rings.lrp");
        TableIds = File.Exists(project) ? ReadProject(File.ReadAllBytes(project),project) : new Dictionary<int,string>();
    }

    public static LqrDatabase OpenInstall(string install) => new(System.IO.Path.Combine(install,"Database"));

    public LqrTable this[string name] =>
        _tables.TryGetValue(name.ToLowerInvariant(),out var table) ? table.Value : throw new KeyNotFoundException($"No table {name} in {Folder}");

    public bool Contains(string name) => _tables.ContainsKey(name.ToLowerInvariant());

    public LqrTable? TableById(int id) => TableIds.TryGetValue(id,out string? name) && Contains(name) ? this[name] : null;

    /// <summary>The symbolic name a reference or enum column's value stands for, else the value itself.</summary>
    public object Resolve(LqrColumn column,object value)
    {
        if (value is not int number) return value;
        if (column.Kind == 1 && number >= 0 && TableById(column.ReferenceTable)?.Get(number) is { } record) return record.Name;
        if (column.Kind == 2 && column.Enum.TryGetValue(number,out string? label)) return label;
        return value;
    }

    /// <summary>Rings.lrp: u32 0x66, two unknown u32, four strings, two bytes, two u32 lists, a u32, three strings,
    /// then u32 count, the table ids and as many table names.</summary>
    public static Dictionary<int,string> ReadProject(byte[] data,string path)
    {
        var r = new LqrTable.Reader(data,path,12);
        for (int i = 0; i < 4; i++) r.Text();
        r.Position += 2;
        r.SkipList(4); r.SkipList(4);
        r.Position += 4;
        for (int i = 0; i < 3; i++) r.Text();
        int count = r.Count();
        var ids = new int[count];
        for (int i = 0; i < count; i++) ids[i] = (int)r.U32();
        var tables = new Dictionary<int,string>();
        foreach (int id in ids) tables[id] = r.Text();
        return tables;
    }
}
