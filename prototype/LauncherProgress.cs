using Godot;
using Reforged.Presentation;

/// <summary>The launcher's import progress (build-191): a row of the import's steps (done, running, still to come) over
/// a wide bar marked at the step boundaries, with the percentage on it and a sheen running along it while the import
/// works. The bar glides to each reported value instead of jumping, and never goes back while an import runs; it turns
/// green when the import finishes, red when it fails and amber when it is cancelled.</summary>
public partial class LauncherProgress : Control
{
    public enum Phase { Idle,Running,Done,Failed,Cancelled }

    private static readonly Color Gold = new("d8b574"),Cream = new("fff3cb"),Dim = new("8f8670"),Good = new("9fd37a"),
        Bad = new("ff8a6e"),Warn = new("f0c75e"),Trough = new(.02f,.03f,.04f);
    private const float StepsHeight = 26,BarTop = 34,BarHeight = 24;

    private readonly List<(string Title,double Start,double End)> _steps = [];
    private Phase _phase;
    private int _current;
    private double _target,_shown;
    private float _time;

    /// <summary>The value the bar is gliding to, 0 to 1.</summary>
    public double Target => _target;
    /// <summary>The value the bar shows now.</summary>
    public double Shown => _shown;

    public LauncherProgress()
    {
        CustomMinimumSize = new(0,BarTop+BarHeight+2);
        MouseFilter = MouseFilterEnum.Ignore;
        // Until the import sends its plan: the steps it is known to take, with their measured shares.
        SetPlan([("",0,.01),("",.01,.07),("",.07,.15),("",.15,.16),("",.16,1)]);
    }

    public void SetPlan(IEnumerable<(string Title,double Start,double End)> steps)
    {
        _steps.Clear();
        int number = 0;
        foreach (var (title,start,end) in steps)
            _steps.Add((GameText.TryT("launcher.step."+ ++number) ?? title,start,end));
        QueueRedraw();
    }

    /// <summary>A new import: the bar empties and the first step is current.</summary>
    public void Begin()
    {
        _phase = Phase.Running; _current = 0; _target = _shown = 0;
        QueueRedraw();
    }

    public void SetStep(int step) { _current = step; QueueRedraw(); }

    /// <summary>Moves the bar on to `value`; it never goes back while the import runs.</summary>
    public void Advance(double value) => _target = Math.Clamp(Math.Max(_target,value),0,1);

    public void Finish(Phase phase)
    {
        _phase = phase;
        // A finished import shows the full bar at once: no 99% left gliding on the screen.
        if (phase == Phase.Done) { _target = _shown = 1; _current = _steps.Count+1; }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        double before = _shown;
        // About a third of a second to close most of a gap: smooth, yet never far behind.
        _shown += (_target-_shown)*(1-Math.Exp(-delta*6));
        if (Math.Abs(_target-_shown) < .0005) _shown = _target;
        if (_phase == Phase.Running || before != _shown) QueueRedraw();
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        float width = Size.X;
        DrawSteps(font,width);

        var bar = new Rect2(0,BarTop,width,BarHeight);
        DrawRect(bar,Trough);
        Color tone = _phase switch { Phase.Done => Good,Phase.Failed => Bad,Phase.Cancelled => Warn,_ => Gold };
        float fill = (float)(width*_shown);
        if (fill > 0)
        {
            var filled = new Rect2(0,BarTop,fill,BarHeight);
            DrawRect(filled,tone.Darkened(.45f));
            DrawRect(new Rect2(0,BarTop,fill,BarHeight*.5f),tone.Darkened(.2f) with { A = .55f });
            DrawRect(new Rect2(0,BarTop,fill,2),tone.Lightened(.25f) with { A = .7f });
            if (_phase == Phase.Running) DrawSheen(fill);
        }
        // The step boundaries, so the bar reads as the steps above it.
        foreach (var (_,_,end) in _steps.Take(_steps.Count-1))
        {
            float x = (float)(width*end);
            DrawLine(new Vector2(x,BarTop+3),new Vector2(x,BarTop+BarHeight-3),x < fill ? new Color(0,0,0,.45f) : new Color(1,1,1,.12f),1);
        }
        DrawRect(bar,_phase == Phase.Idle ? Dim : tone,false,1);

        if (_phase != Phase.Idle)
        {
            string percent = $"{Math.Floor(_shown*100):0}%";
            const int size = 15;
            float textWidth = font.GetStringSize(percent,HorizontalAlignment.Left,-1,size).X;
            var at = new Vector2((width-textWidth)/2,BarTop+BarHeight/2+(font.GetAscent(size)-font.GetDescent(size))/2);
            DrawString(font,at+new Vector2(1,1),percent,HorizontalAlignment.Left,-1,size,new Color(0,0,0,.8f));
            DrawString(font,at,percent,HorizontalAlignment.Left,-1,size,Cream);
        }
    }

