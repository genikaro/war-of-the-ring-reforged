using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-210: a map's lighting pictures as map_lighting.write_images makes them: the cloud map
/// (TEXTURE_BASECLOUDS) and, when the map uses a skybox, its side, top and bottom faces, in RGB, a face not 512 x 512
/// resized to it with Pillow's Lanczos filter (Resample.c, ported to the pixel: the double coefficients normalised,
/// turned into 22-bit fixed point, a horizontal pass over the rows the vertical one reads, then the vertical one).</summary>
public static class MapPictures
{
    public const int SkySize = 512;
    public const string CloudTexture = "TEXTURE_BASECLOUDS";

    /// <summary>The pictures by the path the lighting block names (map/clouds.png, map/sky-side.png, ...).</summary>
    public static Dictionary<string,Picture> Build(World world,LqrDatabase db,ModelResolver resolver)
    {
        Picture Texture(int id,int? size = null)
        {
            var entry = resolver.Texture(id);
            if (entry == null || entry["exists"] is not true) throw new InvalidDataException($"missing texture {id}");
            var image = Images.Decode(resolver.Read((string)entry["file"]!),(string)entry["file"]!);
            var picture = new Picture(image.Width,image.Height,false,image.Rgb());
            return size is { } s && (picture.Width != s || picture.Height != s) ? Lanczos(picture,s,s) : picture;
        }
        var result = new Dictionary<string,Picture> { ["map/clouds.png"] = Texture(db["Textures"].Get(CloudTexture)!.Id) };
        var final = world.Tail.Final;
        if (final[0] == 1 && db["Skyboxes"].Get((int)final[4]) is { } record)
            foreach (var (face,column) in PackMap.SkyFaces) result[$"map/sky-{face}.png"] = Texture(record.Int(column),SkySize);
        return result;
    }

    private static double Sinc(double x)
    {
        if (x == 0.0) return 1.0;
        x *= Math.PI;
        return Math.Sin(x)/x;
    }

    private static double LanczosFilter(double x) => -3.0 <= x && x < 3.0 ? Sinc(x)*Sinc(x/3) : 0.0;

    private const int PrecisionBits = 32-8-2;

    /// <summary>Resample.c precompute_coeffs and normalize_coeffs_8bpc for one axis of the whole image.</summary>
    private static (int Size,int[] Bounds,int[] Coefficients) Coefficients(int inSize,int outSize)
    {
        float in0 = 0, in1 = inSize;
        double scale = (double)(in1-in0)/outSize, filterscale = scale < 1.0 ? 1.0 : scale;
        double support = 3.0*filterscale;
        int ksize = (int)Math.Ceiling(support)*2+1;
        var kk = new double[outSize*ksize];
        var bounds = new int[outSize*2];
        for (int xx = 0; xx < outSize; xx++)
        {
            double center = in0+(xx+0.5)*scale, ww = 0.0, ss = 1.0/filterscale;
            int xmin = (int)(center-support+0.5);
            if (xmin < 0) xmin = 0;
            int xmax = (int)(center+support+0.5);
            if (xmax > inSize) xmax = inSize;
            xmax -= xmin;
            int x;
            for (x = 0; x < xmax; x++)
            {
                double w = LanczosFilter((x+xmin-center+0.5)*ss);
                kk[xx*ksize+x] = w;
                ww += w;
            }
            for (x = 0; x < xmax; x++) if (ww != 0.0) kk[xx*ksize+x] /= ww;
            bounds[xx*2] = xmin; bounds[xx*2+1] = xmax;
        }
        var fixedPoint = new int[kk.Length];
        for (int i = 0; i < kk.Length; i++)
            fixedPoint[i] = kk[i] < 0 ? (int)(-0.5+kk[i]*(1 << PrecisionBits)) : (int)(0.5+kk[i]*(1 << PrecisionBits));
        return (ksize,bounds,fixedPoint);
    }

    private static byte Clip8(int value)
    {
        int shifted = value >> PrecisionBits;
        return (byte)(shifted < 0 ? 0 : shifted > 255 ? 255 : shifted);
    }

    /// <summary>Image.resize((width, height), Image.LANCZOS) of an RGB picture.</summary>
    public static Picture Lanczos(Picture source,int width,int height)
    {
        var (ksizeH,boundsH,kkH) = Coefficients(source.Width,width);
        var (ksizeV,boundsV,kkV) = Coefficients(source.Height,height);
        bool horizontal = width != source.Width, vertical = height != source.Height;
        int first = boundsV[0], last = boundsV[height*2-2]+boundsV[height*2-1];
        byte[] pixels = source.Pixels; int rows = source.Height, columns = source.Width, offset = 0;
        if (horizontal)
        {
            for (int i = 0; i < height; i++) boundsV[i*2] -= first;
            rows = last-first;
            var temp = new byte[width*rows*3];
            for (int yy = 0; yy < rows; yy++)
                for (int xx = 0; xx < width; xx++)
                {
                    int xmin = boundsH[xx*2], xmax = boundsH[xx*2+1];
                    int s0 = 1 << (PrecisionBits-1), s1 = s0, s2 = s0;
                    for (int x = 0; x < xmax; x++)
                    {
                        int k = kkH[xx*ksizeH+x], at = ((yy+first)*source.Width+x+xmin)*3;
                        s0 += pixels[at]*k; s1 += pixels[at+1]*k; s2 += pixels[at+2]*k;
                    }
                    int o = (yy*width+xx)*3;
                    temp[o] = Clip8(s0); temp[o+1] = Clip8(s1); temp[o+2] = Clip8(s2);
                }
            pixels = temp; columns = width;
        }
        else offset = 0;
        if (!vertical) return new(columns,rows,false,pixels);
        var output = new byte[columns*height*3];
        for (int yy = 0; yy < height; yy++)
        {
            int ymin = boundsV[yy*2], ymax = boundsV[yy*2+1];
            for (int xx = 0; xx < columns; xx++)
            {
                int s0 = 1 << (PrecisionBits-1), s1 = s0, s2 = s0;
                for (int y = 0; y < ymax; y++)
                {
                    int k = kkV[yy*ksizeV+y], at = ((y+ymin+offset)*columns+xx)*3;
                    s0 += pixels[at]*k; s1 += pixels[at+1]*k; s2 += pixels[at+2]*k;
                }
                int o = (yy*columns+xx)*3;
                output[o] = Clip8(s0); output[o+1] = Clip8(s1); output[o+2] = Clip8(s2);
            }
        }
        return new(columns,height,false,output);
    }
}
