using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _abilitySmoke;
    private readonly Dictionary<string,bool> _abilityChecks = [];
    private int _abilitySmokeTarget, _abilityCommandCount, _abilityPausedTick;
    private double _abilityPausedTime;
    private double _healingPausedTime;
    private float _healingPausedAlpha;
    private object? _abilityCooldownSnapshot;
    private void HoverAbilityPoint(Vector2 point) => GetViewport().PushInput(new InputEventMouseMotion {Position = point,GlobalPosition = point},true);
    private Vector2? VisibleHeroPoint(int id)
    {
        var unit = _world!.Units[id];
        foreach (float fraction in new[] {.85f,.65f,.45f,.25f})
        {
            var point = _camera.UnprojectPosition(UnitPosition(unit)+Vector3.Up*(unit.Definition.BodyHeightMm*fraction/1000f));
            if (UnitAt(point)?.Id == id) return point;
        }
        return null;
    }
    private Vector2 HeroPoint(int id) => VisibleHeroPoint(id) ?? throw new InvalidOperationException("No visible body point for ability target "+id);

    private void AbilitySmokeInput()
    {
        var world = _world!; var hero = world.Units[0];
        switch (_frames)
        {
            case 2:
                foreach (var team in world.Units.GroupBy(u => u.Team))
                    world.Enqueue(team.Key,team.Select(u => u.Id),OrderKind.HoldGround);
                PushKey(Key.Key1); break;
            case 5: Click(_commands.PortraitButton(0).GetGlobalRect().GetCenter()); break;
            case 8:
                _abilityChecks["source_ability_panel"] = _commands.AbilityButton(0).Visible && !_commands.AbilityButton(0).Disabled
                    && _commands.AbilityButton(1).Visible && _commands.AbilityButton(1).Disabled
                    && _commands.AbilityButton(2).Visible && _commands.AbilityButton(2).Disabled && !_commands.AbilityButton(3).Visible;
                _abilityChecks["passive_stats"] = hero.MeleeDamageRate == 30000 && hero.Immunities == (StatusProtection)15;
                _abilityChecks["clean_skill_labels"] = _commands.AbilityLabel(1) == "" && _commands.AbilityLabel(2) == ""
                    && _commands.AbilityHotkey(1).Text == "" && _commands.AbilityHotkey(2).Text == "";
                var hotkey = _commands.AbilityHotkey(0);
                _abilityChecks["skill_hotkey_bottom_right"] = hotkey.Text == "Q" && hotkey.HorizontalAlignment == HorizontalAlignment.Right
                    && hotkey.Position.X > 30 && hotkey.Position.Y > 30 && hotkey.GetRect().End.X <= _commands.AbilityButton(0).Size.X;
                _abilitySmokeTarget = world.Units.First(u => u.Id != 0 && _primarySquad.Contains(u.Id)
                    && VisibleHeroPoint(u.Id) != null && SimWorld.SeparationSquared(hero,u) <= 10000L*10000).Id;
                world.Units[_abilitySmokeTarget].Health = 60; _abilityCommandCount = world.Commands.Count;
                PushKey(Key.W); PushKey(Key.E); PushKey(Key.R); break;
            case 10:
                _abilityChecks["passive_and_empty_keys_no_orders"] = world.Commands.Count == _abilityCommandCount && _orders.Pending == null;
                PushKey(Key.Q); break;
            case 12:
                _abilityChecks["q_arms_heal"] = _orders.Pending == OrderKind.UseAbility && _commands.AbilityButton(0).ButtonPressed;
                HoverAbilityPoint(HeroPoint(_abilitySmokeTarget)); break;
            case 13:
                _abilityChecks["injured_ally_healing_cursor"] = _commands.DisplayedCursorName == "cursor_g_greater_kingsfoil";
                HoverAbilityPoint(HeroPoint(world.Units.First(u => u.Team == 1 && VisibleHeroPoint(u.Id) != null).Id)); break;
            case 14:
                _abilityChecks["enemy_invalid_cursor"] = _commands.DisplayedCursorName == "cursor_g_negative";
                ClickEnemy(); break;
            case 15:
                _abilityChecks["enemy_target_rejected"] = _orders.Pending == OrderKind.UseAbility && world.Commands.Count == _abilityCommandCount;
                Click(HeroPoint(0)); break;
            case 18:
                _abilityChecks["full_health_target_rejected"] = _orders.Pending == OrderKind.UseAbility && world.Commands.Count == _abilityCommandCount;
                _abilityChecks["full_health_invalid_cursor"] = _commands.DisplayedCursorName == "cursor_g_negative";
                Click(HeroPoint(_abilitySmokeTarget)); break;
            case 22:
                _abilityChecks["targeted_cast_recorded"] = world.Commands.Last().Kind == OrderKind.UseAbility
                    && world.Commands.Last().Target == _abilitySmokeTarget && world.Commands.Last().AbilitySlot == 0 && _orders.Pending == null;
                _abilityChecks["cast_uses_source_clip"] = hero.Casting && _views[0].Clip == hero.Ability(0)!.CastClip;
                _abilityChecks["no_early_heal"] = world.Units[_abilitySmokeTarget].Health == 60;
                _abilityChecks["targeting_cursor_clears_after_cast"] = _commands.DisplayedCursorName == null;
                PushKey(Key.Space); _abilityPausedTick = world.Tick; _abilityPausedTime = _views[0].Player!.CurrentAnimationPosition; break;
            case 27:
                _abilityChecks["pause_freezes_cast"] = world.Tick == _abilityPausedTick && world.Units[_abilitySmokeTarget].Health == 60
                    && Math.Abs(_views[0].Player!.CurrentAnimationPosition-_abilityPausedTime) < .0001;
                PushKey(Key.Space); break;
            case 37:
                _abilityChecks["source_heal_applied"] = world.Units[_abilitySmokeTarget].Health == 310 && _healLabels.Count > 0;
                _abilityChecks["original_healing_sprite_layers_visible"] = _healingLayers.Length == 5 && _healingEffects.Count == 1
                    && _healingEffects[0].VisibleSprites > 0;
                _abilityChecks["original_heal_sound_loaded"] = _abilitySounds.Count > 0;
                _abilityChecks["cooldown_displayed"] = _commands.AbilityButton(0).Disabled && _commands.AbilityLabel(0).EndsWith('s');
                _abilityCommandCount = world.Commands.Count; PushKey(Key.Q); Click(_commands.AbilityButton(0).GetGlobalRect().GetCenter()); break;
            case 38: PushKey(Key.Space); break;
            case 39:
                _healingPausedTime = _healingTime; _healingPausedAlpha = _healingEffects[0].PeakAlpha; break;
            case 40:
                _abilityChecks["healing_visual_freezes_when_paused"] = _healingTime == _healingPausedTime && _healingEffects[0].PeakAlpha == _healingPausedAlpha;
                PushKey(Key.Space); break;
            case 41:
                _abilityChecks["cooldown_prevents_input_recast"] = world.Commands.Count == _abilityCommandCount && _orders.Pending == null;
                ClickEnemy(); break;
            case 44:
                _abilityChecks["enemy_selection_hides_abilities"] = !_commands.AbilityButton(0).Visible && !_commands.AbilityButton(1).Visible;
                PushKey(Key.Q); break;
            case 47:
                _abilityChecks["enemy_selection_cannot_cast"] = world.Commands.Count == _abilityCommandCount && _orders.Pending == null;
                PushKey(Key.Key2); break;
            case 50:
                _abilityChecks["nonhero_selection_hides_abilities"] = !_commands.AbilityButton(0).Visible;
                PushKey(Key.Q); break;
            case 53:
                _abilityChecks["nonhero_cannot_cast"] = world.Commands.Count == _abilityCommandCount && _orders.Pending == null;
                PushKey(Key.Key1); break;
            case 56: Click(_commands.PortraitButton(0).GetGlobalRect().GetCenter()); break;
            case 61:
                _abilityChecks["healing_visual_follows_target"] = _healingEffects.Count == 1
                    && _healingEffects[0].Root.Position.DistanceTo(_views[_abilitySmokeTarget].Root.Position) < .001f;
                if (_capture != "") _ = CaptureFrame(false); break;
            case 120: _abilityChecks["healing_visual_expires"] = _healingEffects.Count == 0; break;
            case 380:
                _abilityCooldownSnapshot = new {world.Tick,selected = _selected.ToArray(),hero.Casting,ready_at = hero.AbilityReadyAt[0],
                    button_disabled = _commands.AbilityButton(0).Disabled,label = _commands.AbilityLabel(0),hotkey = _commands.AbilityHotkey(0).Text};
                _abilityChecks["cooldown_expires"] = !_commands.AbilityButton(0).Disabled && _commands.AbilityLabel(0) == ""
                    && _commands.AbilityHotkey(0).Text == "Q";
                hero.Health = 100; Click(_commands.AbilityButton(0).GetGlobalRect().GetCenter()); break;
            case 381:
                _abilityChecks["interface_uses_normal_cursor"] = _commands.DisplayedCursorName == null;
                HoverAbilityPoint(HeroPoint(0)); break;
            case 382:
                _abilityChecks["self_healing_cursor"] = _commands.DisplayedCursorName == "cursor_g_greater_kingsfoil";
                HoverAbilityPoint(new Vector2(20,100)); break;
            case 383:
                _abilityChecks["button_arms_heal"] = _orders.Pending == OrderKind.UseAbility;
                _abilityChecks["ground_shows_ability_targeting_cursor"] = _commands.DisplayedCursorName == "cursor_g_greater_kingsfoil";
                PushKey(Key.Escape); break;
            case 386:
                _abilityChecks["escape_cancels_ability_targeting"] = _orders.Pending == null && _selected.SetEquals([0]) && _commands.DisplayedCursorName == null;
                PushKey(Key.Q); Click(HeroPoint(0),MouseButton.Right); break;
            case 389:
                _abilityChecks["right_click_cancels_ability_targeting"] = _orders.Pending == null && world.Commands.Count == _abilityCommandCount;
                PushKey(Key.Space); _abilityPausedTick = world.Tick;
                PushKey(Key.Q); Click(HeroPoint(0)); break;
            case 392:
                _abilityChecks["paused_cast_waits_for_tick"] = world.Tick == _abilityPausedTick && !hero.Casting
                    && world.Commands.Last().Kind == OrderKind.UseAbility;
                PushKey(Key.Space); break;
            case 396:
                _abilityChecks["paused_cast_starts_on_resume"] = hero.Casting;
                PushKey(Key.S); break;
            case 410:
                _abilityChecks["stop_interrupts_without_heal"] = !hero.Casting && hero.Health == 100 && hero.AbilityReadyAt[0] > world.Tick;
                var report = new { build = BuildInfo.Label,checks = _abilityChecks,passed = _abilityChecks.Count == 38 && _abilityChecks.Values.All(v => v),
                    commands = world.Commands,cooldown_snapshot = _abilityCooldownSnapshot,final_digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"ability-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("ABILITY_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
