using System.Buffers.Binary;
using System.IO.Compression;

namespace Reforged.Formats;

/// <summary>A decoded picture as Pillow opens it: RGBA rows top to bottom, and whether the file has an alpha channel
/// (Pillow's mode RGBA; DXT1 and DXT5 always do, a 24-bit TGA does not).</summary>
public sealed record DecodedImage(int Width,int Height,bool AlphaChannel,byte[] Rgba)
{
    /// <summary>The smallest alpha value (255 without an alpha channel), Pillow's getextrema()[-1][0].</summary>
    public int MinimumAlpha()
    {
        if (!AlphaChannel) return 255;
        int minimum = 255;
        for (int i = 3; i < Rgba.Length && minimum > 0; i += 4) minimum = Math.Min(minimum,Rgba[i]);
        return minimum;
    }

    /// <summary>Pillow's convert('RGB'): the colour channels only.</summary>
    public byte[] Rgb()
    {
        var rgb = new byte[Width*Height*3];
        for (int i = 0, j = 0; i < Rgba.Length; i += 4, j += 3) { rgb[j] = Rgba[i]; rgb[j+1] = Rgba[i+1]; rgb[j+2] = Rgba[i+2]; }
        return rgb;
    }
}

/// <summary>Build-197: the texture files models use, decoded as Pillow decodes them, so a model built from the install
/// matches the import's: DDS DXT1 and DXT5 (Pillow's BCn decoder, its integer interpolation included) and TGA
/// (uncompressed and RLE truecolour, 24 or 32 bits, either row order). PNG encodes the result for a glTF image.</summary>
public static class Images
{
    public static DecodedImage Decode(byte[] data,string name)
    {
        if (data.Length >= 4 && data.AsSpan(0,4).SequenceEqual("DDS "u8)) return Dds(data,name);
        if (name.EndsWith(".tga",StringComparison.OrdinalIgnoreCase)) return Tga(data,name);
        throw new InvalidDataException($"{name}: not a DDS or TGA picture");
    }

