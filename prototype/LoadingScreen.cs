using Godot;

/// <summary>The loading screen (build-147), after the original Menu_Loading_Screen: the parchment with the game's logo
/// (menu_loading_screen_background) and the One Ring as the progress bar, the plain gold ring
/// (menu_loading_screen_bar_background) turning into the glowing, inscribed one (menu_loading_screen_bar_fill) from
/// left to right, in the original's rectangle (CONTROL_MENU_LOADING_BAR at 0.3335, 0.6989, 0.333 × 0.2109 of the
/// screen). The 1024 × 768 art keeps its shape in any window, with black beside it. The map's name sits between the
/// logo and the ring and the step being loaded under the ring, in the Immortal font; the original shows neither.</summary>
public partial class LoadingScreen : Control
{
    private static readonly Vector2 ArtSize = new(1024,768);
    private static readonly Rect2 RingRect = new(.3335f*1024,.6989f*768,.333f*1024,.2109f*768);
    private static readonly Color Ink = new("3b2a14"),InkSoft = new("5f4a2e");
    private Texture2D? _parchment,_plainRing,_glowingRing;
    private Font? _titleFont,_stepFont;
    private float _target,_shown;

    /// <summary>The map or screen being loaded, and the step under way.</summary>
    public string Title { get; set; } = "";
    public string Step { get; set; } = "";
    /// <summary>Completion from 0 to 1; the ring eases toward it so steps of uneven length still fill smoothly.</summary>
    public float Progress { get => _target; set { _target = Math.Clamp(value,0,1); if (_target < _shown) _shown = _target; } }
    public float ShownProgress => _shown;
    public bool ArtLoaded => _parchment != null && _plainRing != null && _glowingRing != null;

    public override void _Ready()
    {
        Name = "LoadingScreen"; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var art = FrontEndArt.Shared;
        _parchment = art.Art("menu_loading_screen_background");
        _plainRing = art.Art("menu_loading_screen_bar_background");
        _glowingRing = art.Art("menu_loading_screen_bar_fill");
        _titleFont = art.Immortal(24) ?? ThemeDB.FallbackFont; _stepFont = art.Immortal(16) ?? ThemeDB.FallbackFont;
    }

    /// <summary>Jumps the ring to the progress set, for a screen shown mid-load or a capture.</summary>
    public void Settle() { _shown = _target; QueueRedraw(); }

    public override void _Process(double delta)
    {
        if (_shown >= _target) return;
        _shown = Math.Min(_target,_shown+Math.Max((_target-_shown)*(float)delta*8,(float)delta*.15f));
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),Colors.Black);
        float scale = Math.Min(Size.X/ArtSize.X,Size.Y/ArtSize.Y);
        var origin = ((Size-ArtSize*scale)/2).Floor();
        Rect2 Place(Rect2 rect) => new(origin+rect.Position*scale,rect.Size*scale);
        if (_parchment != null) DrawTextureRect(_parchment,Place(new(Vector2.Zero,ArtSize)),false);
        else DrawRect(Place(new(Vector2.Zero,ArtSize)),new Color("cdb98c"));
        var ring = Place(RingRect);
        if (_plainRing != null && _glowingRing != null)
        {
            DrawTextureRect(_plainRing,ring,false);
            if (_shown > 0)
            {
                var size = _glowingRing.GetSize();
                DrawTextureRectRegion(_glowingRing,new Rect2(ring.Position,new(ring.Size.X*_shown,ring.Size.Y)),new Rect2(0,0,size.X*_shown,size.Y));
            }
        }
        else
        {
            var bar = new Rect2(ring.Position+new Vector2(0,ring.Size.Y*.4f),new(ring.Size.X,ring.Size.Y*.2f));
            DrawRect(bar,new Color(0,0,0,.35f)); DrawRect(new Rect2(bar.Position,new(bar.Size.X*_shown,bar.Size.Y)),new Color("d8a63c"));
        }
        DrawCentered(_titleFont,Title,Place(new(160,470,704,44)),Mathf.RoundToInt(27*scale),Ink);
        DrawCentered(_stepFont,Step,Place(new(160,702,704,26)),Mathf.RoundToInt(16*scale),InkSoft);
    }

    private void DrawCentered(Font? font,string text,Rect2 box,int size,Color color)
    {
        if (font == null || text == "" || size < 4) return;
        float ascent = font.GetAscent(size),height = font.GetHeight(size);
        DrawString(font,new Vector2(box.Position.X,box.Position.Y+(box.Size.Y-height)/2+ascent),text,HorizontalAlignment.Center,box.Size.X,size,color);
    }
}
