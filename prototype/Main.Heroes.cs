using Godot;
using Reforged.Input;
using Reforged.Simulation;

/// <summary>The right-edge hero portraits and the One Ring's translucent presentation.</summary>
public partial class Main
{
    private HeroBar _heroBar = null!;

    private List<SimUnit> FriendlyHeroes() => [.. _world!.Units.Where(u => u.Team == 0 && u.Present && u.Definition.Hero).OrderBy(u => u.Id).Take(HeroBar.Slots)];

    /// <summary>Original portrait behavior: a click selects the hero, a double click also centers the camera on him.</summary>
    private void ChooseHero(int id,bool center,bool append)
    {
        if (_world == null || !LivingFriendly(id)) return;
        _selectedStructure = null; ClearConstructionTargeting(); _orders.Cancel();
        UnitSelection.Click(_selected,_world.Units,_world.Units[id],append,false);
        if (center) { _focus = UnitPosition(_world.Units[id]); UpdateCamera(); }
    }

    private void PresentRing(SimUnit unit,UnitView view)
    {
        bool faded = unit.RingActive && unit.Present;
        if (faded == view.Translucent) return;
        view.Translucent = faded;
        float alpha = (unit.Definition.Abilities?.FirstOrDefault(a => a.Kind == AbilityKind.Ring)?.AlphaPpm ?? 1_000_000)/1_000_000f;
        // Imported materials are shared between instances; the faded copies are per-unit surface overrides.
        view.Ghost ??= [.. Descendants(view.Root).OfType<MeshInstance3D>().Where(m => m != view.Ring && m.Mesh != null)
            .SelectMany(m => Enumerable.Range(0,m.Mesh!.GetSurfaceCount()).Select(surface => (m,surface,Ghost(m.GetActiveMaterial(surface),alpha))))];
        foreach (var (mesh,surface,material) in view.Ghost) mesh.SetSurfaceOverrideMaterial(surface,faded ? material : null);
    }

    private static Material? Ghost(Material? source,float alpha)
    {
        if (source is not BaseMaterial3D material) return source;
        var copy = (BaseMaterial3D)material.Duplicate();
        copy.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; copy.AlbedoColor = copy.AlbedoColor with { A = copy.AlbedoColor.A*alpha };
        return copy;
    }
}
