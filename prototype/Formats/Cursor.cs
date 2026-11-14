using System.Buffers.Binary;
using System.Text;

namespace Reforged.Formats;

/// <summary>Build-195: little-endian reading over a whole file with every length checked against what is left, so a
/// damaged count throws InvalidDataException instead of allocating or reading past the end (the model parsers).</summary>
internal sealed class Cursor(byte[] data,int position,string what)
{
    public readonly byte[] Data = data;
    public int Position = position;
    public int Remaining => Data.Length-Position;

    public void Need(long bytes)
    {
        if (bytes < 0 || bytes > Remaining) throw new InvalidDataException($"truncated {what} at 0x{Position:x}");
    }

    private ReadOnlySpan<byte> Take(int bytes) { Need(bytes); var span = Data.AsSpan(Position,bytes); Position += bytes; return span; }
    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public uint[] U32s(long count)
    {
        Need(4*count);
        var values = new uint[count];
        for (int i = 0; i < count; i++) values[i] = U32();
        return values;
    }

    public int[] I32s(long count)
    {
        Need(4*count);
        var values = new int[count];
        for (int i = 0; i < count; i++) values[i] = I32();
        return values;
    }

    public float[] F32s(long count)
    {
        Need(4*count);
        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = F32();
        return values;
    }

    public byte[] Bytes(long count) { Need(count); var bytes = Data.AsSpan(Position,(int)count).ToArray(); Position += (int)count; return bytes; }

    /// <summary>UTF-16LE text of a known character count, trailing NULs dropped.</summary>
    public string Utf16(long characters) => Encoding.Unicode.GetString(Take(checked((int)(2*characters)))).TrimEnd('\0');

    public InvalidDataException Fail(string message) => new($"{what}: {message} at 0x{Position:x}");
}