    /// <summary>Build-201: a DDS or TGA picture's width and height from its header, as Pillow's Image.size gives them.</summary>
    public static (int Width,int Height) Size(byte[] data,string name)
    {
        if (data.Length >= 128 && data.AsSpan(0,4).SequenceEqual("DDS "u8))
            return (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)),BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12)));
        if (name.EndsWith(".tga",StringComparison.OrdinalIgnoreCase) && data.Length >= 18)
            return (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)),BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)));
        throw new InvalidDataException($"{name}: not a DDS or TGA picture");
    }

    // ---- DDS ----------------------------------------------------------------------------------------------------

    public static DecodedImage Dds(byte[] data,string name)
    {
        if (data.Length < 128) throw new InvalidDataException($"{name}: DDS header cut short");
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12)), width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        uint formatFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(80));
        string fourCc = System.Text.Encoding.ASCII.GetString(data,84,4);
        if (width <= 0 || height <= 0 || (formatFlags & 4) == 0 || fourCc is not ("DXT1" or "DXT5"))
            throw new InvalidDataException($"{name}: unsupported DDS ({fourCc}, flags 0x{formatFlags:x})");
        bool dxt5 = fourCc == "DXT5";
        int blockSize = dxt5 ? 16 : 8, blocksWide = (width+3)/4, blocksHigh = (height+3)/4;
        if (128L+(long)blocksWide*blocksHigh*blockSize > data.Length) throw new InvalidDataException($"{name}: DDS data cut short");
        var rgba = new byte[width*height*4];
        Span<byte> block = stackalloc byte[64];
        int offset = 128;
        for (int by = 0; by < blocksHigh; by++)
            for (int bx = 0; bx < blocksWide; bx++, offset += blockSize)
            {
                if (dxt5) { Bc1Colour(data.AsSpan(offset+8),block,true); Bc3Alpha(data.AsSpan(offset),block); }
                else Bc1Colour(data.AsSpan(offset),block,false);
                for (int py = 0; py < 4; py++)
                {
                    int y = by*4+py;
                    if (y >= height) break;
                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx*4+px;
                        if (x >= width) break;
                        block.Slice((py*4+px)*4,4).CopyTo(rgba.AsSpan((y*width+x)*4));
                    }
                }
            }
        return new(width,height,true,rgba);
    }

    private static void Rgb565(ushort v,out int r,out int g,out int b)
    {
        r = (v & 0xf800) >> 8; r |= r >> 5;
        g = (v & 0x07e0) >> 3; g |= g >> 6;
        b = (v & 0x001f) << 3; b |= b >> 5;
    }

    /// <summary>Pillow's decode_bc1_color: DXT5 colour blocks always take the four-colour branch.</summary>
    private static void Bc1Colour(ReadOnlySpan<byte> src,Span<byte> block,bool separateAlpha)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(src), c1 = BinaryPrimitives.ReadUInt16LittleEndian(src[2..]);
        uint lut = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);
        Rgb565(c0,out int r0,out int g0,out int b0);
        Rgb565(c1,out int r1,out int g1,out int b1);
        Span<byte> p = stackalloc byte[16];
        p[0] = (byte)r0; p[1] = (byte)g0; p[2] = (byte)b0; p[3] = 255;
        p[4] = (byte)r1; p[5] = (byte)g1; p[6] = (byte)b1; p[7] = 255;
        if (c0 > c1 || separateAlpha)
        {
            p[8] = (byte)((2*r0+r1)/3); p[9] = (byte)((2*g0+g1)/3); p[10] = (byte)((2*b0+b1)/3); p[11] = 255;
            p[12] = (byte)((r0+2*r1)/3); p[13] = (byte)((g0+2*g1)/3); p[14] = (byte)((b0+2*b1)/3); p[15] = 255;
        }
        else
        {
            p[8] = (byte)((r0+r1)/2); p[9] = (byte)((g0+g1)/2); p[10] = (byte)((b0+b1)/2); p[11] = 255;
            p[12] = 0; p[13] = 0; p[14] = 0; p[15] = 0;
        }
        for (int n = 0; n < 16; n++) p.Slice((int)(lut >> (2*n) & 3)*4,4).CopyTo(block[(n*4)..]);
    }

    /// <summary>Pillow's decode_bc3_alpha (unsigned).</summary>
    private static void Bc3Alpha(ReadOnlySpan<byte> src,Span<byte> block)
    {
        int a0 = src[0], a1 = src[1];
        Span<byte> a = stackalloc byte[8];
        a[0] = (byte)a0; a[1] = (byte)a1;
        if (a0 > a1)
            for (int i = 1; i <= 6; i++) a[i+1] = (byte)(((7-i)*a0+i*a1)/7);
        else
        {
            for (int i = 1; i <= 4; i++) a[i+1] = (byte)(((5-i)*a0+i*a1)/5);
            a[6] = 0; a[7] = 255;
        }
        int lut1 = src[2] | src[3] << 8 | src[4] << 16, lut2 = src[5] | src[6] << 8 | src[7] << 16;
        for (int n = 0; n < 8; n++) block[n*4+3] = a[lut1 >> (3*n) & 7];
        for (int n = 0; n < 8; n++) block[(8+n)*4+3] = a[lut2 >> (3*n) & 7];
    }

    // ---- TGA ----------------------------------------------------------------------------------------------------

    public static DecodedImage Tga(byte[] data,string name)
    {
        if (data.Length < 18) throw new InvalidDataException($"{name}: TGA header cut short");
        int idLength = data[0], colourMap = data[1], type = data[2], bits = data[16], descriptor = data[17];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)), height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
        if (colourMap != 0 || type is not (2 or 10) || bits is not (24 or 32) || width == 0 || height == 0)
            throw new InvalidDataException($"{name}: unsupported TGA (type {type}, {bits} bits)");
        int size = bits/8, position = 18+idLength;
        var pixels = new byte[width*height*size];
        if (type == 2)
        {
            if (position+pixels.Length > data.Length) throw new InvalidDataException($"{name}: TGA data cut short");
            data.AsSpan(position,pixels.Length).CopyTo(pixels);
        }
        else
            for (int done = 0; done < pixels.Length;)
            {
                if (position >= data.Length) throw new InvalidDataException($"{name}: TGA data cut short");
                int header = data[position++], count = (header & 0x7f)+1;
                int bytes = Math.Min(count*size,pixels.Length-done);
                if (header >= 0x80)
                {
                    if (position+size > data.Length) throw new InvalidDataException($"{name}: TGA data cut short");
                    for (int i = 0; i < bytes; i++) pixels[done+i] = data[position+i%size];
                    position += size;
                }
                else
                {
                    if (position+bytes > data.Length) throw new InvalidDataException($"{name}: TGA data cut short");
                    data.AsSpan(position,bytes).CopyTo(pixels.AsSpan(done));
                    position += count*size;
                }
                done += bytes;
            }
        bool topDown = (descriptor & 0x20) != 0;
        var rgba = new byte[width*height*4];
        for (int y = 0; y < height; y++)
        {
            int row = topDown ? y : height-1-y;
            for (int x = 0; x < width; x++)
            {
                int s = (row*width+x)*size, d = (y*width+x)*4;
                rgba[d] = pixels[s+2]; rgba[d+1] = pixels[s+1]; rgba[d+2] = pixels[s]; rgba[d+3] = size == 4 ? pixels[s+3] : (byte)255;
            }
        }
        return new(width,height,size == 4,rgba);
    }

    // ---- PNG ----------------------------------------------------------------------------------------------------

    /// <summary>A PNG of 8-bit RGB or RGBA rows (no filtering), for a glTF image.</summary>
    public static byte[] Png(int width,int height,byte[] pixels,bool alpha)
    {
        int channels = alpha ? 4 : 3, stride = width*channels;
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw,CompressionLevel.Fastest,true))
            for (int y = 0; y < height; y++) { z.WriteByte(0); z.Write(pixels,y*stride,stride); }
        using var png = new MemoryStream();
        png.Write([0x89,(byte)'P',(byte)'N',(byte)'G',0x0D,0x0A,0x1A,0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header,width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..],height);
        header[8] = 8; header[9] = (byte)(alpha ? 6 : 2); header[10] = 0; header[11] = 0; header[12] = 0;
        Chunk(png,"IHDR",header);
        Chunk(png,"IDAT",raw.ToArray());
        Chunk(png,"IEND",[]);
        return png.ToArray();
    }

    private static void Chunk(Stream png,string type,ReadOnlySpan<byte> payload)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word,payload.Length);
        png.Write(word);
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. payload];
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(word,Crc32.Compute(typed));
        png.Write(word);
    }
}
