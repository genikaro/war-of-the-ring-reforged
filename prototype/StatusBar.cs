using Godot;
using Reforged.Presentation;
using Reforged.Simulation;

/// <summary>Build-70: the selected unit's status bar, the source console's row of small effect icons under the health bar
/// (local capture campaign-004: Gimli with two), helpful effects first. Each icon's tooltip names the effect, what it
/// does and where it comes from; a timed one darkens from the top as it runs out. The row replaces build-67's activity
/// line ("Running", "Standing"), which the owner dropped from the panel.</summary>
public partial class CommandPanel
{
    public const int StatusSlots = 8;
    private const float StatusSize = 24,StatusStep = 28;
    private readonly (TextureRect Icon,ColorRect Elapsed)[] _statusIcons = new (TextureRect,ColorRect)[StatusSlots];
    private readonly string[] _statusTitles = new string[StatusSlots];
    private Label _attackBonus = null!,_armorBonus = null!;

    private void CreateStatusBar()
    {
        for (int slot = 0; slot < StatusSlots; slot++)
        {
            var icon = new TextureRect { Position = new(slot*StatusStep,104),Size = new(StatusSize,StatusSize),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,MouseFilter = MouseFilterEnum.Stop,Visible = false };
            var elapsed = new ColorRect { Color = new Color(0,0,0,.55f),Size = new(StatusSize,0),MouseFilter = MouseFilterEnum.Ignore };
            icon.AddChild(elapsed); _single.AddChild(icon); _statusIcons[slot] = (icon,elapsed);
        }
        // The green bonus column beside the attack and armor values.
        (_attackBonus,_armorBonus) = (BonusLabel(0),BonusLabel(1));
    }

    private Label BonusLabel(int row)
    {
        var label = Text("",new(461,33+row*22),new(40,22),13); label.MouseFilter = MouseFilterEnum.Pass;
        _single.AddChild(label); return label;
    }

    private static void ShowBonus(Label label,string bonus,string tip)
    {
        label.Text = bonus; label.TooltipText = bonus == "" ? "" : tip;
        label.AddThemeColorOverride("font_color",new Color(bonus.StartsWith('-') ? "ff6a5a" : "6be35a"));
    }

    private void RefreshStatusBar(SimUnit unit,Func<int,string> name)
    {
        var statuses = _world == null ? [] : _world.StatusesOf(unit).OrderBy(s => s.Harmful).Take(StatusSlots).ToArray();
        for (int slot = 0; slot < StatusSlots; slot++)
        {
            var (icon,elapsed) = _statusIcons[slot];
            icon.Position=new(slot*StatusStep,unit.Definition.Hero ? 120 : 86);
            icon.Visible = slot < statuses.Length;
            if (!icon.Visible) { _statusTitles[slot] = ""; continue; }
            var status = statuses[slot]; var text = UnitStatusText.From(_world!,unit,status,name);
            icon.Texture = _art.GetValueOrDefault(text.Icon); icon.TooltipText = text.Tooltip; _statusTitles[slot] = text.Title;
            int duration = status.Ability?.DurationTicks ?? 0;
            elapsed.Size = new(StatusSize,status.Until >= 0 && duration > 0 ? StatusSize*Math.Clamp(1-(status.Until-_world!.Tick)/(float)duration,0,1) : 0);
        }
    }

    /// <summary>The status names a group portrait's tooltip lists.</summary>
    private string StatusLine(SimUnit unit,Func<int,string> name) => _world == null ? ""
        : string.Join(" · ",_world.StatusesOf(unit).Select(s => UnitStatusText.From(_world,unit,s,name).Title));

    public string[] DisplayedStatuses => [.. _statusTitles.Where((title,slot) => _single.Visible && _statusIcons[slot].Icon.Visible)];
    public TextureRect StatusIcon(int slot) => _statusIcons[slot].Icon;
    public float StatusElapsed(int slot) => _statusIcons[slot].Elapsed.Size.Y/StatusSize;
    public string DisplayedAttackBonus => _attackBonus.Text;
    public string DisplayedArmorBonus => _armorBonus.Text;
}
