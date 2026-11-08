using Godot;
using Reforged.Simulation;
using Reforged.Presentation;

/// <summary>The Sandbox's training dummies (build-59, owner request): one per damage type beside the blue base, so the
/// type cycle can be tried against each. Each has 1000 health, no armor, no regeneration and no attack, never picks a
/// fight or answers one, and has three buttons of its own: +1 armor, armor 0 and full health. A dummy takes its type's
/// model (Swordsman for Cutting, Elven Archer for Piercing, Shieldbreaker for Blunt, Gimli for Hero), drawn half again as
/// big and gilded so it is easy to find, but none of that unit's own pair modifiers. Every hit on a dummy floats its damage
/// overhead, with the type multiplier when it is not 100%.</summary>
public partial class Main
{
    private static readonly (string Type,string Model)[] SandboxDummyModels =
        [("Cutting","UNIT_G_GONDOR_SWORDSMAN"),("Piercing","UNIT_G_ELVEN_ARCHER"),("Blunt","UNIT_G_DWARVEN_SHIELDBREAKER"),("Hero","UNIT_G_HERO_GIMLI")];
    private const int SandboxDummyTeam = 0,SandboxDummyHealth = 1000,SandboxDummyScalePpm = 1_500_000;
    /// <summary>The gilding: the model's colours times this, plus a faint glow of it.</summary>
    private static readonly Color SandboxDummyTint = new(1f,.8f,.22f);
    /// <summary>How long a damage number floats, in seconds, and how far it rises.</summary>
    private const float DummyDamageSeconds = 1.4f,DummyDamageRise = 1.6f;
    private readonly List<int> _sandboxDummies = [];
    private readonly List<(Label3D Label,int Unit,int Tick,float Side)> _dummyDamage = [];

    private List<UnitDefinition> SandboxDummyDefinitions()
    {
        AbilityDefinition[] buttons = [new(-1,0,GameText.T("sandbox.dummy.armor_up"),"Default_Protection_Borderless",false,AbilityKind.ArmorUp),
            new(-2,1,GameText.T("sandbox.dummy.armor_reset"),"BattleGear_Shield_Break_Borderless",false,AbilityKind.ArmorReset),
            new(-3,2,GameText.T("sandbox.dummy.restore"),"BattleGear_Healing_Herbs_Borderless",false,AbilityKind.Restore)];
        var definitions = new List<UnitDefinition>();
        foreach (var ((type,model),index) in SandboxDummyModels.Select((d,i) => (d,i)))
        {
            var source = _rules.Units[model]; string name = "SANDBOX_DUMMY_"+type.ToUpperInvariant();
            // The unit pair modifiers go by unit id: a dummy's own id keeps only the damage-type cycle. The body stays the
            // model unit's (navigation has a clearance profile for each source body); only the drawing grows.
            var dummy = source with { Id = -10-index,Name = name,DisplayName = GameText.T("sandbox.dummy."+type.ToLowerInvariant()),MaxHealth = SandboxDummyHealth,StartingHealthPercent = 100,
                ArmorMilli = 0,DpsMilli = 0,MeleeDpsMilli = 0,RegenerationPpm = 0,SiegePpm = 1_000_000,Hero = false,Experience = null,Abilities = buttons,Passive = true,
                ModelScalePpm = (int)Math.Min(2_000_000L,(long)source.ModelScalePpm*SandboxDummyScalePpm/1_000_000),
                SelectionRadiusMm = Math.Min(6000,source.SelectionRadiusMm*SandboxDummyScalePpm/1_000_000) };
            dummy.Validate();
            _rules.Units[name] = dummy; _unitModels[name] = _unitModels[model];
            CommandPanel.ExtraPortraits[dummy.Name] = CommandPanel.PortraitName(source.Name);
            definitions.Add(dummy);
        }
        return definitions;
    }

