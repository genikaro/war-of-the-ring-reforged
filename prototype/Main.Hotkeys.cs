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
        if (_commands.GlobalAbilitiesOpen)
        {
            if (slot == CommandPanel.BackSlot) _commands.CloseGlobalAbilities();
            else if (slot < 5) ChooseGlobalPower(slot);
            return true;
        }
        if (_commands.PlacePowerButton.Visible)
        {
            if (slot == 0) ChoosePlacePower();
            return true;
        }
        if (_buildMenu)
        {
            if (slot == CommandPanel.BackSlot) OpenBuildMenu(false);
            else if (slot < PageChoices().Length)
            {
                string building = PageChoices()[slot].Building;
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
        if (slot == 1 && _commands.AdvancedBuildButton.Visible) { OpenBuildMenu(true,advanced: true); return true; }
        if (slot < 4 && _commands.AbilityButton(slot).Visible) { ChooseAbility(slot); return true; }
        return false;
    }

    private void Notify(string text)
    {
        _status = text; _commands.ShowNotice(text);
        if (AudioSelection.FirstOrDefault() is { } speaker) _audio.Speak(speaker.Definition.Name,9);
    }

    /// <summary>A worker of the player's that gave up a foundation says why.</summary>
    private void PresentFoundationFailures()
    {
        if (_world!.FoundationFailures.FirstOrDefault(f => f.Team == PlayerTeam) is { Reason: { } reason }) Notify("Construction abandoned: "+reason);
    }
}
