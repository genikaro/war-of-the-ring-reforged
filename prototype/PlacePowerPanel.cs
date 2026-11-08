using Godot;
using Reforged.Presentation;
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
        _placePowerButton.TooltipText = HudTip.Make(HudTip.Keyed(place.Name,"Q"),null,PlacePowerText(power),
            GameText.Labeled("tip.cooldown",SecondsValue(power.RechargeTicks)),reason);
    }

    /// <summary>Build-184: what a place's power does, with the simulation's numbers.</summary>
    public static string PlacePowerText(PlacePowerDefinition power) => power.Kind == "Willow"
        ? GameText.T("placepower.willow",power.RadiusMm/1000,power.TimeScalePpm/10000,power.DurationTicks/20,power.RangeMm/1000)
        : GameText.T("placepower.aviary",power.RadiusMm/1000,power.DurationTicks/20);
}
