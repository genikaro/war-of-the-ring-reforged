namespace Reforged.Formats;

/// <summary>Build-192: the PKWARE Data Compression Library "implode" decompressor that H2O archives pack their files
/// with, ported from tools/blast.py (itself Mark Adler's blast.c from zlib contrib). Huffman codes decode through
/// one lookup table per code: table[next maxlen bits] = symbol &lt;&lt; 4 | code length.</summary>
public static class Blast
{
    // Run-length code-length tables from blast.c: each byte is (repeat - 1) << 4 | length.
    private static readonly byte[] LitLen = [
        11, 124, 8, 7, 28, 7, 188, 13, 76, 4, 10, 8, 12, 10, 12, 10, 8, 23, 8,
        9, 7, 6, 7, 8, 7, 6, 55, 8, 23, 24, 12, 11, 7, 9, 11, 12, 6, 7, 22, 5,
        7, 24, 6, 11, 9, 6, 7, 22, 7, 11, 38, 7, 9, 8, 25, 11, 8, 11, 9, 12,
        8, 12, 5, 38, 5, 38, 5, 11, 7, 5, 6, 21, 6, 10, 53, 8, 7, 24, 10, 27,
        44, 253, 253, 253, 252, 252, 252, 13, 12, 45, 12, 45, 12, 61, 12, 45,
        44, 173];
    private static readonly byte[] LenLen = [2, 35, 36, 53, 38, 23];
    private static readonly byte[] DistLen = [2, 20, 53, 230, 247, 151, 248];
    private static readonly int[] Base = [3, 2, 4, 5, 6, 7, 8, 9, 10, 12, 16, 24, 40, 72, 136, 264];
    private static readonly int[] Extra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];

    private sealed record Code(ushort[] Table,int MaxLength);
    private static readonly Code Lit = Build(LitLen), Len = Build(LenLen), Dist = Build(DistLen);

    private static Code Build(byte[] rep)
    {
        var lengths = new List<int>();
        foreach (byte b in rep) lengths.AddRange(Enumerable.Repeat(b & 15,(b >> 4)+1));
        int maxLength = lengths.Max(), code = 0;
        var table = new ushort[1 << maxLength];
        // Canonical codes, shortest first, symbols in order within a length; stored bit-inverted, first bit read is the MSB.
        for (int length = 1; length <= maxLength; length++)
        {
            for (int symbol = 0; symbol < lengths.Count; symbol++)
            {
                if (lengths[symbol] != length) continue;
                int peek = 0;
                for (int i = 0; i < length; i++) peek |= (((code >> (length-1-i)) & 1) ^ 1) << i;
                for (int fill = 0; fill < 1 << (maxLength-length); fill++) table[peek | (fill << length)] = (ushort)(symbol << 4 | length);
                code++;
            }
            code <<= 1;
        }
        return new(table,maxLength);
    }

    /// <summary>Decompresses <paramref name="source"/> into all of <paramref name="destination"/>. Throws when the data
    /// is malformed, ends early (the end code before the destination is full) or would run past the destination.</summary>
    public static void Explode(ReadOnlySpan<byte> source,Span<byte> destination)
    {
        var input = new BitReader(source);
        int written = 0;
        uint literals = input.Bits(8);
        if (literals > 1) throw new InvalidDataException($"DCL: bad literal flag {literals}");
        int dictionaryBits = (int)input.Bits(8);
        if (dictionaryBits is < 4 or > 6) throw new InvalidDataException($"DCL: bad dictionary size {dictionaryBits}");
        while (written < destination.Length)
        {
            if (input.Position > source.Length+4) throw new InvalidDataException("DCL: input exhausted");
            if (input.Bits(1) != 0)
            {
                int symbol = input.Decode(Len), length = Base[symbol]+(int)input.Bits(Extra[symbol]);
                if (length == 519) break;
                int shift = length == 2 ? 2 : dictionaryBits;
                int distance = (input.Decode(Dist) << shift)+(int)input.Bits(shift)+1;
                if (distance > written) throw new InvalidDataException("DCL: distance too far back");
                if (written+length > destination.Length) throw new InvalidDataException("DCL: data runs past the expected size");
                int start = written-distance;
                if (distance >= length) destination.Slice(start,length).CopyTo(destination[written..]);
                else for (int i = 0; i < length; i++) destination[written+i] = destination[start+i];
                written += length;
            }
            else destination[written++] = (byte)(literals != 0 ? input.Decode(Lit) : (int)input.Bits(8));
        }
        if (written != destination.Length) throw new InvalidDataException($"DCL: {written} bytes, expected {destination.Length}");
    }

    /// <summary>Bits least significant first; past the end of the input it reads zero bytes, as blast.py does.</summary>
    private ref struct BitReader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> _source = source;
        private uint _buffer;
        private int _count;
        public int Position;

        private void Need(int k)
        {
            while (_count < k) { _buffer |= (uint)(Position < _source.Length ? _source[Position] : 0) << _count; Position++; _count += 8; }
        }

        public uint Bits(int k)
        {
            Need(k);
            uint value = _buffer & ((1u << k)-1);
            _buffer >>= k; _count -= k;
            return value;
        }

        public int Decode(Code code)
        {
            Need(code.MaxLength);
            int entry = code.Table[_buffer & ((1u << code.MaxLength)-1)], length = entry & 15;
            _buffer >>= length; _count -= length;
            return entry >> 4;
        }
    }
}
