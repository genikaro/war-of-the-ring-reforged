using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>The Razing win condition end to end, over two rounds joined by the result panel's Restart button.
/// Round one: a player squad right-clicked onto the enemy Citadel razes it, and the victory panel pauses the game
/// and blocks the battlefield. Round two: an enemy squad razes the player's Citadel, the defeat panel appears,
/// and Continue resumes play on the decided match. Fixture: both squads are spawned beside their target.</summary>
public partial class Main
{
    private bool _victorySmoke;
    // The smoke outlives the scene reload that Restart performs.
    private static readonly Dictionary<string,bool> _victoryChecks = [];
    private static int _victoryRound;
    private int _victoryPhase,_victoryStep,_victoryPausedTick = -1;
    private int[] _victorySquad = [];

    private void PressRelease(Button button)
    {
        var point = button.GetGlobalRect().GetCenter();
        if (_victoryStep % 2 == 0) Press(point); else Release(point);
    }

    private void VictorySmokeInput()
    {
        var world = _world!;
        if (_frames < 2 || _victoryPhase > 10) return;
        int enemyCitadel = SmokeEnemyCitadel,ownCitadel = SmokeOwnCitadel;
        switch (_victoryPhase)
        {
            case 0 when _victoryRound == 0:
                _victoryChecks["skirmish_has_two_contenders"] = world.Contenders.SetEquals([0,1]) && !world.MatchOver;
                FocusStructure(enemyCitadel);
                _selected.Clear(); _victorySquad = SpawnSiegeSquad(0,enemyCitadel,8).ToArray();
                foreach (int id in _victorySquad) _selected.Add(id);
                _victoryPhase = 1; _victoryStep = 0; break;
            case 1:
                if (++_victoryStep == 3) RightClickStructure(enemyCitadel);
                if (!world.MatchOver) { if (_victoryStep > 4000) _victoryPhase = 10; break; }
                _victoryChecks["razing_the_enemy_citadel_wins"] = world.Winner == 0 && world.DefeatedTeams.SetEquals([1])
                    && !world.ConstructionSites.ContainsKey(enemyCitadel);
                _victoryChecks["enemy_units_do_not_matter"] = world.Units.Any(u => u.Team == 1 && u.Present);
                _victoryPhase = 2; _victoryStep = 0; break;
            case 2:
                if (++_victoryStep < 3) break;
                if (_victoryStep == 3)
                {
                    _victoryPausedTick = world.Tick;
                    _victoryChecks["victory_panel_shows"] = _outcome.Visible && _outcomeTitle.Text == "You won!" && _paused
                        && _status.StartsWith("Victory");
                    if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-victory.png");
                    // A click on the battlefield behind the panel must not reach it.
                    _selected.Clear(); Click(_camera.UnprojectPosition(UnitPosition(world.Units[_victorySquad[0]])+Vector3.Up));
                    break;
                }
                if (_victoryStep < 10) break;
                _victoryChecks["panel_pauses_and_blocks_the_battlefield"] = world.Tick == _victoryPausedTick && _selected.Count == 0;
                _victoryPhase = 3; _victoryStep = 0; break;
            case 3:
                // Restart reloads the scene; the next Main instance resumes at round two.
                _victoryRound = 1;
                PressRelease(_outcomeRestart);
                if (++_victoryStep > 30) _victoryPhase = 10;
                break;
            case 0 when _victoryRound == 1:
                _victoryChecks["restart_reloads_the_skirmish"] = world.Tick < 60 && !world.MatchOver && world.ConstructionSites.ContainsKey(enemyCitadel)
                    && !_outcome.Visible && !_paused;
                FocusStructure(ownCitadel);
                world.Enqueue(1,SpawnSiegeSquad(1,ownCitadel,8),OrderKind.AttackStructure,target: ownCitadel);
                _victoryPhase = 6; _victoryStep = 0; break;
            case 6:
                if (!world.MatchOver) { if (++_victoryStep > 4000) _victoryPhase = 10; break; }
                _victoryChecks["losing_the_last_building_loses"] = world.Winner == 1 && world.DefeatedTeams.SetEquals([0])
                    && world.Units.Any(u => u.Team == 0 && u.Present);
                _victoryPhase = 7; _victoryStep = 0; break;
            case 7:
                if (++_victoryStep < 3) break;
                _victoryChecks["defeat_panel_shows"] = _outcome.Visible && _outcomeTitle.Text == "You lost!" && _paused && _status.StartsWith("Defeat");
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-defeat.png");
                _victoryPhase = 8; _victoryStep = 0; break;
            case 8:
                if (++_victoryStep < 4) break; // Let the capture finish writing.
                PressRelease(_outcomeContinue);
                if (_victoryStep < 6) break;
                _victoryPhase = 9; _victoryStep = 0; break;
            case 9:
                if (++_victoryStep < 20) break;
                _victoryChecks["continue_resumes_the_decided_match"] = !_outcome.Visible && !_paused && world.Tick > world.OutcomeTick+10
                    && world.Winner == 1 && world.MatchOver;
                _victoryPhase = 10; break;
            case 10:
                var report = new { build = BuildInfo.Label,checks = _victoryChecks,
                    passed = _victoryChecks.Count == 9 && _victoryChecks.Values.All(v => v),
                    fixture = "Squads spawn beside the Citadel they raze; round two follows the Restart button",
                    round = _victoryRound,winner = world.Winner,defeated = world.DefeatedTeams.ToArray(),outcome_tick = world.OutcomeTick,
                    ticks = world.Tick,status = _status,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"victory-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("VICTORY_SMOKE "+JsonSerializer.Serialize(report)); _victoryPhase = 11;
                _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
