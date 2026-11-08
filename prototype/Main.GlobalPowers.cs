using Godot;
using Reforged.Simulation;
using Reforged.Rendering;
using Reforged.Input;
using System.Text.Json;

public partial class Main
{
    private int _pendingGlobalPower = -1;
    private readonly Dictionary<GlobalArea,ElvenSpellEffect> _globalVisuals = [];
    private string? _bramblesModel;

    private sealed record SummonPresentation(string Clip,int Start,double Duration,Skeleton3D? Skeleton,
        (int Bone,int Id,ElvenSpellEffect Effect)[] Attachments)
    {
        public bool Interrupted { get; set; }
    }

    private void ConfigureSummon(UnitView view,Node3D visual,JsonElement appearance,SimUnit unit)
    {
        if (unit.SummonedUntil < 0 || view.Player == null) return;
        var model = appearance.GetProperty("models")[0];
        var states = model.GetProperty("states");
        string Clip(string state) => states.EnumerateArray().First(s => s.GetProperty("name").GetString() == state)
            .GetProperty("choices")[0].GetProperty("clip").GetString()!;
        string clip = Clip("UNIT_ANIM_STATE_SPECIAL_01");
        if (AnimationName(view,clip) is not { } animation) return;
        view.StateClips["idle"] = Clip("UNIT_ANIM_STATE_IDLE");
        var skeleton = Descendants(visual).OfType<Skeleton3D>().FirstOrDefault();
        var attachments = new List<(int, int, ElvenSpellEffect)>();
        if (unit.Definition.Name == "UNIT_E_HERO_BALROG" && skeleton != null)
        {
            static string JointKey(string name) => string.Concat(name.Where(char.IsLetterOrDigit));
            var joints = Enumerable.Range(0,skeleton.GetBoneCount()).ToLookup(i => JointKey(skeleton.GetBoneName(i)));
            foreach (var binding in model.GetProperty("effects").EnumerateArray())
            {
                int id = binding.GetProperty("effect").GetProperty("id").GetInt32();
                if (!_elvenVisuals.TryGetValue(id,out var data) || id != 65 && !binding.GetProperty("shown").GetBoolean()) continue;
                int bone = joints[JointKey(binding.GetProperty("joint_name").GetString()!)].FirstOrDefault(-1);
                if (bone < 0) continue;
                var effect = new ElvenSpellEffect(data.Layers,areaRadius:id == 65 ? 1.2f : .45f);
                view.Root.AddChild(effect.Root);
                attachments.Add((bone,id,effect));
            }
        }
        view.Summon = new(clip,_world!.Tick-1,view.Player.GetAnimation(animation).Length,skeleton,attachments.ToArray());
    }

    private double SummonAge(SummonPresentation summon,double fraction = 1)
        => (_world!.Tick-1+fraction-summon.Start)/SimWorld.TicksPerSecond;

    private bool PlaySummon(UnitView view,string state)
    {
        if (view.Summon is not { Interrupted:false } summon || SummonAge(summon) >= summon.Duration) return false;
        // An actual strike or stride takes over immediately, without replaying the entrance when it ends.
        summon.Interrupted = state is not ("ready" or "idle");
        return !summon.Interrupted;
    }

    private void PoseSummon(SimUnit unit,UnitView view,double fraction)
    {
        if (view.Summon is not { } summon) return;
        double age = Math.Max(0,SummonAge(summon,fraction));
        bool arriving = view.Clip == summon.Clip;
        if (arriving && view.Player != null)
            view.Player.Seek(Math.Min(age,summon.Duration),true);
        if (summon.Skeleton is not { } skeleton) return;
        foreach (var (bone,id,effect) in summon.Attachments)
        {
            bool visible = unit.Present && (id == 65 ? arriving && age < summon.Duration*.749 : id != 53 || !arriving);
            effect.Root.Visible = visible;
            if (!visible) continue;
            var origin = skeleton.ToGlobal(skeleton.GetBoneGlobalPose(bone).Origin);
            effect.Draw(age,view.Root.ToLocal(origin),id == 65 ? 1.8f : .75f,loop:true);
        }
    }

    private NavigationHit? GlobalGroundAt(Vector2 point)
    {
        var origin=_camera.ProjectRayOrigin(point);var direction=_camera.ProjectRayNormal(point);
        return _picker.Pick(new(-origin.X,origin.Y,origin.Z),new(-direction.X,direction.Y,direction.Z));
    }

    private void ChooseGlobalPower(int slot)
    {
        if (_world == null || _observing) return;
        if (!_world.CanUseGlobalPower(PlayerTeam,slot,out string reason)) { Notify(reason); return; }
        CancelTargeting(); OpenBuildMenu(false);
        _pendingGlobalPower = slot;
        _orders.Begin(OrderKind.UseGlobalPower); _dragging = false; _hud.SelectionBox = null;
        _status = _world.GlobalPower(slot,PlayerTeam)!.Name+": choose "+(_world.GlobalPower(slot,PlayerTeam)!.Kind == GlobalPowerKind.HeroicLegacy ? "a friendly hero" : "a point on the battlefield");
    }

