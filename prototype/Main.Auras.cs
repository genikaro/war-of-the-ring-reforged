using Reforged.Rendering;
using Reforged.Simulation;

public partial class Main
{
    private readonly Dictionary<(int Unit,int Effect,bool Source),AuraEffect> _auraEffects = [];
    private readonly HashSet<(int Unit,int Effect,bool Source)> _shownAuras = [];

    private bool SpeedAuraBearer(SimUnit unit) => _elvenVisuals.ContainsKey(AuraEffect.Heroic)
        && unit.Definition.Name == "UNIT_G_HERO_LEGOLAS" && unit.ElvenSpeed?.Source == unit.Id;

    private void PresentAuras()
    {
        if (!_elvenVisuals.ContainsKey(AuraEffect.Heroic)) return;
        _shownAuras.Clear();
        foreach (int id in _presented)
        {
            var unit = _world!.Units[id]; var view = _views[id];
            if (!unit.Present || !view.Drawn || view.Translucent) continue;
            bool heroic = false,siege = false;
            if (unit.Definition.Abilities is { Length: > 0 })
                foreach (var ability in unit.EnabledAbilities)
                    switch (ability.Kind)
                    {
                        case AbilityKind.Aura: heroic = true; break;
                        case AbilityKind.SiegeAura: siege = true; break;
                        case AbilityKind.TerrorAura: Draw(AuraEffect.TerrorSource,true); break;
                    }
            if (heroic || _world.HeroicAuraOver(unit)) Draw(AuraEffect.Heroic,heroic);
            if (siege || unit.SiegeAuraPpm > 1_000_000) Draw(AuraEffect.Siege,siege);
            if (unit.TerrorArmorMilli > 0) Draw(AuraEffect.TerrorTarget,false);
            if (_world.ObeliskOver(unit) != null) Draw(AuraEffect.Obelisk,false);
            if (unit.ElvenSpeed?.Until > _world.Tick && SpeedAuraBearer(unit)) Draw(328,true);

            void Draw(int effect,bool source)
            {
                if (!_elvenVisuals.TryGetValue(effect,out var data)) return;
                var key = (id,effect,source); _shownAuras.Add(key);
                if (!_auraEffects.TryGetValue(key,out var visual))
                {
                    visual = new AuraEffect(effect,source,data.Layers);
                    AddChild(visual.Root); _auraEffects.Add(key,visual);
                }
                visual.Draw(_healingTime+id*.137,view.Root.GlobalPosition,unit.Definition.BodyHeightMm/1000f);
            }
        }
        if (_auraEffects.Count == _shownAuras.Count) return;
        foreach (var key in _auraEffects.Keys.Where(key => !_shownAuras.Contains(key)).ToArray())
        {
            _auraEffects[key].Root.QueueFree(); _auraEffects.Remove(key);
        }
    }
}
