using Godot;
using Reforged.Presentation;
using Reforged.Rendering;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Build-129: blood. A unit that takes a weapon hit plays its source blood effect (Units.BloodEffectType) and the
/// attacker's weapon plays its impact effect against the victim's composition (WeaponEffects); a dying unit spills one of
/// its three source death pools; a melee weapon that drew blood shows its bloody overlay (ModelFlags WeaponBloodRight/Left)
/// for the rest of the unit's life. Effects the source marks mature follow Settings > Game > Blood.</summary>
public partial class Main
{
    private BloodEffects? _blood;
    private bool _bloodShown = true;
    private readonly HashSet<int> _bloodDeaths = [];
    private readonly HashSet<int> _bloodyWeapons = [];
    private int _bloodHits,_bloodImpacts,_bloodPools,_bloodCriticals;

    private void LoadBlood()
    {
        if (!_cacheFiles.Root.TryGetProperty("blood",out var file)) return;
        _blood = BloodEffects.Load(_cacheFiles.Content,CachePath,ReadJson(file.GetString()!));
        AddChild(_blood.Root);
    }

    private void UseBloodSetting(bool shown)
    {
        _bloodShown = shown;
        if (!shown) _blood?.Clear();
        foreach (int id in _bloodyWeapons)
            if (_views.TryGetValue(id,out var view) && view.Appearance is { } appearance) appearance.WeaponBlood = shown;
    }

    private bool BloodAllowed(int effect) => _blood?.EffectById(effect) is { } e && (_bloodShown || !e.Mature);

    /// <summary>One simulation step's hits, ability blows and deaths.</summary>
    private void PresentBloodEvents()
    {
        if (_blood == null || _world == null) return;
        double now = _world.Tick/(double)SimWorld.TicksPerSecond;
        foreach (var combat in _world.CombatEvents)
        {
            if (combat.Kind != CombatEventKind.Hit || combat.DamageMilli <= 0 || combat.Target < 0) continue;
            var attacker = combat.Unit >= 0 ? _world.Units[combat.Unit] : null;
            var from = attacker != null ? UnitPosition(attacker)
                : _world.ConstructionSites.TryGetValue(combat.Site,out var site) ? SitePoint(site) : UnitPosition(_world.Units[combat.Target]);
            Bleed(_world.Units[combat.Target],from,attacker,attacker == null || attacker.Definition.FiresProjectile(combat.Attack),now,
                combat.DamageMilli,combat.Hit);
        }
        // Spell damage (Trueshot's arrow, the bear's Savage Blow) has no weapon hit event of its own.
        foreach (var e in _world.AbilityEvents)
            if (e.Kind is AbilityEventKind.Trueshot or AbilityEventKind.SavageBlow && e.Target >= 0 && e.Unit >= 0)
                Bleed(_world.Units[e.Target],UnitPosition(_world.Units[e.Unit]),null,e.Kind == AbilityEventKind.Trueshot,now,
                    e.Kind == AbilityEventKind.Trueshot ? e.HealthMilli : _world.Units[e.Unit].Ability(e.Slot)?.HealMilli ?? 0,e.Slot);
        if (_bloodSmoke) BloodSmokeStep();
        foreach (var unit in _world.Units)
        {
            if (unit.Health > 0 || !_bloodDeaths.Add(unit.Id) || unit.InsideSite >= 0 || !_bloodShown) continue;
            if (_blood.Unit(unit.Definition.Name) is not { } source || !_world.CanSeeUnit(ViewTeam,unit)) continue;
            if (unit.CriticalDeath && source.CriticalDeath is int burst && BloodAllowed(burst) && Explode(unit,burst,now)) continue;
            if (source.DeathPools.Length == 0) continue;
            int pool = source.DeathPools[(int)((uint)(unit.Id*31+_world.Tick)%source.DeathPools.Length)];
            if (_blood.Spill(pool,UnitPosition(unit),GroundNormal(unit),unit.Id,now)) _bloodPools++;
        }
    }