    /// <summary>Gilds a dummy's model: per-instance copies of its materials, so the units sharing the model keep theirs.</summary>
    private static void TintSandboxDummy(Node3D visual)
    {
        foreach (var mesh in Descendants(visual).OfType<MeshInstance3D>().Where(m => m.Mesh != null))
            for (int surface = 0; surface < mesh.Mesh!.GetSurfaceCount(); surface++)
                if (Reforged.Rendering.ClassicLighting.Source(mesh.GetActiveMaterial(surface)) is BaseMaterial3D material)
                {
                    var gilded = (BaseMaterial3D)material.Duplicate();
                    gilded.AlbedoColor *= SandboxDummyTint;
                    gilded.EmissionEnabled = true; gilded.Emission = SandboxDummyTint*.35f;
                    mesh.SetSurfaceOverrideMaterial(surface,gilded);
                }
    }

    /// <summary>A floating number for every hit on a dummy in the last step: the damage dealt after armor, and the type
    /// multiplier (the only one a dummy takes) when it is not 100%: orange for 125%, grey-blue for 75%.</summary>
    private void PresentDummyDamage()
    {
        foreach (var hit in _world!.CombatEvents.Where(e => e.Kind == CombatEventKind.Hit && e.Target >= 0 && e.DamageMilli > 0))
        {
            var dummy = _world.Units[hit.Target];
            if (!dummy.Definition.Passive) continue;
            int scale = hit.Unit >= 0 ? _rules.DamageScale(_world.Units[hit.Unit].Definition,dummy.Definition) : 1_000_000;
            var label = new Label3D { Text = "-"+Reforged.Presentation.UnitHudStats.Whole(hit.DamageMilli/1000m)+(scale != 1_000_000 ? $" ({scale/10000}%)" : ""),FontSize = 40,PixelSize = .018f,
                Modulate = scale > 1_000_000 ? new Color("ff9a3c") : scale < 1_000_000 ? new Color("a9c4dd") : new Color("fff4e0"),
                OutlineModulate = new Color("2a1408"),OutlineSize = 9,Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest = true };
            AddChild(label);
            // Numbers landing together spread sideways so a volley stays readable.
            int together = _dummyDamage.Count(d => d.Unit == hit.Target && hit.Tick-d.Tick < 6);
            _dummyDamage.Add((label,hit.Target,hit.Tick,(together%3-1)*.45f));
        }
    }

    /// <summary>Each frame: damage numbers rise over their dummy and fade; they freeze while paused.</summary>
    private void AnimateDummyDamage()
    {
        if (_world == null || _dummyDamage.Count == 0) return;
        double fraction = _paused ? 1 : Engine.GetPhysicsInterpolationFraction();
        double now = (_world.Tick-1+fraction)/SimWorld.TicksPerSecond;
        var right = _camera.GlobalTransform.Basis.X;
        foreach (var entry in _dummyDamage.ToArray())
        {
            float age = (float)(now-entry.Tick/(double)SimWorld.TicksPerSecond);
            var dummy = _world.Units[entry.Unit];
            if (age > DummyDamageSeconds) { entry.Label.QueueFree(); _dummyDamage.Remove(entry); continue; }
            float top = dummy.Definition.BodyHeightMm/1000f*dummy.Definition.ModelScalePpm/1_000_000f;
            entry.Label.Position = UnitPosition(dummy)+Vector3.Up*(top+.5f+DummyDamageRise*Math.Max(0,age)/DummyDamageSeconds)+right*entry.Side;
            float alpha = Math.Clamp(1.6f-1.6f*age/DummyDamageSeconds,0,1);
            entry.Label.Modulate = entry.Label.Modulate with { A = alpha }; entry.Label.OutlineModulate = entry.Label.OutlineModulate with { A = alpha };
        }
    }

    /// <summary>Damage numbers on show, for the Sandbox smoke.</summary>
    private IEnumerable<string> DummyDamageTexts => _dummyDamage.Select(d => d.Label.Text);
}
