using Godot;
using Reforged.Simulation;

public partial class CommandPanel
{
    public event Action? GlobalAbilitiesChosen;
    public event Action<int>? GlobalPowerChosen;
    public event Action? GlobalAbilitiesClosed;
    private readonly Button[] _globalButtons = new Button[5];
    private readonly Label[] _globalTimes = new Label[5];
    public Button GlobalPowerButton(int slot) => _globalButtons[slot];
    private Button _fateButton = null!,_fateBack = null!;
    private Panel _globalAbilities = null!;
    private string _fateSelection = "";
    public bool GlobalAbilitiesOpen { get; private set; }
    public Button FateButton => _fateButton;
    public Button FateBackButton => _fateBack;
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
            GlobalAbilitiesOpen = !GlobalAbilitiesOpen;
            _fateButton.SetPressedNoSignal(GlobalAbilitiesOpen);
            GlobalAbilitiesChosen?.Invoke();
        };

        _globalAbilities = new Panel { Name = "GlobalAbilities",Position = new(788,34),Size = new(224,166),
            Visible = false,MouseFilter = MouseFilterEnum.Stop };
        _globalAbilities.AddThemeStyleboxOverride("panel",Border(new Color("282418"),new Color("bda56b")));
        _content.AddChild(_globalAbilities);
        var title = Text("Global abilities",new(4,3),new(216,22),15); title.HorizontalAlignment = HorizontalAlignment.Center;
        _globalAbilities.AddChild(title);
        for (int slot = 0; slot < 5; slot++)
        {
            int chosen = slot;
            var button = new Button { Position = new(4+slot%4*55,29+slot/4*55),Size = new(51,51),ExpandIcon = true,
                FocusMode = FocusModeEnum.None,ToggleMode = true };
            Skin(button); AddHotkey(button,GridKeys[slot].ToString());
            button.Pressed += () => GlobalPowerChosen?.Invoke(chosen);
            _globalAbilities.AddChild(button); _globalButtons[slot] = button;
            var timer = Text("",new(0,0),new(51,24),16); timer.HorizontalAlignment = HorizontalAlignment.Center;
            timer.MouseFilter = MouseFilterEnum.Ignore; button.AddChild(timer); _globalTimes[slot] = timer;
        }
        var note = Text("Choose a power,\nthen a target.\nRight-click cancels.",new(62,86),new(102,66),11);
        _globalAbilities.AddChild(note);
        _fateBack = new Button { Position = new(168,112),Size = new(54,52),Text = "Back",FocusMode = FocusModeEnum.None,
            TooltipText = "Back [V or Esc]" };
        Skin(_fateBack); _fateBack.AddThemeFontSizeOverride("font_size",12); AddHotkey(_fateBack,"V");
        _fateBack.Pressed += CloseGlobalAbilities; _globalAbilities.AddChild(_fateBack);
    }

    public void CloseGlobalAbilities()
    {
        bool wasOpen = GlobalAbilitiesOpen;
        GlobalAbilitiesOpen = false; _globalAbilities.Visible = false; _fateButton.SetPressedNoSignal(false);
        if (wasOpen) GlobalAbilitiesClosed?.Invoke();
    }

    /// <summary>Apply after the selection's ordinary command pages; changing selection restores its commands.</summary>
    public void RefreshFatePage(string selection,bool mirrorOpen = false)
    {
        if (selection != _fateSelection) CloseGlobalAbilities();
        _fateSelection = selection;
        if (mirrorOpen) GlobalAbilitiesOpen = true;
        _globalAbilities.Visible = GlobalAbilitiesOpen && _fateButton.Visible;
        _fateButton.SetPressedNoSignal(_globalAbilities.Visible);
        if (!_globalAbilities.Visible) return;
        foreach (var button in _content.GetChildren().OfType<Button>().Where(b => b.Position.X >= 788 && b.Position.Y >= 34)) button.Visible = false;
        RefreshGlobalPowers(-1);
    }

    public void RefreshGlobalPowers(int pending)
    {
        if (_world == null) return;
        for (int slot = 0; slot < _globalButtons.Length; slot++)
        {
            var button = _globalButtons[slot]; var power = _world.GlobalPower(slot,PlayerTeam);
            button.Visible = power != null;
            if (power == null) continue;
            button.Icon = _art.GetValueOrDefault(power.Icon);
            button.Disabled = !_world.CanUseGlobalPower(PlayerTeam,slot,out string reason);
            button.SetPressedNoSignal(pending == slot);
            int remaining = _world.GlobalCooldown(PlayerTeam,power.Id);
            bool active = _world.GlobalActive(PlayerTeam,power.Id);
            _globalTimes[slot].Text = active ? "Active" : remaining > 0 ? $"{(remaining+19)/20}s" : "";
            button.TooltipText = $"{power.Name} [{GridKeys[slot]}]\n{power.FateCost} Fate · {power.RechargeTicks/20}s recharge\n{power.Description}"
                +(button.Disabled ? "\n"+reason : "\nChoose "+(power.Kind == GlobalPowerKind.HeroicLegacy ? "a friendly hero." : "a point on the battlefield."));
        }
    }
}
