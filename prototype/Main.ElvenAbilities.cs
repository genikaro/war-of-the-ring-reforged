using Godot;
using Reforged.Input;
using Reforged.Rendering;
using Reforged.Simulation;

public partial class Main
{
    private MeshInstance3D? _abilityAreaPreview;
    private readonly Dictionary<int,(double Duration,HealingEffect.Layer[] Layers)> _elvenVisuals = [];
    private readonly List<(ElvenSpellEffect Effect,int Target,Vector3 Position,double Start,double Duration)> _elvenBursts = [];
    private readonly Dictionary<(int Unit,int Effect),(ElvenSpellEffect Visual,int Start)> _elvenAttachments = [];

    private NavigationHit? AbilityGroundAt(Vector2 point)
    {
        var origin = _camera.ProjectRayOrigin(point); var direction = _camera.ProjectRayNormal(point);
        var hit = _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
        return hit == null ? null : hit with { Node = _world!.Navigation.Nearest(hit.Node) };
    }

    private void ShowAbilityArea(System.Numerics.Vector3 position,int radiusMm,bool valid)
    {
        if (_abilityAreaPreview == null)
        {
            _abilityAreaPreview = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,CullMode = BaseMaterial3D.CullModeEnum.Disabled } };
            AddChild(_abilityAreaPreview);
        }
        float radius = radiusMm/1000f;
        _abilityAreaPreview.Mesh = Reforged.Rendering.ScenePresentation.SelectionRing(local => {
            float distance = local.Length() > .6f ? radius : Math.Max(.1f,radius-.14f);
            var world = new Vector3(-position.X,position.Y,position.Z)+local.Normalized()*distance;
            world.Y = Math.Max(position.Y,_world!.Navigation.GroundHeight((int)(-world.X*1000),(int)(world.Z*1000))/1000f)+.09f;
            return world;
        },_abilityAreaPreview.Mesh as ArrayMesh);
        ((StandardMaterial3D)_abilityAreaPreview.MaterialOverride!).AlbedoColor = valid ? new Color(.35f,.85f,1,.8f) : new Color(1,.25f,.2f,.8f);
        _abilityAreaPreview.Visible = true;
    }

    private void ElvenBurst(int effect,int target,Vector3 position,int tick)
    {
        if (!_elvenVisuals.TryGetValue(effect,out var data)) return;
        var visual = new ElvenSpellEffect(data.Layers,area:effect is 75 or 402 or 145 or 142 or 186 or 182 or 388 or 197 or 202 or 364 or 365 or 183 or 184 or 163 or 403 or 130,
            areaRadius:effect is 182 or 388 or 197 or 202 or 364 or 365 or 163 ? 10 : effect is 183 or 184 ? 12 : effect is 403 or 130 ? 4 : 5);
        AddChild(visual.Root);
        _elvenBursts.Add((visual,target,position,tick/(double)SimWorld.TicksPerSecond,data.Duration));
    }

    private void PresentElvenEvents()
    {
        foreach (var e in _world!.AbilityEvents)
        {
            if (e.Unit<0) continue;
            var source = _world.Units[e.Unit]; var ability = source.Ability(e.Slot);
            if (e.Kind == AbilityEventKind.Protection)
            { ElvenBurst(349,e.Unit,UnitPosition(source),e.Tick); PlayAbilitySound(ability?.SoundEvents,e.Unit,UnitPosition(source)); }
            if (e.Kind == AbilityEventKind.Dispel)
            { ElvenBurst(308,e.Target,UnitPosition(_world.Units[e.Target]),e.Tick); }
            if (e.Kind == AbilityEventKind.SearingLight)
            {
                var position = new Vector3(-e.X/1000f,e.Y/1000f,e.Z/1000f);
                ElvenBurst(75,-1,position,e.Tick); ElvenBurst(402,-1,position,e.Tick);
                ElvenBurst(104,e.Unit,UnitPosition(source),e.Tick); PlayAbilitySound(ability?.SoundEvents,e.Unit,UnitPosition(source));
            }
            if (e.Kind == AbilityEventKind.SearEnd) PlayAbilitySound(ability?.EndSoundEvents,e.Target,UnitPosition(_world.Units[e.Target]));
            if (e.Kind == AbilityEventKind.Start && ability?.Kind == AbilityKind.Dispel)
                PlayAbilitySound(ability.SoundEvents,e.Unit,UnitPosition(source));
        }
    }

    private void PresentElvenEffects()
    {
        if (_world == null) return;
        PresentNatureRoots();
        PresentGlobalAreas();
        double time = _healingTime;
        foreach (var burst in _elvenBursts.ToArray())
        {
            if (time-burst.Start >= burst.Duration || burst.Target >= 0 && !_world.Units[burst.Target].Present)
            { burst.Effect.Free(); _elvenBursts.Remove(burst); continue; }
            bool visible = burst.Target < 0 || _world.CanSeeUnit(PlayerTeam,_world.Units[burst.Target]);
            burst.Effect.Root.Visible = visible;
            burst.Effect.Draw(time-burst.Start,burst.Target < 0 ? burst.Position : _views[burst.Target].Root.Position,2);
        }
        var active = new HashSet<(int,int)>();
        foreach (var unit in _world.Units.Where(u => u.Present))
        {
            foreach (int effect in new[] {unit.ProtectionMilli > 0 && unit.ProtectionUntil > _world.Tick ? 350 : -1,
                         unit.SearingEffects.Any(s => s.Until > _world.Tick) ? 106 : -1,
                         unit.Dazed?.Until > _world.Tick ? unit.Dazed.Ability.Kind == AbilityKind.Sunder ? 253 : unit.Dazed.Ability.Kind == AbilityKind.BlindingLight ? 143 : unit.Dazed.Ability.Kind == AbilityKind.PrisonOfIce ? 356 : 363 : -1,
                         unit.Poisons.Any(p => p.Until > _world.Tick) ? unit.Poisons.Any(p => p.Until > _world.Tick && p.Ability.Kind == AbilityKind.PoisonTrap) ? 164 : 200 : -1,
                         unit.HealEffects.Any(h => h.Until > _world.Tick) ? 169 : -1,
                         unit.ElvenSpeed?.Until > _world.Tick ? 328 : -1,
                         unit.Negated?.Until > _world.Tick ? 185 : -1,
                         unit.FlameShield?.Until > _world.Tick ? 178 : -1,
                         unit.HeroBurns.Any(b => b.Until > _world.Tick) ? 176 : -1,
                         unit.BlindUntil > _world.Tick ? 226 : -1,
                         unit.WillowUntil > _world.Tick ? 254 : -1,
                         unit.BlessedWindUntil > _world.Tick ? 286 : -1,
                         unit.HeroicLegacy != null ? 291 : -1}.Where(e => e >= 0))
            {
                var key = (unit.Id,effect); active.Add(key);
                if (!_elvenVisuals.TryGetValue(effect,out var data)) continue;
                if (!_elvenAttachments.TryGetValue(key,out var attachment))
                {
                    var visual = new ElvenSpellEffect(data.Layers,protection:effect==350); AddChild(visual.Root);
                    attachment = (visual,_world.Tick); _elvenAttachments.Add(key,attachment);
                }
                attachment.Visual.Root.Visible = _world.CanSeeUnit(PlayerTeam,unit);
                attachment.Visual.Draw(Math.Max(0,time-attachment.Start/(double)SimWorld.TicksPerSecond),_views[unit.Id].Root.Position,unit.Definition.BodyHeightMm/1000f,loop:true);
            }
        }
        foreach (var key in _elvenAttachments.Keys.Where(k => !active.Contains(k)).ToArray())
        { _elvenAttachments[key].Visual.Free(); _elvenAttachments.Remove(key); }
    }
}
