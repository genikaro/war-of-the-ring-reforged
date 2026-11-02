using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Build-150 fog of war on Bilbo's Bluffs, you against the Default bot (run.ps1 -FogSmoke [-CapturePath x.png]):
/// at the start the enemy base is hidden in the world and on the minimap and in haze; a worker walks over and finds it;
/// walked home, the enemy's units vanish while its Citadel stays as last seen; Reveal map shows everything; and the bot
/// sends a scout that finds your base. Captures -found.png and -memory.png beside the given path.</summary>
public partial class Main
{
    private bool _fogSmoke;
    private readonly Dictionary<string,bool> _fogChecks = [];
    private int _fogPhase,_fogScout = -1,_fogMark,_fogFound = -1;
    private int _fogTicks => _world!.Tick;

    private void FogSmokeInput()
    {
        var world = _world!; int team = PlayerTeam;
        var home = world.ConstructionSites.Values.First(s => s.Team == team && s.Prebuilt);
        var enemy = world.ConstructionSites.Values.First(s => s.Team != team && s.Team >= 0 && s.Prebuilt);
        var (ex,ez) = world.SitePosition(enemy); var (hx,hz) = world.SitePosition(home);
        var enemies = world.Units.Where(u => u.Team == enemy.Team && u.Present).ToArray();
        var bot = world.AiPlayers[enemy.Team];
        string Capture(string suffix) => System.IO.Path.ChangeExtension(_capture,null)+suffix;
        void Look(int x,int z) { _focus = PositionOf(x,z); UpdateCamera(); }
        int Spot(SimUnit unit,int x,int z) => world.Navigation.ForBody(unit.Definition.BodyRadiusMm,unit.Definition.BodyHeightMm)
            .Nearest(world.Navigation.Cell(x,z),connectedTo: unit.Node);
        switch (_fogPhase)
        {
            case 0 when _fogTicks >= 20:
                _fogChecks["fog_on_for_the_match"] = world.FogOfWar && ViewTeam == team && _fogOverlay?.Node.Visible == true;
                _fogChecks["enemy_base_unknown_at_start"] = world.RememberedSite(team,enemy.Id) == null && !_constructionViews[enemy.Id].Root.Visible;
                _fogChecks["enemy_units_hidden_at_start"] = enemies.Length > 0 && enemies.All(u => !world.CanSeeUnit(team,u) && !_views[u.Id].Root.Visible);
                _fogChecks["own_units_shown"] = world.Units.Where(u => u.Team == team && u.Present).All(u => _views[u.Id].Root.Visible);
                _fogChecks["minimap_shows_own_buildings_only"] = _commands.Minimap.Buildings().All(b => b.Team == team);
                _fogChecks["haze_over_the_enemy_base_not_home"] = _fogOverlay!.Shown(ex,ez) == 0 && _fogOverlay.Shown(hx,hz) == 1;
                _fogChecks["minimap_grey_outside_sight"] = _commands.Minimap.ColouredAt(hx,hz) && !_commands.Minimap.ColouredAt(ex,ez);
                var scout = world.Units.Where(u => u.Team == team && u.Present).OrderBy(u => u.Id).First();
                _fogScout = scout.Id;
                world.Enqueue(team,[scout.Id],OrderKind.Move,Spot(scout,ex,ez));
                _fogPhase++; _fogMark = _fogTicks; break;
            case 1:
                var walker = world.Units[_fogScout];
                if (world.RememberedSite(team,enemy.Id) == null)
                {
                    if (_fogTicks-_fogMark > 3000 || !walker.Present) { _fogChecks["worker_finds_the_enemy_base"] = false; FinishFogSmoke(); }
                    break;
                }
                _fogChecks["worker_finds_the_enemy_base"] = true;
                // A few frames later, once the view and the minimap have caught up with the sight.
                if (_fogFound < 0) _fogFound = _fogTicks;
                if (_fogTicks-_fogFound < 8) break;
                _fogChecks["found_base_shown"] = _constructionViews[enemy.Id].Root.Visible && SightOf(enemy) == SiteSight.Live
                    && _commands.Minimap.Buildings().Any(b => b.Team == enemy.Team);
                _fogChecks["enemy_units_in_sight_shown"] = enemies.Any(u => world.CanSeeUnit(team,u) && _views[u.Id].Root.Visible)
                    && enemies.All(u => world.CanSeeUnit(team,u) == _views[u.Id].Root.Visible || u.InsideSite >= 0);
                Look(walker.X,walker.Z);
                if (_capture != "") _ = CaptureFrame(false,Capture("-found.png"));
                world.Enqueue(team,[walker.Id],OrderKind.Move,Spot(walker,hx,hz));
                _fogPhase++; _fogMark = _fogTicks; break;
            case 2:
                if (world.InSight(team,ex,ez) || world.Units[_fogScout].Present && SimWorld.DistanceSquared(world.Units[_fogScout].X,world.Units[_fogScout].Z,ex,ez) < 45000L*45000)
                {
                    if (_fogTicks-_fogMark > 3000) { _fogChecks["enemy_base_left_behind"] = false; FinishFogSmoke(); }
                    break;
                }
                if (_fogTicks-_fogMark < 60) break;
                _fogChecks["enemy_base_left_behind"] = true;
                _fogChecks["citadel_remembered_out_of_sight"] = SightOf(enemy) == SiteSight.Remembered && _constructionViews[enemy.Id].Root.Visible
                    && _commands.Minimap.Buildings().Any(b => b.Team == enemy.Team);
                var unseen = enemies.Where(u => !world.InSight(team,u.X,u.Z)).ToArray();
                _fogChecks["enemy_units_vanish_under_fog"] = unseen.Length > 0 && unseen.All(u => !world.CanSeeUnit(team,u) && !_views[u.Id].Root.Visible);
                _fogChecks["haze_returns_over_the_enemy_base"] = _fogOverlay!.Shown(ex,ez) == 0 && !_commands.Minimap.ColouredAt(ex,ez);
                Look(ex,ez);
                if (_capture != "") _ = CaptureFrame(false,Capture("-memory.png"));
                _fogPhase++; _fogMark = _fogTicks; break;
            case 3 when _fogTicks-_fogMark >= 10:
                // After the capture: Reveal map.
                _revealMap = true;
                _fogPhase++; _fogMark = _fogTicks; break;
            case 4 when _fogTicks-_fogMark >= 3:
                _fogChecks["reveal_map_shows_everything"] = ViewTeam < 0 && _fogOverlay?.Node.Visible == false
                    && enemies.Where(u => u.InsideSite < 0).All(u => _views[u.Id].Root.Visible) && world.RememberedSite(team,enemy.Id) != null;
                _revealMap = false;
                _fogPhase++; _fogMark = _fogTicks; break;
            case 5:
                // The bot scouts within its Scouting gate (Default 50: 1:45) and finds the player's base.
                bool found = world.RememberedSite(enemy.Team,home.Id) != null;
                if (!found && _fogTicks-_fogMark < 6000) break;
                // The bot learns of it at its next think (a second at most).
                if (found && _fogFound < _fogMark) _fogFound = _fogTicks;
                bool written = bot.Journal.Entries.Any(e => e.Kind == AiJournalKind.Scout && e.Text.StartsWith("Found "));
                // The journal writes the finds once the scout heads home or 15 s after the first.
                if (found && !written && _fogTicks-_fogFound < 20*SimWorld.TicksPerSecond) break;
                _fogChecks["bot_sends_a_scout"] = bot.ScoutTrips > 0 && bot.Journal.Entries.Any(e => e.Kind == AiJournalKind.Scout && e.Text.StartsWith("Scout #1"));
                _fogChecks["bot_finds_the_player_base"] = found && bot.Journal.Entries.Any(e => e.Kind == AiJournalKind.Scout && e.Text.StartsWith("Found "));
                _fogChecks["bot_status_reports_what_it_knows"] = bot.Status.Contains("knows ");
                FinishFogSmoke(); _fogPhase++; break;
        }
    }

    private void FinishFogSmoke()
    {
        bool passed = _fogChecks.Count >= 18 && _fogChecks.Values.All(v => v);
        GD.Print("FOG_SMOKE "+JsonSerializer.Serialize(new { passed,tick = _world!.Tick,checks = _fogChecks,
            bot = _world.AiPlayers.Values.Select(a => a.Status).ToArray(),
            journal = _world.AiPlayers.Values.SelectMany(a => a.Journal.Entries.Where(e => e.Kind is AiJournalKind.Scout or AiJournalKind.Attack).Select(e => $"{e.Tick/20/60}:{e.Tick/20%60:00} {e.Text}")).ToArray() }));
        _fogPhase = 99;
        _ = FinishCombatAudio(passed ? 0 : 1);
    }
}
