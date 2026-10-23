using Godot;
using Reforged.Simulation;

/// <summary>Grid hotkeys for the building page and training buttons, and refusal notices above the console.</summary>
public partial class Main
{
    /// <summary>The key in an action-grid position (CommandPanel.GridKeys) presses the button showing there.
    /// Returns true when the key belongs to the page on show, which then keeps it from the unit commands.</summary>
    private bool UseGridHotkey(int slot)
    {
        if (!_skirmish) return false;
        if (_buildMenu)
        {
            if (slot == CommandPanel.BackSlot) OpenBuildMenu(false);
            else if (slot < _constructionChoices.Length)
            {
                string building = _constructionChoices[slot].Building;
                if (!_commands.ConstructionButton(building).Disabled) ChooseConstruction(building);
                else Notify(ResourceBuildingKind(building) != null || _world!.CanStartBuilding(PlayerTeam,building,out string reason) ? "Insufficient resources" : reason);
            }
            return true;
        }
        if (slot == CommandPanel.UpgradeSlot && _commands.UpgradeButton.Visible) { ChooseUpgrade(); return true; }
        if (slot < 8 && _commands.ResearchButton(slot).Visible) { ChooseResearch(slot); return true; }
        if (SelectedProducer() is { } producer)
        {
            if (slot >= producer.Units.Length) return false;
            ChooseTraining(slot); return true;
        }
        if (slot == CommandPanel.BackSlot && _commands.CancelConstructionButton.Visible) { ChooseConstruction(null); return true; }
        if (slot == 0 && _commands.BuildButton.Visible) { OpenBuildMenu(true); return true; }
        return false;
    }

    private void Notify(string text) { _status.Text = text; _commands.ShowNotice(text); }

    /// <summary>A worker of the player's that gave up a foundation says why.</summary>
    private void PresentFoundationFailures()
    {
        if (_world!.FoundationFailures.FirstOrDefault(f => f.Team == PlayerTeam) is { Reason: { } reason }) Notify("Construction abandoned: "+reason);
    }
}
