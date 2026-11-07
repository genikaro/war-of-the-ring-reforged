using Godot;
using Reforged.Simulation;
using Reforged.Presentation;

public partial class CommandPanel
{
    // GameConsole.lsr: matching health/XP frames, level above XP, statuses below.
    private const float SelectedBarWidth = 236;
    private Label _singleLevel = null!, _singleExperienceText = null!;
    private TextureRect _singleExperienceFrame = null!;
    private Control _singleExperienceClip = null!;
    private TextureRect _singleExperience = null!;
    private ColorRect _singleLifetime = null!;

    private void CreateHeroExperience()
    {
        _singleLevel=Text("",new(3,83),new(SelectedBarWidth,16),12);
        _singleLevel.MouseFilter=MouseFilterEnum.Stop; _single.AddChild(_singleLevel);
        _singleExperienceFrame=new TextureRect {Position=new(1,100),Size=new(SelectedBarWidth+6,18),
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=MouseFilterEnum.Stop};
        _single.AddChild(_singleExperienceFrame);
        _singleExperienceClip=new Control {Position=new(3,3),Size=new(0,12),ClipContents=true,MouseFilter=MouseFilterEnum.Ignore};
        _singleExperienceFrame.AddChild(_singleExperienceClip);
        // Reveal the original gradient without shrinking its texture as XP changes.
        _singleExperience=new TextureRect {Size=new(SelectedBarWidth,12),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.Scale,MouseFilter=MouseFilterEnum.Ignore};
        _singleExperienceClip.AddChild(_singleExperience);
        // Build-172: a summon's remaining life fills the same frame in turquoise (the Balrog's "Lifetime" meter, capture 14).
        _singleLifetime=new ColorRect {Size=new(SelectedBarWidth,12),Color=new Color(HealthTints.Lifetime),MouseFilter=MouseFilterEnum.Ignore,Visible=false};
        _singleExperienceClip.AddChild(_singleLifetime);
        _singleExperienceText=Counter("",new(3,3),new(SelectedBarWidth,12),11,HorizontalAlignment.Center);
        _singleExperienceFrame.AddChild(_singleExperienceText);
    }

    private void RefreshHeroExperience(SimUnit unit)
    {
        bool summoned = _world != null && unit.SummonedUntil > _world.Tick;
        _singleLevel.Visible=_singleExperienceFrame.Visible=unit.Definition.Hero || summoned;
        _singleLifetime.Visible=summoned; _singleExperience.Visible=!summoned;
        if (summoned)
        {
            float life=_world!.SummonLifeLeft(unit); int seconds=(unit.SummonedUntil-_world.Tick+SimWorld.TicksPerSecond-1)/SimWorld.TicksPerSecond;
            _singleLevel.Text="Lifetime"; _singleLevel.TooltipText=_singleExperienceFrame.TooltipText=$"Remaining life: {seconds} s";
            _singleExperienceText.Text=$"{seconds} s"; _singleExperienceClip.Size=new(SelectedBarWidth*life,12);
            return;
        }
        if (!unit.Definition.Hero) return;
        var xp=HeroExperienceText.From(unit);
        _singleLevel.Text=xp.Level; _singleLevel.TooltipText=_singleExperienceFrame.TooltipText=xp.Tooltip;
        _singleExperienceText.Text=xp.Counter;
        _singleExperienceClip.Size=new(SelectedBarWidth*xp.Progress,12);
    }

    public string DisplayedHeroLevel => _single.Visible && _singleLevel.Visible ? _singleLevel.Text : "";
    public float DisplayedHeroExperience => _single.Visible && _singleExperienceFrame.Visible ? _singleExperienceClip.Size.X/SelectedBarWidth : 0;
    public string DisplayedHeroExperienceText => _single.Visible && _singleExperienceFrame.Visible ? _singleExperienceText.Text : "";
    public bool HeroExperienceCounterCentered => Math.Abs(_singleExperienceText.GetRect().GetCenter().Y-9)<.01f;
    public string HeroExperienceTooltip => _singleExperienceFrame.TooltipText;
    public float DisplayedLifetime => _single.Visible && _singleLifetime.IsVisibleInTree() ? _singleExperienceClip.Size.X/SelectedBarWidth : 0;
}

public partial class Main
{
    private void PresentHeroLevels()
    {
        foreach (var e in _world!.HeroLevelEvents)
        {
            var unit=_world.Units[e.Unit];
            if (!unit.Present || !_world.CanSeeUnit(PlayerTeam,unit)) continue;
            var position=UnitPosition(unit);
            ElvenBurst(335,unit.Id,position,e.Tick); PlayAbilitySound([412],unit.Id,position);
            var label=new Label3D {Text=$"Level {e.Level}",FontSize=40,PixelSize=.018f,Modulate=new Color("ffe098"),
                OutlineModulate=new Color("352408"),OutlineSize=8,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest=true,Position=position+Vector3.Up*(unit.Definition.BodyHeightMm/1000f+.6f)};
            AddChild(label);_healLabels.Add((label,e.Tick+40));
        }
    }
}
