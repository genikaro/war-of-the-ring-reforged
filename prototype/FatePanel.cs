using Godot;
using Reforged.Simulation;

public partial class CommandPanel
{
    public event Action? GlobalAbilitiesChosen;
    public event Action<int>? GlobalPowerChosen;
    public event Action? GlobalAbilitiesClosed;
    private readonly Button[] _globalButtons = new Button[5];
    private readonly Dictionary<string,(Texture2D[] Frames,int FrameMs)> _fateOrbs = [];
    private (Texture2D[] Frames,int FrameMs) _fateOrb;
    private double _fateAnimationMs = -1;
    public Button GlobalPowerButton(int slot) => _globalButtons[slot];
    private Button _fateButton = null!;
    private string _fateSelection = "";
    public bool GlobalAbilitiesOpen { get; private set; }
    public Button FateButton => _fateButton;
    public bool FateCounterCentered => _fateText.GetRect().GetCenter().DistanceTo(_fateIcon.GetRect().GetCenter()) < .01f;

    private void CreateFateControls()
    {
        // Center on the round detail at (510, 21) in the original 1024 x 212 console atlas.
        _fateButton = new Button { Name = "Fate",Position = new(495,6),Size = new(30,30),
            FocusMode = FocusModeEnum.None,Visible = false,ToggleMode = true };
        AtlasButton(_fateButton); _content.AddChild(_fateButton);
        _fateIcon = new TextureRect { Size = new(30,30),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter = MouseFilterEnum.Ignore };
        _fateButton.AddChild(_fateIcon);
        _fateText = Counter("0",Vector2.Zero,new(30,30),17,HorizontalAlignment.Center);
        _fateText.AddThemeColorOverride("font_outline_color",new Color("14202b")); _fateText.AddThemeConstantOverride("outline_size",4);
        _fateButton.AddChild(_fateText);
        _fateButton.TooltipText = "Fate\nProgress to next point: 0%\nClick for global abilities.";
        _fateButton.Pressed += () => {
            _fateAnimationMs = 0;
            if (GlobalAbilitiesOpen) CloseGlobalAbilities();
            else
            {
                GlobalAbilitiesOpen = true;
                GlobalAbilitiesChosen?.Invoke();
            }
        };

        for (int slot = 0; slot < 5; slot++)
        {
            int chosen = slot;
            var button = ActionButton(slot);
            button.ToggleMode = true;
            button.Pressed += () => GlobalPowerChosen?.Invoke(chosen);
            _globalButtons[slot] = button;
        }
    }

    private Texture2D LoadFateOrb(string name,Texture2D sheet,int count,int frameMs)
    {
        int width = sheet.GetWidth()/count;
        var frames = Enumerable.Range(0,count).Select(i => (Texture2D)new AtlasTexture {
            Atlas = sheet,Region = new(i*width,0,width,sheet.GetHeight()),FilterClip = true }).ToArray();
        _fateOrbs[name] = (frames,frameMs);
        return frames[0];
    }

    private void ApplyFateSkin()
    {
        string key = EvilSkin ? "console_e_powerbutton" : "console_g_powerbutton";
        _fateOrb = _fateOrbs.GetValueOrDefault(key);
        _fateIcon.Texture = _art.GetValueOrDefault(key) ?? _art.GetValueOrDefault("console_g_powerchit");
        _fateAnimationMs = -1;
    }

    private void AnimateFateOrb(double delta)
    {
        if (_fateAnimationMs < 0 || _fateOrb.Frames is not { } frames || _fateOrb.FrameMs <= 0) return;
        _fateAnimationMs += delta*1000;
        int frame = (int)(_fateAnimationMs/_fateOrb.FrameMs);
        if (frame >= frames.Length) { frame = 0; _fateAnimationMs = -1; }
        _fateIcon.Texture = frames[frame];
    }

    public void CloseGlobalAbilities()
    {
        bool wasOpen = GlobalAbilitiesOpen;
        GlobalAbilitiesOpen = false;
        foreach (var button in _globalButtons) button.Visible = false;
        _fateButton.SetPressedNoSignal(false);
        if (wasOpen) GlobalAbilitiesClosed?.Invoke();
    }

    /// <summary>Apply after the selection's ordinary command pages; changing selection restores its commands.</summary>
    public void RefreshFatePage(string selection,int pending,bool mirrorOpen = false)
    {
        if (selection != _fateSelection) CloseGlobalAbilities();
        _fateSelection = selection;
        if (mirrorOpen) GlobalAbilitiesOpen = true;
        _fateButton.SetPressedNoSignal(GlobalAbilitiesOpen && _fateButton.Visible);
        if (!GlobalAbilitiesOpen || !_fateButton.Visible || _world == null) return;
        foreach (var button in _content.GetChildren().OfType<Button>())
            if (button.Position.X >= 788 && button.Position.Y >= 34 && !_globalButtons.Contains(button))
                button.Visible = false;
        for (int slot = 0; slot < _globalButtons.Length; slot++)
        {
            var button = _globalButtons[slot]; var power = _world.GlobalPower(slot,PlayerTeam);
            button.Visible = power != null;
            if (power == null) continue;
            button.Icon = _art.GetValueOrDefault(power.Icon);
            button.Disabled = !_world.CanUseGlobalPower(PlayerTeam,slot,out string reason);
            button.SetPressedNoSignal(pending == slot);
            int remaining = _world.GlobalCooldown(PlayerTeam,power.Id);
            int seconds = SimWorld.TicksPerSecond;
            if (remaining > 0) reason = $"Recharging ({(remaining+seconds-1)/seconds}s)";
            button.TooltipText = $"{power.Name} [{GridKeys[slot]}]\n{power.FateCost} Fate · {power.RechargeTicks/seconds}s recharge\n{power.Description}"
                +(button.Disabled ? "\n"+reason : "\nChoose "+(power.Kind == GlobalPowerKind.HeroicLegacy ? "a friendly hero." : "a point on the battlefield."));
        }
    }
}