    /// <summary>Build-130: a critical death bursts the body into its source gore (skull, bones, flesh, a blood-vapour
    /// cloud, splats and a blood mark) instead of falling; the model disappears behind the burst.</summary>
    private bool Explode(SimUnit unit,int burst,double now)
    {
        var view = _views[unit.Id];
        var feet = UnitPosition(unit);
        var chest = feet+Vector3.Up*unit.Definition.BodyHeightMm/2000f;
        float Ground(Vector3 point) => unit.Node < _world!.Navigation.GroundCount
            ? _world.Navigation.GroundHeight((int)(-point.X*1000),(int)(point.Z*1000))/1000f : feet.Y;
        if (!_blood!.Play(burst,chest,feet,Vector3.Zero,false,now,unit.Id,Ground)) return false;
        view.Exploded = true; view.Root.Visible = false; _bloodCriticals++;
        return true;
    }

    private Vector3 SitePoint(ConstructionSite site)
    {
        var (x,z) = _world!.SitePosition(site);
        return new(-x/1000f,_world.Navigation.GroundHeight(x,z)/1000f+3,z/1000f);
    }

    /// <summary>The struck unit's blood at the side facing the blow, and the attacker's weapon impact against it.</summary>
    private void Bleed(SimUnit victim,Vector3 from,SimUnit? attacker,bool missile,double now,long damageMilli,int hit)
    {
        if (_blood!.Unit(victim.Definition.Name) is not { } source || !_world!.CanSeeUnit(ViewTeam,victim)) return;
        var view = _views[victim.Id];
        var feet = view.Root.Position;
        var away = feet-from; away.Y = 0;
        if (away.LengthSquared() < .0001f) away = -view.Root.GlobalBasis.Z;
        away = away.Normalized();
        float height = victim.Definition.BodyHeightMm/1000f,radius = victim.Definition.BodyRadiusMm/1000f;
        var point = feet+Vector3.Up*height*(missile ? .5f : .6f)-away*Math.Min(.3f,radius*.5f);
        bool left = away.Dot(_camera.GlobalBasis.X) < 0;
        // Build-131 (Reforged option): wounded units bleed more often, harder hits throw more blood.
        bool scaled = _settings.IsOn(BloodIntensity.Option),bled = false;
        bool bleeds = !scaled || BloodIntensity.Bleeds(victim.HealthMilli,(long)victim.MaxHealth*1000,victim.Id,_world.Tick,attacker?.Id ?? -1,hit);
        if (source.Blood is int blood && bleeds && BloodAllowed(blood)
            && _blood.Play(blood,point,feet,away,left,now,amountScale: scaled ? BloodIntensity.AmountScale(damageMilli) : 1))
        { _bloodHits++; bled = true; }
        if (_bloodSmoke) _bloodRolls.Add(((double)victim.HealthMilli/Math.Max(1,(long)victim.MaxHealth*1000),bled,damageMilli,bled ? _blood.LastParticles : 0));
        if (attacker == null || _blood.Unit(attacker.Definition.Name) is not { } weapon) return;
        int composition = _unitCompositions.GetValueOrDefault(victim.Definition.Name,-1);
        if ((missile ? weapon.MissileImpacts : weapon.MeleeImpacts).TryGetValue(composition,out int impact) && impact != source.Blood
            && BloodAllowed(impact) && _blood.Play(impact,point,feet,away,left,now)) _bloodImpacts++;
        // A blade that drew blood stays bloody; sap or a wraith's shadow does not stain it.
        if (!missile && bled && source.Blood is int stain && _blood.EffectById(stain)?.Mature == true
            && _views[attacker.Id].Appearance is { HasWeaponBlood: true } appearance && _bloodyWeapons.Add(attacker.Id))
            appearance.WeaponBlood = _bloodShown;
    }

