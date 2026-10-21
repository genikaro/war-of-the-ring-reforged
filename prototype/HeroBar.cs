using Godot;
using Reforged.Simulation;

/// <summary>Right-edge hero portraits from the source GameConsole_Hero_Portraits.lsr: one slot per living hero,
/// with a health bar and an experience bar beneath the portrait.</summary>
public partial class HeroBar : Control
{
    /// <summary>Hero unit id, double click (select and center) and Shift (add to the selection).</summary>
    public event Action<int,bool,bool>? HeroChosen;
    // Source rectangles as fractions of a 1024x768 screen. HERO_PORTRAIT_0..4 stack down from y .2481;
    // the separate SPECIAL slot above them (y .1546) has no recovered use and stays empty.
    private const float Left = .9467f,Width = .0474f,Top = .2481f,Step = .0909f,PortraitHeight = .0633f;
    private const float BarTop = .0652f,BarsHeight = .0182f,FillLeft = .9485f,FillWidth = .0437f,FillHeight = .005f,HealthFill = .0668f,ExperienceFill = .0761f;
    public const int Slots = 5;
    private readonly (Button Portrait,TextureRect Frame,TextureRect Bars,TextureRect Health,TextureRect Experience)[] _slots = new (Button,TextureRect,TextureRect,TextureRect,TextureRect)[Slots];
    private readonly int[] _ids = Enumerable.Repeat(-1,Slots).ToArray();
    private float _scale = 1;

    public override void _Ready()
    {
        Name = "HeroBar"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (int slot = 0; slot < Slots; slot++)
        {
            int index = slot;
            var portrait = new Button { Name = "Hero"+slot,FocusMode = FocusModeEnum.None,ExpandIcon = true,Visible = false };
            foreach (string style in new[] {"normal","hover","pressed","disabled"})
                portrait.AddThemeStyleboxOverride(style,new StyleBoxFlat { BgColor = new Color(0,0,0,.35f) });
            portrait.GuiInput += input => {
                if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left,Pressed: true } mouse || _ids[index] < 0) return;
                HeroChosen?.Invoke(_ids[index],mouse.DoubleClick,mouse.ShiftPressed); portrait.AcceptEvent();
            };
            var frame = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Ignore };
            portrait.AddChild(frame);
            TextureRect Bar() => new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,Visible = false };
            var bars = Bar(); var health = Bar(); var experience = Bar();
            AddChild(portrait); AddChild(bars); AddChild(health); AddChild(experience);
            _slots[slot] = (portrait,frame,bars,health,experience);
        }
    }

    public void LoadArtwork(CommandPanel art)
    {
        foreach (var slot in _slots)
        {
            slot.Frame.Texture = art.Art("console_frameportrait"); slot.Bars.Texture = art.Art("console_g_doublebarbackgroundsmall");
            slot.Health.Texture = art.Art("console_g_healthbarsmall"); slot.Experience.Texture = art.Art("console_g_staminabarsmall");
            slot.Health.Modulate = new Color("5bd03f");
        }
    }

    /// <summary>Lays out the slots for the console scale (1 = the original 1024x768 screen) and shows the living heroes in order.</summary>
    public void Refresh(IReadOnlyList<SimUnit> heroes,Func<string,Texture2D?> portrait,float scale)
    {
        _scale = scale; var viewport = GetViewportRect().Size;
        float X(float fraction) => viewport.X-(1-fraction)*1024*scale;
        for (int index = 0; index < Slots; index++)
        {
            var slot = _slots[index]; var hero = index < heroes.Count ? heroes[index] : null;
            _ids[index] = hero?.Id ?? -1;
            slot.Portrait.Visible = slot.Bars.Visible = slot.Health.Visible = slot.Experience.Visible = hero != null;
            if (hero == null) continue;
            float top = (Top+index*Step)*768*scale;
            slot.Portrait.Position = new(X(Left),top); slot.Portrait.Size = new Vector2(Width*1024,PortraitHeight*768)*scale;
            slot.Frame.Size = slot.Portrait.Size;
            slot.Portrait.Icon = portrait(CommandPanel.PortraitName(hero.Definition.DisplayName));
            slot.Bars.Position = new(X(Left),top+BarTop*768*scale); slot.Bars.Size = new Vector2(Width*1024,BarsHeight*768)*scale;
            float health = Math.Clamp(hero.HealthMilli/(float)(hero.MaxHealth*1000L),0,1);
            slot.Health.Position = new(X(FillLeft),top+HealthFill*768*scale); slot.Health.Size = new Vector2(FillWidth*1024*health,FillHeight*768)*scale;
            // Experience and levels are not simulated yet; the bar stays at the level-1 start.
            slot.Experience.Position = new(X(FillLeft),top+ExperienceFill*768*scale); slot.Experience.Size = new Vector2(0,FillHeight*768)*scale;
            slot.Portrait.TooltipText = $"{hero.Definition.DisplayName} · {hero.Health}/{hero.MaxHealth} HP · Level 1"
                + (hero.RingActive ? "\nWearing the One Ring" : "")
                + "\nClick to select · double-click to select and center the camera\nExperience and levels are not simulated yet.";
        }
    }

    public Button Portrait(int slot) => _slots[slot].Portrait;
    public int HeroIn(int slot) => _ids[slot];
    public float HealthWidth(int slot) => _slots[slot].Health.Size.X/_scale;
    public bool Contains(Vector2 point) => _slots.Any(s => s.Portrait.Visible && s.Portrait.GetGlobalRect().HasPoint(point));
}
