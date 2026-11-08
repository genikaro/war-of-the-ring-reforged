using Godot;
using Reforged.Simulation;
using Reforged.Presentation;

/// <summary>Right-edge hero portraits from the source GameConsole_Hero_Portraits.lsr: one slot per hero, with a health bar and an
/// experience bar beneath the portrait. Build-172: a fallen hero keeps a darkened portrait (the original shows one until he is
/// summoned again), health fills take the green / yellow / red of the other bars, and the SPECIAL slot above holds a summoned
/// Balrog or Giant Ent with its turquoise remaining life in the experience bar's place.</summary>
public partial class HeroBar : Control
{
    /// <summary>Hero unit id, double click (select and center) and Shift (add to the selection).</summary>
    public event Action<int,bool,bool>? HeroChosen;
    // Source rectangles as fractions of a 1024x768 screen. HERO_PORTRAIT_0..4 stack down from y .2481; the separate SPECIAL slot
    // above them (y .1546) holds a summon (the Evil compendium's captures 13-14 show the Balrog there).
    private const float Left = .9467f,Width = .0474f,Top = .2481f,Step = .0909f,PortraitHeight = .0633f,SpecialTop = .1546f;
    private const float BarTop = .0652f,BarsHeight = .0182f,FillLeft = .9485f,FillWidth = .0437f,FillHeight = .005f,HealthFill = .0668f,ExperienceFill = .0761f;
    public const int Slots = 5,SummonSlot = Slots;
    private readonly (Button Portrait,TextureRect Frame,TextureRect Bars,TextureRect Health,TextureRect Experience,Label Level)[] _slots = new (Button,TextureRect,TextureRect,TextureRect,TextureRect,Label)[Slots+1];
    private readonly int[] _ids = Enumerable.Repeat(-1,Slots+1).ToArray();
    private float _scale = 1;

    public override void _Ready()
    {
        Name = "HeroBar"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        for (int slot = 0; slot <= Slots; slot++)
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
            var level=new Label {MouseFilter=MouseFilterEnum.Ignore,HorizontalAlignment=HorizontalAlignment.Right};
            level.AddThemeColorOverride("font_color",new Color("ffe098"));level.AddThemeColorOverride("font_outline_color",new Color("201b0d"));
            level.AddThemeConstantOverride("outline_size",3);portrait.AddChild(level);
            TextureRect Bar() => new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,Visible = false };
            var bars = Bar(); var health = Bar(); var experience = Bar();
            AddChild(portrait); AddChild(bars); AddChild(health); AddChild(experience);
            _slots[slot] = (portrait,frame,bars,health,experience,level);
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
        // The summon's second bar is its remaining life: the health texture tinted turquoise (capture 14).
        _slots[SummonSlot].Experience.Texture = art.Art("console_g_healthbarsmall");
        _slots[SummonSlot].Experience.Modulate = new Color(HealthTints.Lifetime);
    }

    /// <summary>Lays out the slots for the console scale (1 = the original 1024x768 screen): the heroes in order (a fallen one
    /// darkened, its bars empty), and above them the summon with its remaining life (0-1).</summary>
    public void Refresh(IReadOnlyList<SimUnit> heroes,Func<string,Texture2D?> portrait,float scale,SimUnit? summon = null,float life = 0)
    {
        _scale = scale; var viewport = GetViewportRect().Size;
        float X(float fraction) => viewport.X-(1-fraction)*1024*scale;
        for (int index = 0; index <= Slots; index++)
        {
            var slot = _slots[index]; var hero = index == SummonSlot ? summon : index < heroes.Count ? heroes[index] : null;
            bool fallen = hero is { Health: <= 0 };
            _ids[index] = hero == null || fallen ? -1 : hero.Id;
            slot.Portrait.Visible = slot.Bars.Visible = hero != null;
            slot.Health.Visible = slot.Experience.Visible = hero != null && !fallen;
            if (hero == null) continue;
            float top = (index == SummonSlot ? SpecialTop : Top+index*Step)*768*scale;
            slot.Portrait.Position = new(X(Left),top); slot.Portrait.Size = new Vector2(Width*1024,PortraitHeight*768)*scale;
            slot.Frame.Size = slot.Portrait.Size;
            slot.Portrait.Icon = portrait(CommandPanel.PortraitName(hero.Definition.Name));
            slot.Portrait.SelfModulate = fallen ? new Color(.35f,.35f,.35f) : Colors.White;
            slot.Bars.Position = new(X(Left),top+BarTop*768*scale); slot.Bars.Size = new Vector2(Width*1024,BarsHeight*768)*scale;
            float health = Math.Clamp(hero.HealthMilli/(float)(hero.MaxHealth*1000L),0,1);
            slot.Health.Modulate = new Color(HealthTints.Hex(HealthTints.Of(hero.HealthMilli,hero.MaxHealth*1000L,poisoned: hero.Poisons.Count > 0)));
            slot.Health.Position = new(X(FillLeft),top+HealthFill*768*scale); slot.Health.Size = new Vector2(FillWidth*1024*health,FillHeight*768)*scale;
            slot.Level.Size=new(19*scale,20*scale);slot.Level.AddThemeFontSizeOverride("font_size",Math.Max(10,(int)(14*scale)));
            slot.Level.Position=new(Width*1024*scale-23*scale,PortraitHeight*768*scale-22*scale);
            if (index == SummonSlot)
            {
                slot.Experience.Position = new(X(FillLeft),top+ExperienceFill*768*scale);
                slot.Experience.Size = new Vector2(FillWidth*1024*Math.Clamp(life,0,1),FillHeight*768)*scale;
                slot.Level.Text = "";
                slot.Portrait.TooltipText = HudTip.Make(GameText.T("hero.health_line",hero.Definition.DisplayName,hero.Health,hero.MaxHealth),null,
                    GameText.T("hero.remaining_life",GameText.Number((decimal)Math.Clamp(life,0,1)*100,"0")+"%"));
                continue;
            }
            var xp=HeroExperienceText.From(hero);
            slot.Experience.Position = new(X(FillLeft),top+ExperienceFill*768*scale); slot.Experience.Size = new Vector2(FillWidth*1024*xp.Progress,FillHeight*768)*scale;
            slot.Level.Text=hero.HeroLevel.ToString();
            slot.Portrait.TooltipText = fallen ? GameText.T("hero.fallen",hero.Definition.DisplayName)+"\n"+xp.Tooltip
                : GameText.T("hero.health_line",hero.Definition.DisplayName,hero.Health,hero.MaxHealth)+"\n"+xp.Tooltip+(hero.RingActive ? "\n"+GameText.T("hero.ring") : "");
        }
    }

    public Button Portrait(int slot) => _slots[slot].Portrait;
    public int HeroIn(int slot) => _ids[slot];
    public float HealthWidth(int slot) => _slots[slot].Health.Size.X/_scale;
    public float ExperienceFraction(int slot) => _slots[slot].Experience.Size.X/(_scale*FillWidth*1024);
    public string DisplayedLevel(int slot) => _slots[slot].Level.Text;
    public bool Contains(Vector2 point) => _slots.Any(s => s.Portrait.Visible && s.Portrait.GetGlobalRect().HasPoint(point));
    public bool Fallen(int slot) => _slots[slot].Portrait.Visible && _ids[slot] < 0;
    public float LifeFraction => _slots[SummonSlot].Experience.Visible ? _slots[SummonSlot].Experience.Size.X/(_scale*FillWidth*1024) : 0;
}
