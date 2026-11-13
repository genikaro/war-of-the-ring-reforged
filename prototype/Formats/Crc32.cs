namespace Reforged.Formats;

/// <summary>Build-192: zlib's CRC-32 (reflected polynomial 0xEDB88320), the checksum H2O archives store for every
/// file. Slicing by eight: eight bytes per step through eight 256-entry tables.</summary>
public static class Crc32
{
    private static readonly uint[] Tables = MakeTables();

    private static uint[] MakeTables()
    {
        var tables = new uint[8*256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            tables[n] = c;
        }
        for (int t = 1; t < 8; t++)
            for (int n = 0; n < 256; n++) tables[t*256+n] = (tables[(t-1)*256+n] >> 8) ^ tables[tables[(t-1)*256+n] & 0xFF];
        return tables;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        var t = Tables;
        int i = 0;
        for (; i+8 <= data.Length; i += 8)
        {
            uint low = crc ^ (uint)(data[i] | data[i+1] << 8 | data[i+2] << 16 | data[i+3] << 24);
            uint high = (uint)(data[i+4] | data[i+5] << 8 | data[i+6] << 16 | data[i+7] << 24);
            crc = t[7*256+(low & 0xFF)] ^ t[6*256+((low >> 8) & 0xFF)] ^ t[5*256+((low >> 16) & 0xFF)] ^ t[4*256+(low >> 24)]
                ^ t[3*256+(high & 0xFF)] ^ t[2*256+((high >> 8) & 0xFF)] ^ t[256+((high >> 16) & 0xFF)] ^ t[high >> 24];
        }
        for (; i < data.Length; i++) crc = t[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
