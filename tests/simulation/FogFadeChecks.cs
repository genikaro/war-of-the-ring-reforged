using Reforged.Presentation;

/// <summary>Build-167: FogFade gives the fog overlay exactly what the build-150 loops gave it (kept here as Old): the
/// same targets, shown values, texture bytes and settled state, frame after frame, through new sights, side changes
/// (a jump) and uneven frame steps, on maps down to one cell wide.</summary>
static class FogFadeChecks
{
    private sealed class Old(int width,int height)
    {
        public readonly float[] Target = new float[width*height],Shown = new float[width*height];
        public readonly byte[] Bytes = new byte[width*height];
        public bool Settled;

        public void Retarget(byte[] visible)
        {
            Settled = false;
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    int lit = 0,count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = x+dx,pz = z+dz;
                            if (px < 0 || pz < 0 || px >= width || pz >= height) continue;
                            count++; lit += visible[pz*width+px];
                        }
                    Target[z*width+x] = (float)lit/count;
                }
        }

        public void Step(float step)
        {
            bool moving = false;
            for (int i = 0; i < Shown.Length; i++)
            {
                float shown = Shown[i],target = Target[i];
                if (shown != target)
                {
                    shown = Math.Abs(target-shown) <= step ? target : shown+Math.Sign(target-shown)*step;
                    Shown[i] = shown; moving |= shown != target;
                }
                Bytes[i] = (byte)(shown*255+.5f);
            }
            Settled = !moving;
        }
    }

    public static void Run(Action<bool,string> check)
    {
        var random = new Random(164);
        int frames = 0,mismatches = 0; string? first = null;
        foreach (var (width,height) in new[] { (1,1),(1,7),(9,1),(2,2),(17,13),(64,48),(128,96) })
        {
            var old = new Old(width,height); var fade = new FogFade(width,height);
            var visible = new byte[width*height];
            for (int frame = 0; frame < 220; frame++)
            {
                // A new sight about one frame in four (4 Hz at 1x, 16x in bursts), and now and then a side change.
                bool jump = frame == 0 || random.Next(40) == 0;
                if (jump || random.Next(4) == 0)
                {
                    // Sight moves in blobs, as units walk: flip a few discs, sometimes the whole map.
                    if (random.Next(25) == 0) Array.Fill(visible,(byte)random.Next(2));
                    for (int blob = random.Next(1,4); blob > 0; blob--)
                    {
                        int cx = random.Next(width),cz = random.Next(height),r = random.Next(1,6); byte value = (byte)random.Next(2);
                        for (int z = Math.Max(0,cz-r); z <= Math.Min(height-1,cz+r); z++)
                            for (int x = Math.Max(0,cx-r); x <= Math.Min(width-1,cx+r); x++)
                                if ((x-cx)*(x-cx)+(z-cz)*(z-cz) <= r*r) visible[z*width+x] = value;
                    }
                    old.Retarget(visible); fade.Retarget(visible);
                }
                // Frames from 1/240 s to a 0.1 s hitch, against the 0.33 s fade; a jump steps all the way.
                float step = jump ? 1 : Math.Min(1,(float)(random.NextDouble()*.1+1/240.0)/.33f);
                bool run = jump || !old.Settled;
                if (run != (jump || !fade.Settled)) { mismatches++; first ??= $"{width}x{height} frame {frame} settled"; }
                if (run) { old.Step(step); fade.Step(step); }
                frames++;
                bool same = old.Settled == fade.Settled && old.Target.AsSpan().SequenceEqual(fade.Target)
                    && old.Shown.AsSpan().SequenceEqual(fade.Shown) && old.Bytes.AsSpan().SequenceEqual(fade.Bytes);
                if (!same) { mismatches++; first ??= $"{width}x{height} frame {frame}"; }
            }
        }
        check(frames > 1000 && mismatches == 0,
            $"fog fade matches the build-150 loops: targets, shown values, bytes and settling ({frames} frames{(first == null ? "" : ", first mismatch "+first)})");
    }
}