    private Vector3 GroundNormal(SimUnit unit)
    {
        if (unit.Node >= _world!.Navigation.GroundCount) return Vector3.Up;
        float H(int dx,int dz) => _world.Navigation.GroundHeight(unit.X+dx,unit.Z+dz)/1000f;
        // World x is the simulation's -x; one metre either side gives the slope.
        float slopeX = (H(-500,0)-H(500,0))/1f,slopeZ = (H(0,500)-H(0,-500))/1f;
        return new Vector3(-slopeX,1,-slopeZ).Normalized();
    }

    /// <summary>Each frame: layer delays, particle pace and pool fades follow the game clock.</summary>
    private void PresentBlood(double fraction)
    {
        if (_blood == null || _world == null) return;
        double now = (_world.Tick-1+fraction)/SimWorld.TicksPerSecond;
        _blood.Update(now,_paused ? 0 : _speed);
        _blood.UpdatePools(now);
    }

    // -BloodSmoke (build-129): a Swordsman/Uruk-hai brawl and a Trueshot on the Sandbox map, captured mid-fight;
    // build-130 adds Gimli's critical kills; build-131 a Swordsman and an Elven Archer striking an enemy building.
    private bool _bloodSmoke;
    private int[] _bloodBrawl = [],_bloodGimliVictims = [],_bloodSiegers = [];
    private int _bloodSiegeSite = -1,_bloodSoundsSeen,_bloodImpactsSeen;
    private readonly int[] _bloodSiegeHits = new int[2],_bloodSiegeSounds = new int[2];
    private int _bloodLegolas = -1,_bloodKnocked = -1,_bloodGimli = -1,_bloodFollowedThrough,_bloodCutShort,_bloodFirstHit = -1,_bloodFirstBurst = -1;
    private bool _bloodKnockClip,_bloodGetUpClip,_bloodKnockLocked;
    private readonly List<(int Unit,int Start,int End)> _bloodKills = [];
    private readonly Dictionary<string,bool> _bloodChecks = [];
    private readonly List<(double Health,bool Bled,long DamageMilli,int Particles)> _bloodRolls = [];

    /// <summary>Strikes whose victim fell mid-swing must keep playing until their clip ends (no cancel, no new swing).</summary>
    private void BloodSmokeStep()
    {
        var w = _world!;
        // A building hit is heard when this step played a sound: a melee hit through the game's effect voices, an arrow
        // through the combat impact voices.
        for (int i = 0; i < _bloodSiegers.Length && _frames < 298; i++)
            if (w.CombatEvents.Any(c => c.Kind == CombatEventKind.Hit && c.Unit == _bloodSiegers[i] && c.Target < 0 && c.Site == _bloodSiegeSite && c.DamageMilli > 0))
            {
                _bloodSiegeHits[i]++;
                if (i == 0 ? _audio.EffectsPlayed > _bloodSoundsSeen : _impactSamples > _bloodImpactsSeen) _bloodSiegeSounds[i]++;
            }
        _bloodSoundsSeen = _audio.EffectsPlayed; _bloodImpactsSeen = _impactSamples;
        foreach (var combat in w.CombatEvents.Where(c => c.Kind == CombatEventKind.Hit && c.Unit >= 0 && c.Target >= 0 && w.Units[c.Target].Health <= 0))
        {
            var attacker = w.Units[combat.Unit];
            int end = combat.StartTick+attacker.Definition.Attacks[combat.Attack].DurationTicks;
            if (attacker.Present && w.Tick < end) _bloodKills.Add((attacker.Id,combat.StartTick,end));
        }
        foreach (var (unit,start,end) in _bloodKills.ToArray())
        {
            var attacker = w.Units[unit];
            if (w.Tick >= end || !attacker.Present) { _bloodKills.Remove((unit,start,end)); _bloodFollowedThrough++; continue; }
            if (attacker.AttackStartTick != start) { _bloodKills.Remove((unit,start,end)); _bloodCutShort++; }
        }
    }

