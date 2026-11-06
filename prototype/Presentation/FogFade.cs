namespace Reforged.Presentation;

/// <summary>Build-167: the fog overlay's sight texture, worked out without Godot so a check can compare it with the
/// build-150 loops it replaces (tests/simulation/FogFadeChecks.cs). A new sight is blurred 3 x 3 into Target (a cell at
/// the edge of sight is part lit), and each frame every shown value moves toward its target by the frame's step. The
/// blur adds rows and columns in running sums instead of nine reads per cell, and the fade visits only cells still
/// moving: the others keep their byte, which is all the old full pass wrote for them. Same floats, same bytes.</summary>
public sealed class FogFade
{
    private readonly int _width,_height;
    private readonly int[] _rows;
    private readonly int[] _active;
    private int _activeCount;

    public float[] Target { get; }
    public float[] Shown { get; }
    /// <summary>Shown as the texture's bytes, (byte)(shown*255+0.5) for every cell.</summary>
    public byte[] Bytes { get; }
    /// <summary>True once every shown value has reached its target; a new sight unsettles it.</summary>
    public bool Settled { get; private set; }
    public int Moving => _activeCount;

    public FogFade(int width,int height)
    {
        _width = width; _height = height;
        Target = new float[width*height]; Shown = new float[width*height]; Bytes = new byte[width*height];
        _rows = new int[width*height]; _active = new int[width*height];
    }

    /// <summary>A new sight (one byte per cell, the sides' sight together): its 3 x 3 box blur becomes the target, and
    /// every cell whose shown value now differs from it will move.</summary>
    public void Retarget(byte[] visible)
    {
        int w = _width,h = _height;
        // Each cell's sum over its row neighbours (in bounds), then over the column of those sums.
        for (int z = 0; z < h; z++)
        {
            int row = z*w;
            for (int x = 0; x < w; x++)
            {
                int sum = visible[row+x];
                if (x > 0) sum += visible[row+x-1];
                if (x < w-1) sum += visible[row+x+1];
                _rows[row+x] = sum;
            }
        }
        _activeCount = 0;
        for (int z = 0; z < h; z++)
        {
            int rowCount = 1+(z > 0 ? 1 : 0)+(z < h-1 ? 1 : 0);
            for (int x = 0; x < w; x++)
            {
                int i = z*w+x,lit = _rows[i];
                if (z > 0) lit += _rows[i-w];
                if (z < h-1) lit += _rows[i+w];
                int columnCount = 1+(x > 0 ? 1 : 0)+(x < w-1 ? 1 : 0);
                float target = (float)lit/(columnCount*rowCount);
                Target[i] = target;
                if (Shown[i] != target) _active[_activeCount++] = i;
            }
        }
        Settled = false;
    }

    /// <summary>One frame of fading: each moving cell steps toward its target (1 jumps straight there).</summary>
    public void Step(float step)
    {
        int kept = 0;
        for (int k = 0; k < _activeCount; k++)
        {
            int i = _active[k];
            float shown = Shown[i],target = Target[i];
            if (shown != target)
            {
                shown = Math.Abs(target-shown) <= step ? target : shown+Math.Sign(target-shown)*step;
                Shown[i] = shown;
            }
            Bytes[i] = (byte)(shown*255+.5f);
            if (shown != target) _active[kept++] = i;
        }
        _activeCount = kept;
        Settled = kept == 0;
    }
}
