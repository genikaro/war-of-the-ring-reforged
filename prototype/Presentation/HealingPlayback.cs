namespace Reforged.Presentation;

public readonly record struct HealingKey(double Time,double Width,double Height,uint ColorArgb);
public readonly record struct HealingSample(double Width,double Height,double Red,double Green,double Blue,double Alpha);

public static class HealingPlayback
{
    public static HealingSample Sample(IReadOnlyList<HealingKey> keys,double fraction)
    {
        fraction = Math.Clamp(fraction,0,1);
        var a = keys[0]; var b = a;
        foreach (var key in keys.Skip(1))
        {
            b = key;
            if (key.Time >= fraction) break;
            a = key;
        }
        double t = b.Time > a.Time ? Math.Clamp((fraction-a.Time)/(b.Time-a.Time),0,1) : 0;
        double Lerp(double x,double y) => x+(y-x)*t;
        double Channel(int shift) => Lerp((a.ColorArgb>>shift)&255,(b.ColorArgb>>shift)&255)/255;
        return new(Lerp(a.Width,b.Width),Lerp(a.Height,b.Height),Channel(16),Channel(8),Channel(0),Channel(24));
    }
}
