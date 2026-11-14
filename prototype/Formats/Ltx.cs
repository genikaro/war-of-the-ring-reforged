using System.Buffers.Binary;
using System.Text;

namespace Reforged.Formats;

/// <summary>Build-193: a LIQDTEXT text table (Text.H2O's &lt;Table&gt;.ltx), ported from tools/game_text.py's parse_ltx.
/// "LIQDTEXT", f32 version 1.0, u32 count, then count x {u32 id, u8 flag (always 0), u32 length in characters,
/// UTF-16LE characters}. The length counts a closing NUL, which is dropped; an empty string has length 0. Ids are not
/// sorted and must not repeat.</summary>
public static class Ltx
{
    public static Dictionary<int,string> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..8].SequenceEqual("LIQDTEXT"u8)) throw new InvalidDataException("not a LIQDTEXT table");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        int position = 16;
        var entries = new Dictionary<int,string>();
        for (uint i = 0; i < count; i++)
        {
            if (position+9 > data.Length) throw new InvalidDataException($"LIQDTEXT entry {i} starts past the end of the table");
            int id = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            long length = BinaryPrimitives.ReadUInt32LittleEndian(data[(position+5)..]);
            position += 9;
            if (position+2*length > data.Length) throw new InvalidDataException($"LIQDTEXT entry {id} runs past the end of the table");
            string text = Encoding.Unicode.GetString(data.Slice(position,(int)(2*length)));
            position += (int)(2*length);
            if (text.EndsWith('\0')) text = text[..^1];
            if (!entries.TryAdd(id,text)) throw new InvalidDataException($"LIQDTEXT duplicate entry id {id}");
        }
        if (position != data.Length) throw new InvalidDataException($"LIQDTEXT {data.Length-position} trailing bytes after {count} entries");
        return entries;
    }
}
