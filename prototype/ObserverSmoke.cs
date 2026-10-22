using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Observer mode end to end: on Bilbo's Bluffs Gimli's bot against Aragorn's (--observe --bots=Gimli,Aragorn),
/// or a free-for-all of one bot per start point on any imported map (-Map MP04_Wasteland -Bots a,b,c,d), watched by
/// nobody's side. Checks the observer panel, read-only inspection of another side, that no player command is ever
/// issued, the speed buttons and keys, and a fast-forwarded match to its result.</summary>
public partial class Main
{
    private bool _observerSmoke;
    private readonly Dictionary<string,bool> _observerChecks = [];
    private int _observerPhase,_observerStep,_observerTickMark,_observerFrameMark,_observerBurst,_observerInspected = -1,_observerSnapshotTick;
    private readonly List<string> _observerTimeline = [];

    private void ObserverSmokeInput()
    {
        var world = _world!;
        if (_frames < 2 || _observerPhase > 5) return;
        // A snapshot every two game minutes, for tuning the bots.
        if (world.Tick >= _observerSnapshotTick)
        {
            _observerSnapshotTick += 2400;
            _observerTimeline.Add($"{TimeSpan.FromSeconds(world.Tick/SimWorld.TicksPerSecond):mm\\:ss} "+string.Join(" | ",world.AiPlayers.Values.Select(a =>
                $"{a.Personality.Id} {a.Status} · food {world.Economies[a.Team].Food} ore {world.Economies[a.Team].Ore} · "
                +string.Join(",",world.ConstructionSites.Values.Where(s => s.Team == a.Team && s.Health > 0).GroupBy(s => world.SiteRules(s).DisplayName).Select(g => g.Key+(g.Count() > 1 ? "x"+g.Count() : ""))))));
        }
        switch (_observerPhase)
        {
            case 0:
                _observerChecks["every_side_runs_its_bot"] = world.Contenders.Count >= 2 && world.AiPlayers.Keys.SequenceEqual(world.Contenders)
                    && world.AiPlayers.Values.All(a => _bots.ElementAtOrDefault(a.Team) is not { Length: > 0 } bot || string.Equals(a.Personality.Id,bot,StringComparison.OrdinalIgnoreCase));
                _observerChecks["nobody_plays"] = PlayerTeam == ObserverTeam && _commands.PlayerTeam == ObserverTeam && FriendlyHeroes().Count == 0;
                _observerChecks["observer_panel_lists_every_player"] = _observerPanel.Visible && _observerRows.GetChildCount() == world.Contenders.Count
                    && world.AiPlayers.Values.Select((a,i) => _observerRows.GetChild(i).GetChild<Label>(1).Text.Contains(a.Personality.Id)).All(v => v);
                FocusStructure(SmokeEnemyCitadel);
                _observerPhase = 1; _observerStep = 0; break;
            case 1:
                // Inspect an Aragorn unit, then try to command it with a right-click on open ground.
                if (_observerStep++ == 1)
                {
                    var unit = world.Units.Where(u => u.Team == 1 && u.Present && UnitAt(_camera.UnprojectPosition(UnitPosition(u)+Vector3.Up))?.Id == u.Id).FirstOrDefault();
                    if (unit == null) { _observerPhase = 5; break; }
                    _observerInspected = unit.Id; Click(_camera.UnprojectPosition(UnitPosition(unit)+Vector3.Up));
                }
                if (_observerStep == 3) Click(_camera.UnprojectPosition(PositionOf(world.StartStructures[1].X+8000,world.StartStructures[1].Z+8000)),MouseButton.Right);
                if (_observerStep == 4) PushKey(Key.S);
                if (_observerStep < 6) break;
                _observerChecks["observer_inspects_either_side_read_only"] = _selected.SetEquals([_observerInspected])
                    && !_commands.ButtonFor(OrderKind.Attack).Visible && _commands.DisplayedOrder.StartsWith("Player 2 unit");
                _observerPhase = 2; _observerStep = 0; break;
            case 2:
                // The speed buttons, pressed and released across frames like a real click.
                var sixteen = _speedButtons[16].GetGlobalRect().GetCenter();
                if (_observerStep == 0) Press(sixteen);
                if (_observerStep == 1) Release(sixteen);
                if (_observerStep == 3) { _observerTickMark = world.Tick; _observerFrameMark = _frames; }
                if (++_observerStep < 13) break;
                _observerBurst = (world.Tick-_observerTickMark)/Math.Max(1,_frames-_observerFrameMark);
                _observerChecks["speed_button_runs_sixteen_ticks_a_frame"] = _speed == 16 && _speedButtons[16].ButtonPressed && _observerBurst >= 12;
                PushKey(Key.Minus);
                _observerPhase = 3; _observerStep = 0; break;
            case 3:
                if (++_observerStep < 2) break;
                _observerChecks["minus_key_halves_the_speed"] = _speed == 8 && _speedButtons[8].ButtonPressed;
                PushKey(Key.Equal);
                _observerPhase = 4; _observerStep = 0; break;
            case 4:
                // 40 game minutes for a duel, 20 more per extra player.
                if (!world.MatchOver && world.Tick < 48000+24000*(world.Contenders.Count-2)) { if (++_observerStep == 2 && _capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-observer.png"); break; }
                _observerChecks["fast_forward_reaches_a_result"] = world.MatchOver && world.Winner >= 0 && _speed == 16;
                _observerChecks["observer_result_names_the_winner"] = _outcome.Visible && _outcomeTitle.Text == $"Player {world.Winner+1} wins!";
                // Every bot order goes straight into the simulation; nothing came from the keyboard or mouse.
                _observerChecks["observer_never_issued_a_command"] = world.Commands.Count == 0;
                // Bodies leave a minute after death: gone ones are hidden and disabled, none goes early.
                var gone = _views.Where(p => p.Value.Buried).ToArray();
                _observerChecks["corpses_leave_after_a_minute"] = gone.Length > 0
                    && gone.All(p => !p.Value.Root.Visible && p.Value.Root.ProcessMode == Node.ProcessModeEnum.Disabled && world.Units[p.Key].Health <= 0
                        && world.Tick-p.Value.DeathTick >= CorpseTicks)
                    && _views.Where(p => !p.Value.Buried && world.Units[p.Key].Health <= 0).All(p => world.Tick-p.Value.DeathTick < CorpseTicks+CorpseSinkTicks+SimWorld.TicksPerSecond);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-result.png");
                _observerPhase = 5; _observerStep = 0; break;
            case 5:
                if (++_observerStep < 4) break; // Let the capture finish writing.
                var report = new { build = BuildInfo.Label,checks = _observerChecks,
                    passed = _observerChecks.Count == 10 && _observerChecks.Values.All(v => v),
                    fixture = $"{System.IO.Path.GetFileName(_cache)} --observe --bots={string.Join(',',_bots)}; the match runs at 16x after the speed checks",
                    timeline = _observerTimeline,corpses_buried = _corpsesBuried,winner = world.Winner,outcome_tick = world.OutcomeTick,ticks_per_frame_at_16x = _observerBurst,
                    bots = world.AiPlayers.Values.Select(a => $"{a.Personality.Id}: {a.Status}").ToArray(),
                    ticks = world.Tick,status = _status.Text,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"observer-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("OBSERVER_SMOKE "+JsonSerializer.Serialize(report)); _observerPhase = 6;
                _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
