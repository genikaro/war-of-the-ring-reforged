using Godot;

/// <summary>The Sandbox map's player tabs (build-56), in the two console tabs beside the menu button (the original's
/// chat and goals slots, unused here): the pressed tab is the player the mouse and keyboard command. Tab switches too.</summary>
public partial class CommandPanel
{
    public event Action<int>? SandboxPlayerChosen;
    private readonly Button[] _sandboxTabs = new Button[2];

    private void CreateSandboxTabs()
    {
        for (int team = 0; team < _sandboxTabs.Length; team++)
        {
            int chosen = team;
            var tab = new Button { Position = new(85+team*72,5),Size = new(67,17),Text = $"Player {team+1}",FocusMode = FocusModeEnum.None,
                ToggleMode = true,Visible = false,
                TooltipText = $"Command Player {team+1} [Tab]" };
            Skin(tab); tab.AddThemeFontSizeOverride("font_size",11);
            tab.AddThemeStyleboxOverride("normal",Border(new Color(0,0,0,.35f),new Color("6f6040")));
            tab.Pressed += () => { SandboxPlayerChosen?.Invoke(chosen); RefreshSandboxTabs(true); };
            _sandboxTabs[team] = tab; _content.AddChild(tab);
        }
    }

    /// <summary>Shows the tabs on the Sandbox map, each named in its player's colour, the commanded one pressed.</summary>
    public void RefreshSandboxTabs(bool shown)
    {
        for (int team = 0; team < _sandboxTabs.Length; team++)
        {
            var tab = _sandboxTabs[team]; tab.Visible = shown;
            tab.SetPressedNoSignal(team == PlayerTeam);
            var color = team < TeamColors.Length ? TeamColors[team].Lightened(.45f) : new Color("fff3cb");
            foreach (string state in new[] {"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color"}) tab.AddThemeColorOverride(state,color);
        }
    }

    public Button SandboxTab(int team) => _sandboxTabs[team];
}