    private void BloodSmokeInput()
    {
        var w = _world!;
        void Capture(string stage)
        {
            if (_capture != "") _ = CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,
                System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        int Spawn(int team,string name,int x,int z,int health,bool passive = false)
        {
            var d = w.Rules.Units[name] with {MaxHealth = Math.Max(health,w.Rules.Units[name].MaxHealth),Passive = passive};
            var nav = w.Navigation.ForBody(d.BodyRadiusMm,d.BodyHeightMm);
            // The Sandbox's own units and dummies stand here and there: take the nearest spot clear of bodies.
            SimUnit? unit = null;
            for (int ring = 0; unit == null && ring < 12; ring++)
                for (int side = 0; unit == null && side < (ring == 0 ? 1 : 8); side++)
                    try { unit = w.Spawn(team,nav.Nearest(nav.Cell(x+ring*800*Math.Sign(side%3-1),z+ring*800*Math.Sign(side/3-1))),d); }
                    catch (ArgumentException) { }
            if (unit == null) throw new InvalidOperationException("No room to spawn "+name);
            unit.Health = health;
            foreach (var a in (d.Abilities ?? []).Where(a => a.Purchasable)) w.EnqueueAbilityPurchase(team,unit.Id,a.Slot);
            return unit.Id;
        }
        if (_bloodKnocked >= 0)
        {
            var knocked = w.Units[_bloodKnocked];
            string clip = _views[_bloodKnocked].Clip;
            _bloodKnockClip |= clip == knocked.Definition.KnockbackClip && knocked.KnockbackUntil > w.Tick;
            _bloodGetUpClip |= clip == knocked.Definition.GetUpClip && knocked.KnockbackUntil <= w.Tick && knocked.NatureLocked(w.Tick);
            _bloodKnockLocked |= knocked.KnockbackRecoverUntil >= knocked.KnockbackUntil+knocked.Definition.GetUpTicks && knocked.Definition.GetUpTicks > 0;
            if (knocked.KnockbackStart >= 0 && w.Tick == knocked.KnockbackStart+10) Capture("knockback");
            if (knocked.KnockbackStart >= 0 && w.Tick == knocked.KnockbackRecoverUntil-knocked.Definition.GetUpTicks/2) Capture("get-up");
        }
        // Close-ups a few frames into the first blows, while their blood is in the air.
        if (_bloodFirstHit < 0 && _bloodHits > 0 && _frames < 150)
        {
            _bloodFirstHit = _frames;
            _focus = UnitPosition(w.Units[_bloodBrawl[0]]).Lerp(UnitPosition(w.Units[_bloodBrawl[2]]),.5f); _distance = 9; UpdateCamera();
        }
        if (_bloodFirstHit >= 0 && _frames == _bloodFirstHit+3) Capture("closeup");
        if (_bloodFirstHit >= 0 && _frames == _bloodFirstHit+9) Capture("closeup-late");
        if (_bloodFirstHit >= 0 && _frames == _bloodFirstHit+12) { _distance = 16; UpdateCamera(); }
        if (_bloodFirstBurst < 0 && _bloodCriticals > 0) _bloodFirstBurst = _frames;
        if (_bloodFirstBurst >= 0 && _frames == _bloodFirstBurst+2) Capture("critical-burst");
        if (_bloodFirstBurst >= 0 && _frames == _bloodFirstBurst+10) Capture("critical-gore");
        if (_bloodFirstBurst >= 0 && _frames == _bloodFirstBurst+40) Capture("critical-rest");
        switch (_frames)
        {
            case 2:
                foreach (var u in w.Units) w.Enqueue(u.Team,[u.Id],OrderKind.HoldGround);
                w.Rules.Regeneration = RegenerationRates.None;
                int cx = w.Navigation.Width*w.Navigation.CellSize/2,cz = w.Navigation.Height*w.Navigation.CellSize/2;
                _bloodBrawl = [Spawn(0,"UNIT_G_GONDOR_SWORDSMAN",cx-1800,cz-1000,2000),Spawn(0,"UNIT_G_GONDOR_SWORDSMAN",cx-1800,cz+1000,2000),
                    Spawn(1,"UNIT_E_URUK_HAI",cx+1800,cz-1000,40),Spawn(1,"UNIT_E_URUK_HAI",cx+1800,cz+1000,40),
                    Spawn(1,"UNIT_E_URUK_HAI",cx+3200,cz,60)];
                _bloodLegolas = Spawn(0,"UNIT_G_HERO_LEGOLAS",cx,cz-7000,2000,passive: true);
                _bloodKnocked = Spawn(1,"UNIT_E_URUK_HAI",cx,cz-15000,2000,passive: true);
                _bloodGimli = Spawn(0,"UNIT_G_HERO_GIMLI",cx+6000,cz+7000,2000);
                _bloodGimliVictims = [Spawn(1,"UNIT_E_URUK_HAI",cx+12500,cz+6200,30,passive: true),Spawn(1,"UNIT_E_URUK_HAI",cx+12500,cz+7800,30,passive: true)];
                foreach (int id in (int[])[_bloodLegolas,_bloodKnocked,_bloodGimli,.._bloodGimliVictims]) w.Units[id].Order = OrderKind.HoldGround;
                // The nearest standing enemy building to the field's centre, struck from its near side.
                var building = w.ConstructionSites.Values.Where(site => site.Team == 1 && site.Complete && site.Health > 0 && site.Building != null)
                    .OrderBy(site => (long)(site.X-cx)*(site.X-cx)+(long)(site.Z-cz)*(site.Z-cz)).First();
                _bloodSiegeSite = building.Id;
                long reach = Math.Max(1,(long)Math.Sqrt((double)(building.X-cx)*(building.X-cx)+(double)(building.Z-cz)*(building.Z-cz)));
                int Toward(int from,int to) => (int)(from+(to-from)*14000/reach);
                _bloodSiegers = [Spawn(0,"UNIT_G_GONDOR_SWORDSMAN",Toward(building.X,cx),Toward(building.Z,cz),2000,passive: true),
                    Spawn(0,"UNIT_G_ELVEN_ARCHER",Toward(building.X,cx)+1500,Toward(building.Z,cz),2000,passive: true)];
                foreach (int id in _bloodSiegers) w.Units[id].Order = OrderKind.HoldGround;
                _focus = UnitPosition(w.Units[_bloodBrawl[0]]).Lerp(UnitPosition(w.Units[_bloodBrawl[2]]),.5f); _distance = 16; UpdateCamera();
                break;
            case 4:
                foreach (int id in _bloodBrawl) w.Enqueue(w.Units[id].Team,[id],OrderKind.AttackMove,cell: w.Units[_bloodBrawl[w.Units[id].Team == 0 ? 2 : 0]].Node);
                break;
            case 46: Capture("hit"); break;
            case 58: Capture("splash"); break;
            case 150: Capture("pools"); break;
            case 170:
                _bloodChecks["hits_play_source_blood"] = _bloodHits > 0 && _blood?.Started > 0;
                _bloodChecks["weapon_flash_on_flesh"] = _bloodImpacts > 0;
                _bloodChecks["deaths_spill_source_pools"] = _bloodPools > 0 && _blood?.VisiblePools > 0;
                _bloodChecks["swordsman_blade_bloodied"] = _bloodBrawl.Take(2).Any(id => _bloodyWeapons.Contains(id)
                    && _views[id].Appearance is { WeaponBloodVisible: true });
                _bloodChecks["swings_follow_through_after_kill"] = _bloodFollowedThrough > 0 && _bloodCutShort == 0;
                var shooter = UnitPosition(w.Units[_bloodLegolas]);var target = UnitPosition(w.Units[_bloodKnocked]);
                _focus = target+(target-shooter).Normalized()*7; _distance = 18; UpdateCamera();
                w.EnqueueAbility(0,[_bloodLegolas],0,_bloodKnocked);
                break;
            case 176: w.Enqueue(0,_bloodSiegers,OrderKind.AttackStructure,target: _bloodSiegeSite); break;
            case 300:
                _focus = UnitPosition(w.Units[_bloodGimli]).Lerp(UnitPosition(w.Units[_bloodGimliVictims[0]]),.6f); _distance = 13; UpdateCamera();
                w.Enqueue(0,[_bloodGimli],OrderKind.Attack,target: _bloodGimliVictims[0]);
                w.Enqueue(0,[_bloodGimli],OrderKind.Attack,target: _bloodGimliVictims[1],queued: true);
                break;
            case 380:
                var victim = w.Units[_bloodKnocked];
                _bloodChecks["knockback_plays_knocked_back_then_get_up"] = _bloodKnockClip && _bloodGetUpClip && _bloodKnockLocked;
                _bloodChecks["knocked_unit_recovers"] = !victim.NatureLocked(w.Tick) && victim.Present;
                _bloodChecks["gimli_kills_burst_into_gore"] = _bloodCriticals >= 2 && _bloodGimliVictims.All(id => w.Units[id] is { Health: <= 0,CriticalDeath: true }
                    && _views[id] is { Exploded: true,Root.Visible: false });
                _bloodChecks["swordsman_kills_stay_ordinary"] = _bloodBrawl.Skip(2).Where(id => w.Units[id].Health <= 0).All(id => !w.Units[id].CriticalDeath)
                    && _bloodBrawl.Skip(2).Any(id => w.Units[id].Health <= 0);
                var played = _bloodRolls.Where(r => r.Bled).OrderBy(r => r.DamageMilli).ToArray();
                _bloodChecks["unhurt_units_do_not_bleed"] = _bloodRolls.Any(r => r.Health >= .8) && _bloodRolls.Where(r => r.Health >= .8).All(r => !r.Bled);
                _bloodChecks["badly_wounded_units_always_bleed"] = _bloodRolls.Any(r => r.Health <= .2) && _bloodRolls.Where(r => r.Health <= .2).All(r => r.Bled);
                _bloodChecks["harder_hits_bleed_more"] = played.Select(r => r.Particles).Distinct().Count() > 1
                    && played.Zip(played.Skip(1)).All(p => p.First.DamageMilli == p.Second.DamageMilli || p.First.Particles <= p.Second.Particles);
                _bloodChecks["building_hits_are_heard"] = _bloodSiegeHits.All(h => h > 0)
                    && _bloodSiegeSounds[0] == _bloodSiegeHits[0] && _bloodSiegeSounds[1] == _bloodSiegeHits[1];
                OpenGameMenu(); break;
            case 382: Click(_gameMenu.SettingsButton.GetGlobalRect().GetCenter()); break;
            case 384:
                _bloodChecks["settings_game_tab_has_blood_on"] = _gameMenu.SettingsOpen && _gameMenu.BloodBox.IsVisibleInTree() && _gameMenu.BloodBox.ButtonPressed;
                Click(_gameMenu.BloodBox.GetGlobalRect().GetCenter()); Click(_gameMenu.ApplyButton.GetGlobalRect().GetCenter()); break;
            case 386:
                _bloodChecks["blood_setting_hides_effects"] = !_settings.Blood && !_bloodShown && _blood?.ActiveBursts == 0 && _blood?.VisiblePools == 0
                    && _bloodBrawl.Take(2).All(id => _views[id].Appearance?.WeaponBlood == false);
                var report = new {build = BuildInfo.Label,hits = _bloodHits,impacts = _bloodImpacts,pools = _bloodPools,
                    bursts = _blood?.Started ?? 0,criticals = _bloodCriticals,followed_through = _bloodFollowedThrough,cut_short = _bloodCutShort,
                    building_hits = _bloodSiegeHits,building_sounds = _bloodSiegeSounds,
                    rolls = _bloodRolls.Select(r => new {health = Math.Round(r.Health,3),r.Bled,damage = r.DamageMilli/1000.0,r.Particles}).ToArray(),
                    checks = _bloodChecks,passed = _bloodChecks.Count == 15 && _bloodChecks.Values.All(v => v)};
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"blood-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("BLOOD_SMOKE "+JsonSerializer.Serialize(report));
                _ = FinishCombatAudio(report.passed ? 0 : 1);
                break;
        }
    }
}
