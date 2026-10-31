using Godot;

/// <summary>The end of a skirmish: the result over a dimmed battlefield, which pauses the game. Titles are the
/// original result lines in English (Menu text 4602 "Вы победили!", 4702 "Вы проиграли!"), and the two choices
/// are the original Continue (4803) and Restart (4806): Continue resumes play on the decided match, Restart
/// reloads the skirmish. A centred panel instead of the original statistics screen is a modern convenience.</summary>
public partial class Main
{
    private Control _outcome = null!;
    private Label _outcomeTitle = null!,_outcomeDetail = null!;
    private Button _outcomeContinue = null!,_outcomeRestart = null!,_outcomeSetup = null!;
    private bool _outcomeShown;

    private void CreateOutcomePanel()
    {
        _outcome = new Control { Visible = false,MouseFilter = Control.MouseFilterEnum.Stop };
        _hud.AddChild(_outcome); _outcome.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0,0,0,.45f),MouseFilter = Control.MouseFilterEnum.Ignore };
        _outcome.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _outcome.AddChild(centre); centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new(420,0) };
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat {
            BgColor = new Color(.035f,.055f,.07f,.95f),BorderColor = new Color("d8b574"),
            BorderWidthLeft = 2,BorderWidthRight = 2,BorderWidthTop = 2,BorderWidthBottom = 2,
            ContentMarginLeft = 32,ContentMarginRight = 32,ContentMarginTop = 24,ContentMarginBottom = 24,
            CornerRadiusTopLeft = 8,CornerRadiusBottomRight = 8 });
        centre.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation",16); panel.AddChild(box);
        _outcomeTitle = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _outcomeTitle.AddThemeFontSizeOverride("font_size",44); box.AddChild(_outcomeTitle);
        _outcomeDetail = new Label { HorizontalAlignment = HorizontalAlignment.Center }; box.AddChild(_outcomeDetail);
        var choices = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        choices.AddThemeConstantOverride("separation",16); box.AddChild(choices);
        _outcomeContinue = DebugButton("Continue",ContinueAfterOutcome); _outcomeContinue.CustomMinimumSize = new(140,40); choices.AddChild(_outcomeContinue);
        _outcomeRestart = DebugButton("Restart",() => _ = ReloadWithAudio()); _outcomeRestart.CustomMinimumSize = new(140,40); choices.AddChild(_outcomeRestart);
        // Back to the skirmish setup screen, when the match was chosen there.
        _outcomeSetup = DebugButton("Setup",ReturnToSetup); _outcomeSetup.CustomMinimumSize = new(140,40); choices.AddChild(_outcomeSetup);
    }

    private void PresentOutcome()
    {
        // The Sandbox map is for testing: razing a side ends nothing.
        if (_outcomeShown || _sandboxMap || _world is not { MatchOver: true } world) return;
        _outcomeShown = true;
        bool won = world.Winner == PlayerTeam;
        if (_observing)
        {
            // An observer has no side: name the winner in its colour.
            string bot = world.AiPlayers.GetValueOrDefault(world.Winner)?.Personality.Id ?? "";
            _outcomeTitle.Text = world.Winner < 0 ? "No one won" : $"Player {world.Winner+1} wins!";
            _outcomeTitle.Modulate = world.Winner < 0 ? new Color("d8b574") : TeamColor(world.Winner).Lightened(.3f);
            _outcomeDetail.Text = world.Winner < 0 ? "The last buildings fell together." : $"{bot} razed every enemy building.";
            _status.Text = world.Winner < 0 ? "Match over: no buildings left" : $"Match over: Player {world.Winner+1} ({bot}) wins";
        }
        else
        {
            _outcomeTitle.Text = won ? "You won!" : "You lost!";
            _outcomeTitle.Modulate = won ? new Color("d8b574") : new Color("d06a5a");
            _outcomeDetail.Text = won ? "Every enemy building lies in ruins." : "All of your buildings have been destroyed.";
            _status.Text = won ? "Victory: all enemy buildings destroyed" : "Defeat: all your buildings destroyed";
        }
        _outcome.Visible = true; SetPaused(true);
    }

    private void ContinueAfterOutcome() { _outcome.Visible = false; SetPaused(false); }
}
