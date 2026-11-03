using Godot;
using Reforged.Input;
using Reforged.Simulation;

/// <summary>The right-edge hero portraits and translucent Ring/Camouflage presentation.</summary>
public partial class Main
{
    private HeroBar _heroBar = null!;

    private int? CaptureSiteAt(Vector2 point)
        => StructureAt(point) is { } hit ? _world!.ConstructionSites.Values.FirstOrDefault(s => s.Obstacle == hit.Id)?.Id : null;

    private static string AbilityTargetPrompt(AbilityDefinition ability) => ability.BuildingTarget ? (ability.Kind == AbilityKind.Torch ? ": choose an enemy building" : ": choose an enemy tower")
        : ability.GroundTarget ? ": choose an area" : ability.EnemyTarget ? ": choose a visible enemy unit"
        : ability.Kind is AbilityKind.Protection or AbilityKind.FlameShield ? ": choose a friendly unit"
        : ability.Kind == AbilityKind.ShadowBind ? ": choose one of your Wraiths"
        : ability.Kind == AbilityKind.BloodMark ? ": choose a friendly unit or one of your War Posts" : ": choose an injured friendly unit";

    private void PresentHeroSkillEvents()
    {
        PresentHeroLevels();
        foreach (var e in _world!.AbilityEvents)
        {
            if (e.Unit<0) continue; // Tower poison has a building source rather than a unit caster.
            var source = _world.Units[e.Unit]; var ability = source.Ability(e.Slot);
            int effect = e.Kind switch { AbilityEventKind.Sunder => 145,AbilityEventKind.BlindingLight => 142,
                AbilityEventKind.NegateAbility => 186,AbilityEventKind.FlameShield => 177,
                AbilityEventKind.RainOfFire => 182,AbilityEventKind.FireImpact => 388,AbilityEventKind.Trueshot when e.HealthMilli > 0 => 127,
                AbilityEventKind.InvisibleForce => 197,AbilityEventKind.Converted => 196,
                AbilityEventKind.ShadowBind => 355,AbilityEventKind.ShadowWalkDepart => 364,AbilityEventKind.ShadowWalkArrive => 365,
                AbilityEventKind.BloodMark => 169,AbilityEventKind.BloodField => e.HealthMilli == 1 ? 184 : 183,AbilityEventKind.TrapDropped => 403,AbilityEventKind.TrapSprung => 163,AbilityEventKind.SatchelBlast => 130,AbilityEventKind.Tracked => 158,_ => -1 };
            var position = e.Kind is AbilityEventKind.NegateAbility or AbilityEventKind.RainOfFire or AbilityEventKind.FireImpact or AbilityEventKind.Capture
                or AbilityEventKind.InvisibleForce or AbilityEventKind.Pestilence or AbilityEventKind.ShadowBind or AbilityEventKind.ShadowWalkDepart or AbilityEventKind.ShadowWalkArrive
                or AbilityEventKind.BloodField or AbilityEventKind.TrapDropped or AbilityEventKind.TrapSprung or AbilityEventKind.SatchelBlast or AbilityEventKind.SatchelDropped
                ? new Vector3(-e.X/1000f,e.Y/1000f,e.Z/1000f) : e.Kind is AbilityEventKind.Trueshot or AbilityEventKind.Converted or AbilityEventKind.PrisonOfIce or AbilityEventKind.BloodMark or AbilityEventKind.Tracked && e.Target >= 0
                ? UnitPosition(_world.Units[e.Target]) : UnitPosition(source);
            if (effect >= 0) ElvenBurst(effect,-1,position,e.Tick);
            if (e.Kind == AbilityEventKind.InvisibleForce) ElvenBurst(202,-1,position,e.Tick);
            if (e.Kind == AbilityEventKind.ShadowWalkArrive) PlayAbilitySound(ability?.EndSoundEvents,e.Unit,position);
            if (e.Kind is AbilityEventKind.Sunder or AbilityEventKind.BlindingLight or AbilityEventKind.NegateAbility or AbilityEventKind.FlameShield
                or AbilityEventKind.ElvenSpeed or AbilityEventKind.FireImpact or AbilityEventKind.Capture
                or AbilityEventKind.PrisonOfIce or AbilityEventKind.InvisibleForce or AbilityEventKind.Pestilence or AbilityEventKind.Converted
                or AbilityEventKind.ShadowBind or AbilityEventKind.ShadowWalkDepart or AbilityEventKind.BloodMark
                || e.Kind == AbilityEventKind.Trueshot && e.HealthMilli == 0)
                PlayAbilitySound(ability?.SoundEvents,e.Unit,position);
        }
    }

    private List<SimUnit> FriendlyHeroes() => [.. _world!.Units.Where(u => u.Team == PlayerTeam && u.Present && u.Definition.Hero).OrderBy(u => u.Id).Take(HeroBar.Slots)];

    /// <summary>Original portrait behavior: a click selects the hero, a double click also centers the camera on him.</summary>
    private void ChooseHero(int id,bool center,bool append)
    {
        if (_world == null || !LivingFriendly(id)) return;
        _selectedStructure = null; ClearConstructionTargeting(); _orders.Cancel();
        UnitSelection.Click(_selected,_world.Units,_world.Units[id],append,false);
        if (center) { _focus = UnitPosition(_world.Units[id]); UpdateCamera(); }
    }

    private void PresentInvisibility(SimUnit unit,UnitView view)
    {
        bool faded = (unit.RingActive || unit.Camouflaged || unit.ElvenCloaked || unit.Sneaking) && unit.Present;
        if (faded == view.Translucent) return;
        view.Translucent = faded;
        float alpha = (unit.Definition.Abilities?.FirstOrDefault(a => a.Kind == (unit.ElvenCloaked ? AbilityKind.ElvenCloak : unit.Camouflaged ? AbilityKind.Camouflage : unit.Sneaking ? AbilityKind.Sneak : AbilityKind.Ring))?.AlphaPpm ?? 1_000_000)/1_000_000f;
        // Imported materials are shared between instances; the faded copies are per-unit surface overrides.
        view.Ghost ??= [.. Descendants(view.Root).OfType<MeshInstance3D>().Where(m => m != view.Ring && m != view.HeroGlow?.Ground && m.Mesh != null)
            .SelectMany(m => Enumerable.Range(0,m.Mesh!.GetSurfaceCount()).Select(surface => (m,surface,Ghost(m.GetActiveMaterial(surface),alpha))))];
        foreach (var (mesh,surface,material) in view.Ghost) mesh.SetSurfaceOverrideMaterial(surface,faded ? material : null);
    }

    private static Material? Ghost(Material? source,float alpha)
    {
        // Build-152: faded copies start from the imported material, not its classic-lit copy.
        if (Reforged.Rendering.ClassicLighting.Source(source) is not BaseMaterial3D material) return source;
        var copy = (BaseMaterial3D)material.Duplicate();
        copy.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; copy.AlbedoColor = copy.AlbedoColor with { A = copy.AlbedoColor.A*alpha };
        return copy;
    }
}
