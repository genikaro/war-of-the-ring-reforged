namespace Reforged.Presentation;

public readonly record struct HudLayout(double Left,double Top,double Width,double Height,double Scale)
{
    public static HudLayout Fit(double width,double height,double percent = 100)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || !double.IsFinite(percent))
            throw new ArgumentOutOfRangeException(nameof(width));
        double scale = Math.Min(width/1024,Math.Min(width/1024,height/768)*Math.Clamp(percent,75,150)/100);
        return new((width-1024*scale)/2,height-212*scale,1024*scale,212*scale,scale);
    }
}
