using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Reforged.Formats;

/// <summary>One file in an H2O archive. <see cref="Path"/> uses the archive's backslashes, without the archive's name.
/// <see cref="Crc"/> is the record's; a packed file is checked against the CRC in front of its compressed data.</summary>
public sealed record H2OEntry(string Path,int RawSize,int CompressedSize,long Offset,uint Crc,bool Packed);

/// <summary>Build-192: a Liquid Entertainment H2O archive (version 6), read in place: the directory once, then any file
/// on request, so nothing has to be extracted first. Ported from tools/h2o.py, whose docstring has the layout. Reads
/// are positional, so several threads can read from one archive at once.</summary>
public sealed class H2OArchive : IDisposable
{
    public string FilePath { get; }
    /// <summary>The file name without extension ("Models"): the folder tools/h2o.py extracts it into.</summary>
    public string Name { get; }
    public IReadOnlyList<H2OEntry> Entries { get; }
    private readonly SafeFileHandle _file;
    private readonly Dictionary<string,H2OEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public H2OArchive(string path)
    {
        FilePath = System.IO.Path.GetFullPath(path);
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        _file = File.OpenHandle(FilePath,FileMode.Open,FileAccess.Read,FileShare.Read,FileOptions.RandomAccess);
        try { Entries = ReadDirectory(); }
        catch { _file.Dispose(); throw; }
        // Later duplicates win, as they do when tools/h2o.py extracts them over the earlier ones.
        foreach (var entry in Entries) _byPath[entry.Path] = entry;
    }

    private List<H2OEntry> ReadDirectory()
    {
        var start = ReadAt(0,(int)Math.Min(4096,RandomAccess.GetLength(_file)));
        int banner = start.AsSpan().IndexOf("\r\n\x1a"u8);
        if (banner < 0) throw new InvalidDataException($"{Name}: not an H2O archive");
        long header = banner+3;
        var fixedPart = ReadAt(header,24);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(fixedPart), count = BinaryPrimitives.ReadUInt32LittleEndian(fixedPart.AsSpan(4));
        if (version != 6) throw new InvalidDataException($"{Name}: unsupported H2O version {version}");
        if (count > 1_000_000) throw new InvalidDataException($"{Name}: implausible file count {count}");
        var records = ReadAt(header+24,checked(40*(int)count));
        long position = header+24+40L*count;
        var directories = Strings(ref position);
        var names = Strings(ref position);
        var entries = new List<H2OEntry>((int)count);
        for (int i = 0; i < count; i++)
        {
            var r = records.AsSpan(40*i,40);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(r);
            int directory = BinaryPrimitives.ReadInt32LittleEndian(r[4..]), nameIndex = BinaryPrimitives.ReadInt32LittleEndian(r[8..]);
            if (nameIndex < 0) continue; // An empty slot.
            uint raw = BinaryPrimitives.ReadUInt32LittleEndian(r[16..]), compressed = BinaryPrimitives.ReadUInt32LittleEndian(r[20..]);
            long offset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(r[24..]));
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(r[32..]);
            string name = names[nameIndex];
            if (directory >= 0) name = directories[directory]+"\\"+name;
            entries.Add(new(name,checked((int)raw),checked((int)compressed),offset,crc,(flags & 1) != 0));
        }
        return entries;
    }

    /// <summary>A string table blob: u32 count, u32 byte size, then NUL-terminated UTF-16LE strings.</summary>
    private string[] Strings(ref long position)
    {
        (var blob,_,position) = Blob(position);
        int count = BinaryPrimitives.ReadInt32LittleEndian(blob);
        var strings = Encoding.Unicode.GetString(blob,8,blob.Length-8).Split('\0');
        if (count < 0 || count > strings.Length) throw new InvalidDataException($"{Name}: string table holds fewer than {count} strings");
        return strings[..count];
    }

    /// <summary>A blob: u32 compressed size, u32 raw size, u32 CRC, then the data, DCL-imploded unless the sizes match.</summary>
    private (byte[] Data,uint Crc,long End) Blob(long offset)
    {
        var head = ReadAt(offset,12);
        int compressed = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(head)), raw = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)));
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8));
        var payload = ReadAt(offset+12,compressed);
        if (compressed == raw) return (payload,crc,offset+12+compressed);
        var data = new byte[raw];
        Blast.Explode(payload,data);
        return (data,crc,offset+12+compressed);
    }

    private byte[] ReadAt(long offset,int length)
    {
        var data = new byte[length];
        int done = 0;
        while (done < length)
        {
            int read = RandomAccess.Read(_file,data.AsSpan(done),offset+done);
            if (read == 0) throw new InvalidDataException($"{Name}: ends at {offset+done}, inside data that should run to {offset+length}");
            done += read;
        }
        return data;
    }

    public bool Contains(string path) => _byPath.ContainsKey(Normalize(path));

    public bool TryGet(string path,out H2OEntry entry) => _byPath.TryGetValue(Normalize(path),out entry!);

    /// <summary>A file's bytes. With <paramref name="verify"/> its CRC-32 is checked too (about as fast as reading it).</summary>
    public byte[] Read(H2OEntry entry,bool verify = true)
    {
        var (data,crc,_) = entry.Packed ? Blob(entry.Offset) : (ReadAt(entry.Offset,entry.RawSize),entry.Crc,0L);
        if (data.Length != entry.RawSize) throw new InvalidDataException($"{Name}\\{entry.Path}: {data.Length} bytes, expected {entry.RawSize}");
        if (verify && Crc32.Compute(data) != crc) throw new InvalidDataException($"{Name}\\{entry.Path}: CRC mismatch");
        return data;
    }

    public byte[] Read(string path,bool verify = true) =>
        TryGet(path,out var entry) ? Read(entry,verify) : throw new FileNotFoundException($"{Name} has no {path}",path);

    /// <summary>Build-193: a file's first <paramref name="length"/> bytes (all of a shorter file), for headers. A packed
    /// file decompresses only that far. A part cannot be CRC-checked.</summary>
    public byte[] ReadPrefix(H2OEntry entry,int length)
    {
        length = Math.Min(length,entry.RawSize);
        if (!entry.Packed) return ReadAt(entry.Offset,length);
        var head = ReadAt(entry.Offset,12);
        int compressed = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(head)), raw = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)));
        if (raw != entry.RawSize) throw new InvalidDataException($"{Name}\\{entry.Path}: {raw} bytes, expected {entry.RawSize}");
        if (compressed == raw) return ReadAt(entry.Offset+12,length);
        // Start with as much compressed data as the part would take stored and read four times more while that is short.
        for (int input = Math.Min(compressed,length+64); ; input = (int)Math.Min(compressed,input*4L))
        {
            var data = new byte[length];
            int written = Blast.ExplodePrefix(ReadAt(entry.Offset+12,input),data,input == compressed);
            if (written == length) return data;
            if (input == compressed) throw new InvalidDataException($"{Name}\\{entry.Path}: ends after {written} of {length} bytes");
        }
    }

    /// <summary>The archive's own form of a path: backslashes, no leading separator.</summary>
    public static string Normalize(string path) => path.Replace('/','\\').TrimStart('\\');

    public void Dispose() => _file.Dispose();
}