    private void TargetGlobalPower(Vector2 point)
    {
        if (_world?.GlobalPower(_pendingGlobalPower,PlayerTeam) is not {} power) { CancelTargeting(); return; }
        if (!_world.CanUseGlobalPower(PlayerTeam,power.Slot,out string reason)) { Notify(reason); CancelTargeting(); return; }
        var ground = GlobalGroundAt(point); var target = power.Kind == GlobalPowerKind.HeroicLegacy ? UnitAt(point) : null;
        int node = ground?.Node ?? -1,x = (int)((ground?.Position.X ?? 0)*1000),z = (int)((ground?.Position.Z ?? 0)*1000);
        if (_world.GlobalTargetRefusal(PlayerTeam,power,node,x,z,target?.Id ?? -1) is { Length: > 0 } refusal) { Notify(refusal); return; }
        _world.EnqueueGlobalPower(PlayerTeam,power.Slot,node,x,z,target?.Id ?? -1);
        CancelTargeting();
        _status = power.Name+(_paused ? " queued (paused)" : " cast");
    }

    private void UpdateGlobalPowerCursor(bool overInterface)
    {
        if (_world?.GlobalPower(_pendingGlobalPower,PlayerTeam) is not {} power || !_world.CanUseGlobalPower(PlayerTeam,_pendingGlobalPower,out _))
        { CancelTargeting(); _commands.UpdateCursor(null); return; }
        bool valid = false;
        if (!overInterface)
        {
            if (power.Kind == GlobalPowerKind.HeroicLegacy)
                valid = _world.ValidGlobalTarget(PlayerTeam,power,-1,0,0,UnitAt(_pointerPosition)?.Id ?? -1);
            else if (GlobalGroundAt(_pointerPosition) is {} ground)
            {
                valid = _world.ValidGlobalTarget(PlayerTeam,power,ground.Node,(int)(ground.Position.X*1000),(int)(ground.Position.Z*1000));
                ShowAbilityArea(ground.Position,power.RadiusMm,valid);
            }
        }
        _commands.UpdateCursor(OrderKind.UseGlobalPower,valid,overInterface,power.Cursor);
    }

    private void PresentGlobalPowerEvents()
    {
        foreach (var cast in _world!.GlobalPowerEvents)
        {
            var power = _world.Rules.GlobalPowers.Single(p => p.Id == cast.Power);
            var position = new Vector3(-cast.X/1000f,cast.Y/1000f,cast.Z/1000f);
            PlayAbilitySound(power.SoundEvents,cast.Tick,position);
            if (power.SummonUnit != null && _world.InSight(ViewTeam,cast.X,cast.Z))
            {
                if (power.Kind == GlobalPowerKind.SummonBalrog) ElvenBurst(64,-1,position,cast.Tick,3.5f);
                else if (power.Kind == GlobalPowerKind.GiantEnt)
                {
                    ElvenBurst(228,-1,position,cast.Tick,4.5f);
                    ElvenBurst(391,-1,position,cast.Tick+16,4.5f);
                }
            }
            foreach (int effect in power.Kind switch {
                         GlobalPowerKind.Blind => new[] {225,400},GlobalPowerKind.HeroicLegacy => new[] {289},_ => Array.Empty<int>() })
                ElvenBurst(effect,-1,position,cast.Tick);
        }
    }

    private void PresentSummonDeath(SimUnit unit)
    {
        if (unit.SummonedUntil < 0 || !_world!.CanSeeUnit(ViewTeam,unit)) return;
        int effect = unit.Definition.Name == "UNIT_E_HERO_BALROG" ? 306 : 227;
        ElvenBurst(effect,-1,UnitPosition(unit),_world.Tick-1,4);
    }

    private void PresentGlobalAreas()
    {
        var active = _world!.GlobalAreas.Where(a => a.Until > _world.Tick && a.Power.Kind is GlobalPowerKind.Brambles or GlobalPowerKind.BlessedWind).ToHashSet();
        foreach (var area in active)
        {
            if (!_globalVisuals.TryGetValue(area,out var visual))
            {
                int effect = area.Power.Kind == GlobalPowerKind.Brambles ? 287 : 285;
                if (!_elvenVisuals.TryGetValue(effect,out var data)) continue;
                visual = new ElvenSpellEffect(data.Layers,area:true,areaRadius:area.Power.RadiusMm/1000f);
                if (area.Power.Kind == GlobalPowerKind.Brambles && _bramblesModel is {} model)
                    visual.Root.AddChild(LoadGlb(model));
                AddChild(visual.Root); _globalVisuals.Add(area,visual);
            }
            visual.Draw(Math.Max(0,_healingTime-area.Start/(double)SimWorld.TicksPerSecond),
                new(-area.X/1000f,area.Y/1000f,area.Z/1000f),2,loop:true);
        }
        foreach (var area in _globalVisuals.Keys.Where(a => !active.Contains(a)).ToArray())
        { _globalVisuals[area].Free(); _globalVisuals.Remove(area); }
    }
}