    /// <summary>A soft band of light sweeping along the filled part every two seconds or so.</summary>
    private void DrawSheen(float fill)
    {
        const float band = 90;
        float cycle = (_time*.55f)%1.3f;
        float center = cycle*(fill+band*2)-band;
        for (int i = 0; i < 12; i++)
        {
            float x0 = center-band/2+i*band/12,x1 = x0+band/12;
            float left = Math.Max(0,x0),right = Math.Min(fill,x1);
            if (right <= left) continue;
            float alpha = .16f*(1-Math.Abs(i-5.5f)/6);
            DrawRect(new Rect2(left,BarTop+1,right-left,BarHeight-2),new Color(1,.97f,.85f,alpha));
        }
    }

    private void DrawSteps(Font font,float width)
    {
        if (_steps.Count == 0) return;
        const int size = 13;
        const float radius = 7;
        float column = width/_steps.Count;
        for (int i = 0; i < _steps.Count; i++)
        {
            int number = i+1;
            bool done = number < _current || (_phase == Phase.Done),current = number == _current && _phase != Phase.Done;
            float x = i*column;
            var dot = new Vector2(x+radius+1,StepsHeight/2);
            Color color;
            if (done)
            {
                color = Good;
                DrawCircle(dot,radius,Good.Darkened(.35f));
                DrawPolyline([dot+new Vector2(-3.5f,0),dot+new Vector2(-1,2.8f),dot+new Vector2(3.8f,-2.8f)],Cream,1.8f,true);
            }
            else if (current)
            {
                color = _phase switch { Phase.Failed => Bad,Phase.Cancelled => Warn,_ => Cream };
                Color ring = _phase switch { Phase.Failed => Bad,Phase.Cancelled => Warn,_ => Gold };
                DrawArc(dot,radius,0,Mathf.Tau,24,ring,1.6f,true);
                float pulse = _phase == Phase.Running ? .55f+.45f*Mathf.Sin(_time*4) : 1;
                DrawCircle(dot,radius*.45f,ring with { A = .35f+.65f*pulse });
            }
            else
            {
                color = Dim;
                DrawArc(dot,radius,0,Mathf.Tau,24,Dim,1.2f,true);
            }
            float textX = x+radius*2+7,room = column-radius*2-12;
            string title = Fit(font,_steps[i].Title,size,room);
            DrawString(font,new Vector2(textX,StepsHeight/2+(font.GetAscent(size)-font.GetDescent(size))/2),title,HorizontalAlignment.Left,-1,size,color);
        }
    }

    /// <summary>The text cut with an ellipsis to fit `room` pixels.</summary>
    private static string Fit(Font font,string text,int size,float room)
    {
        if (font.GetStringSize(text,HorizontalAlignment.Left,-1,size).X <= room) return text;
        for (int length = text.Length-1; length > 0; length--)
        {
            string cut = text[..length].TrimEnd()+"…";
            if (font.GetStringSize(cut,HorizontalAlignment.Left,-1,size).X <= room) return cut;
        }
        return "";
    }
}
