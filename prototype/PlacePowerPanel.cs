using Godot;
using Reforged.Simulation;

public partial class CommandPanel
{
    public event Action? PlacePowerChosen;
    private Button _placePowerButton = null!;
    public Button PlacePowerButton => _placePowerButton;

    private void CreatePlacePowerControls()
    {
        _placePowerButton = ActionButton(0);
        _placePowerButton.Pressed += () => PlacePowerChosen?.Invoke();
    }

    public void RefreshPlacePower(SimWorld world,PlaceOfPower? place,bool observing)
    {
        _placePowerButton.Visible = place != null && world.PlacePowers.ContainsKey(place.Type) && !GlobalAbilitiesOpen;
        if (!_placePowerButton.Visible) return;
        var power = world.PlacePowers[place!.Type];
        _placePowerButton.Icon = _art.GetValueOrDefault(power.Icon);
        _placePowerButton.Disabled = observing || !world.CanUsePlacePower(PlayerTeam,place.Id,out _);
        world.CanUsePlacePower(PlayerTeam,place.Id,out string reason);
        _placePowerButton.TooltipText = $"{place.Name} [Q]\n{power.Description}\nRecharge {power.RechargeTicks/20}s\n{reason}";
    }
}
