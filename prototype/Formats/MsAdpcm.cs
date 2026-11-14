using System.Buffers.Binary;

namespace Reforged.Formats;

/// <summary>Build-205: the original's mono and stereo Microsoft ADPCM effects and speech decoded to 16-bit PCM, a port
/// of tools/pcm_audio.py's decode_ms_adpcm giving the same bytes (the RIFF file Python's wave module writes: a 44-byte
/// header, then the samples). Coefficients and the sample count come from the RIFF header.</summary>
public static class MsAdpcm
{
    private static readonly int[] Adaptation = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];

    /// <summary>The format tag of a RIFF WAVE file (1 = PCM, 2 = Microsoft ADPCM), read where game_audio reads it.</summary>
    public static int FormatTag(byte[] data) => data.Length >= 22 ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20)) : -1;

    public static byte[] Decode(byte[] data)
    {
        if (data.Length < 12 || !data.AsSpan(0,4).SequenceEqual("RIFF"u8) || !data.AsSpan(8,4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("not a RIFF WAVE");
        long end = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4))+8L;
        if (end != data.Length) throw new InvalidDataException("invalid RIFF length");
        var chunks = new Dictionary<string,(int Offset,int Size)>();
        int offset = 12;
        while (offset < end)
        {
            if (offset+8 > end) throw new InvalidDataException("truncated WAV chunk");
            string name = System.Text.Encoding.Latin1.GetString(data,offset,4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset+4));
            offset += 8;
            if (offset+size+(size & 1) > end || chunks.ContainsKey(name)) throw new InvalidDataException("invalid WAV chunk");
            chunks[name] = (offset,(int)size);
            offset += (int)(size+(size & 1));
        }
        byte[] Chunk(string name) => chunks.TryGetValue(name,out var c) ? data.AsSpan(c.Offset,c.Size).ToArray() : [];
        byte[] fmt = Chunk("fmt "), payload = Chunk("data"), fact = Chunk("fact");
        if (fmt.Length < 22 || fact.Length != 4) throw new InvalidDataException("missing ADPCM format/sample count");
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(at));
        int tag = U16(0), channels = U16(2), block = U16(12), bits = U16(14), extra = U16(16), samples = U16(18), count = U16(20);
        long rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
        if (tag != 2 || channels is not (1 or 2) || bits != 4 || rate is < 8000 or > 192000)
            throw new InvalidDataException("unsupported WAV format; expected mono/stereo Microsoft ADPCM");
        int header = 7*channels;
        if (block < header || samples != 2+(block-header)*2/channels || fmt.Length != 18+extra || fmt.Length != 22+count*4 || count == 0)
            throw new InvalidDataException("invalid ADPCM format");
        int remainder = payload.Length%block;
        if (payload.Length == 0 || remainder > 0 && remainder < header) throw new InvalidDataException("incomplete ADPCM block");
        var coefficients = new (int First,int Second)[count];
        for (int i = 0; i < count; i++)
            coefficients[i] = (BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(22+i*4)),BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(24+i*4)));
        long total = BinaryPrimitives.ReadUInt32LittleEndian(fact);
        long available = (long)(payload.Length/block)*samples+(remainder > 0 ? 2+(remainder-header)*2/channels : 0);
        // Shipped speech often ends in a short block; odd-length recordings can declare one sample the encoder omitted.
        if (remainder > 0 && total == available+1) total = available;
        if (total <= 0 || total > available) throw new InvalidDataException("invalid ADPCM sample count");
        var output = new List<short>((int)(available*channels));
        var predictors = new int[channels];
        var deltas = new int[channels];
        var recent = new int[channels];
        var older = new int[channels];
        for (int start = 0; start < payload.Length; start += block)
        {
            for (int c = 0; c < channels; c++)
            {
                predictors[c] = payload[start+c];
                deltas[c] = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(start+channels+2*c));
                recent[c] = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(start+3*channels+2*c));
                older[c] = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(start+5*channels+2*c));
            }
            for (int c = 0; c < channels; c++)
                if (predictors[c] >= count || deltas[c] < 16) throw new InvalidDataException("invalid ADPCM block header");
            for (int c = 0; c < channels; c++) output.Add((short)older[c]);
            for (int c = 0; c < channels; c++) output.Add((short)recent[c]);
            int stop = Math.Min(start+block,payload.Length);
            for (int i = start+header; i < stop; i++)
            {
                int value = payload[i];
                for (int half = 0; half < 2; half++)
                {
                    int nibble = half == 0 ? value >> 4 : value & 15;
                    int channel = channels == 2 ? half : 0;
                    var (first,second) = coefficients[predictors[channel]];
                    long prediction = (long)recent[channel]*first+(long)older[channel]*second;
                    // Truncated toward zero, as the Python spells out (its // would round down).
                    long predicted = Math.Abs(prediction)/256*(prediction >= 0 ? 1 : -1);
                    int sample = (int)Math.Clamp(predicted+(long)(nibble < 8 ? nibble : nibble-16)*deltas[channel],-32768,32767);
                    older[channel] = recent[channel];
                    recent[channel] = sample;
                    deltas[channel] = Math.Max(16,(int)Math.Floor(Adaptation[nibble]*(double)deltas[channel]/256));
                    output.Add((short)sample);
                }
            }
        }
        int length = (int)Math.Min(output.Count,total*channels);
        var wav = new byte[44+length*2];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4),(uint)(36+length*2));
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16),16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20),1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22),(ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24),(uint)rate);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28),(uint)(rate*channels*2));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32),(ushort)(channels*2));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34),16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40),(uint)(length*2));
        for (int i = 0; i < length; i++) BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44+2*i),output[i]);
        return wav;
    }
}
