using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>The skirmish AI end to end on Bilbo's Bluffs with the default personality: the player does nothing while
/// the enemy trains workers, builds its base, trains an army and sends an assault that razes the player's Citadel.
/// The run records when each milestone happened, in game ticks, as a timeline for tuning.</summary>
public partial class Main
{
    private bool _aiSmoke,_aiAttackCaptured,_expansionSmoke;
    private readonly Dictionary<string,bool> _aiChecks = [];
    private readonly SortedDictionary<string,int> _aiTimeline = [];
    private int _aiPhase,_aiStep,_aiMaxWorkers,_aiArmyAtLaunch = -1;
    private const int AiSmokeTickLimit = 24000;

    private void AiMilestone(string name,bool reached) { if (reached && !_aiTimeline.ContainsKey(name)) _aiTimeline[name] = _world!.Tick; }

    private void AiSmokeInput()
    {
        if (_expansionSmoke) { ExpansionSmokeInput(); return; }
        var world = _world!;
        if (_frames < 2 || _aiPhase > 3) return;
        var ai = world.AiPlayers.GetValueOrDefault(AiTeam);
        var sites = world.ConstructionSites.Values.Where(s => s.Team == AiTeam && s.Complete && !s.Prebuilt).Select(s => world.SiteRules(s).Name).ToArray();
        var soldiers = world.Units.Where(u => u.Team == AiTeam && u.Health > 0 && !world.Workers.ContainsKey(u.Definition.Name) && !u.Definition.Hero).ToArray();
        _aiMaxWorkers = Math.Max(_aiMaxWorkers,world.Units.Count(u => u.Team == AiTeam && u.Health > 0 && world.Workers.ContainsKey(u.Definition.Name)));
        AiMilestone("first_worker_trained",_aiMaxWorkers > 4);
        foreach (var name in sites) AiMilestone("built_"+(world.BuildingRules.GetValueOrDefault(name)?.DisplayName
            ?? world.ConstructionRules.Values.First(r => r.Name == name).DisplayName),true);
        AiMilestone("first_soldier",soldiers.Length > 0);
        if (ai is { PartiesSent: > 0 } && _aiArmyAtLaunch < 0) _aiArmyAtLaunch = ai.Party.Count;
        AiMilestone("assault_sent",ai is { PartiesSent: > 0 });
        var citadel = world.ConstructionSites.GetValueOrDefault(SmokeOwnCitadel);
        AiMilestone("player_citadel_damaged",citadel == null || citadel.Health < world.SiteRules(citadel).MaxHealth);
        AiMilestone("match_over",world.MatchOver);
        switch (_aiPhase)
        {
            case 0:
                // Build-49: Default plays the Balanced style, 34/33/33, so its party is 10.
                _aiChecks["enemy_runs_the_default_personality"] = ai is { Personality: { Id: "Default",Difficulty: 1 },Profile.Style.Label: "Balanced" }
                    && world.AiPartySize(ai) == 10 && AiSummary().Contains("Default · Normal · Balanced");
                _aiChecks["player_team_has_no_ai"] = !world.AiPlayers.ContainsKey(0);
                FocusStructure(SmokeEnemyCitadel);
                _aiPhase = 1; break;
            case 1:
                if (_aiTimeline.ContainsKey("player_citadel_damaged") && !_aiAttackCaptured)
                {
                    _aiAttackCaptured = true; FocusStructure(SmokeOwnCitadel);
                    if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-assault.png");
                }
                if (!world.MatchOver && world.Tick < AiSmokeTickLimit) break;
                var built = _aiTimeline.Keys.Where(k => k.StartsWith("built_")).ToArray();
                _aiChecks["enemy_trains_workers"] = _aiMaxWorkers >= 8;
                _aiChecks["enemy_builds_mill_and_foundry"] = built.Contains("built_Mill") && built.Contains("built_Foundry");
                _aiChecks["enemy_builds_camp_and_military"] = built.Contains("built_Camp") && (built.Contains("built_Barracks") || built.Contains("built_Dwarf Hall"));
                _aiChecks["enemy_trains_an_army_before_attacking"] = ai != null && _aiArmyAtLaunch >= world.AiPartySize(ai)
                    && _aiTimeline.GetValueOrDefault("assault_sent") >= ai.Profile.AssaultFromTicks;
                _aiChecks["assault_reaches_the_player_base"] = _aiTimeline.ContainsKey("player_citadel_damaged");
                _aiChecks["enemy_razes_the_passive_player"] = world.MatchOver && world.Winner == AiTeam;
                _aiPhase = 2; _aiStep = 0; break;
            case 2:
                if (++_aiStep < 4) break;
                _aiChecks["defeat_panel_shows"] = !world.MatchOver || _outcome.Visible && _outcomeTitle.Text == "You lost!";
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-defeat.png");
                _aiPhase = 3; _aiStep = 0; break;
            case 3:
                if (++_aiStep < 4) break; // Let the capture finish writing.
                var report = new { build = BuildInfo.Label,checks = _aiChecks,
                    passed = _aiChecks.Count == 9 && _aiChecks.Values.All(v => v),
                    fixture = "Player issues no orders; enemy runs the default personality",
                    timeline_ticks = _aiTimeline,max_workers = _aiMaxWorkers,party_at_launch = _aiArmyAtLaunch,
                    ai = ai?.Status,ticks = world.Tick,status = _status,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"ai-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("AI_SMOKE "+JsonSerializer.Serialize(report)); _aiPhase = 4;
                _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
