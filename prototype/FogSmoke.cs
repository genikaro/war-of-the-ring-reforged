using Godot;
using Reforged.Rendering;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Build-150 fog of war on Bilbo's Bluffs, you against the Default bot (run.ps1 -FogSmoke [-CapturePath x.png]):
/// at the start the enemy base is hidden in the world and on the minimap and in haze; a worker walks over and finds it;
/// walked home, the enemy's units vanish while its Citadel stays as last seen; Reveal map shows everything; and the bot
/// sends a scout that finds your base. Captures -found.png and -memory.png beside the given path. With -Observe
/// (build-151) it watches two bots instead: the view starts on player 1's base and every player's sight together, haze between the bases and
/// every unit shown; V shows player 1's sight, then player 2's, then no fog, then all players again, the console's Sight
/// list following, and choosing player 2 in the list shows its sight. Captures
/// -observer.png and -observer-p1.png.</summary>
public partial class Main
{
    private bool _fogSmoke;
    private readonly Dictionary<string,bool> _fogChecks = [];
    private int _fogPhase,_fogScout = -1,_fogMark,_fogFound = -1;
    private int _fogTicks => _world!.Tick;

    private void FogSmokeInput()
    {
        if (_observing) { ObserverFogSmokeInput(); return; }
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
                // Build-153: Bilbo's Bluffs' own fog colour, at the strength matched to its captures.
                var mapFog = _cacheFiles.Root.GetProperty("lighting").GetProperty("fog_of_war");
                var mapColor = mapFog.GetProperty("color");
                _fogChecks["haze_takes_the_maps_fog_colour"] = _fogOverlay!.Haze.IsEqualApprox(new Color(mapColor[0].GetSingle(),mapColor[1].GetSingle(),mapColor[2].GetSingle()))
                    && Math.Abs(_fogOverlay.Strength-FogOverlay.StrengthFor(mapFog.GetProperty("alpha").GetSingle())) < .001f
                    && Math.Abs(FogOverlay.StrengthFor(FogOverlay.MatchedAlpha)-FogOverlay.DefaultStrength) < .001f;
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

    private void ObserverFogSmokeInput()
    {
        var world = _world!;
        var sides = world.Contenders.ToArray();
        var bases = sides.Select(t => world.SitePosition(world.ConstructionSites.Values.First(s => s.Team == t && s.Prebuilt))).ToArray();
        var (mx,mz) = ((bases[0].X+bases[1].X)/2,(bases[0].Z+bases[1].Z)/2);
        string Capture(string suffix) => System.IO.Path.ChangeExtension(_capture,null)+suffix;
        void Look(int x,int z) { _focus = PositionOf(x,z); UpdateCamera(); }
        bool AllShown() => world.Units.Where(u => u.Present).All(u => _views[u.Id].Root.Visible)
            && _constructionViews.All(v => !world.ConstructionSites.TryGetValue(v.Key,out var site) || site.Team < 0 || v.Value.Root.Visible);
        switch (_fogPhase)
        {
            case 0 when _fogTicks >= 20:
                _fogChecks["observer_starts_on_every_players_sight"] = HazeTeams.SequenceEqual(sides) && ViewTeam < 0 && _fogOverlay?.Node.Visible == true
                    && _commands.DisplayedSight == "All players" && _commands.SightList.IsVisibleInTree()
                    && _commands.SightList.ItemCount == sides.Length+2 && _commands.SightList.GetItemText(sides.Length+1) == "No fog";
                _fogChecks["haze_between_the_bases_not_on_them"] = bases.All(b => _fogOverlay!.Shown(b.X,b.Z) == 1) && _fogOverlay!.Shown(mx,mz) == 0;
                _fogChecks["minimap_grey_between_the_bases"] = bases.All(b => _commands.Minimap.ColouredAt(b.X,b.Z)) && !_commands.Minimap.ColouredAt(mx,mz);
                _fogChecks["every_unit_and_building_shown"] = AllShown() && _commands.Minimap.Buildings().Select(b => b.Team).Distinct().Count() == sides.Length;
                // The match opens on player 1's base at a player's zoom, not the whole map from far above.
                var start = PositionOf(bases[0].X,bases[0].Z);
                _fogChecks["observer_starts_on_player_one_base"] = new Vector2(_focus.X-start.X,_focus.Z-start.Z).Length() < 10 && _distance <= 60;
                if (_capture != "") _ = CaptureFrame(false,Capture("-observer.png"));
                _fogPhase++; _fogMark = _fogTicks; break;
            case 1 when _fogTicks-_fogMark >= 10:
                PushKey(Key.V); _fogPhase++; _fogMark = _fogTicks; break;
            case 2 when _fogTicks-_fogMark >= 3:
                var enemies = world.Units.Where(u => u.Present && u.Team == sides[1]).ToArray();
                var enemyBase = world.ConstructionSites.Values.First(s => s.Team == sides[1] && s.Prebuilt);
                _fogChecks["v_shows_player_one_sight"] = _visionTeam == sides[0] && ViewTeam == sides[0] && HazeTeams.SequenceEqual([sides[0]])
                    && _commands.DisplayedSight.EndsWith($"(P{sides[0]+1})");
                _fogChecks["player_one_sight_hides_the_enemy"] = enemies.Length > 0 && enemies.All(u => !_views[u.Id].Root.Visible)
                    && !_constructionViews[enemyBase.Id].Root.Visible && _fogOverlay!.Shown(bases[1].X,bases[1].Z) == 0 && _fogOverlay.Shown(bases[0].X,bases[0].Z) == 1
                    && !_commands.Minimap.ColouredAt(bases[1].X,bases[1].Z) && _commands.Minimap.Buildings().All(b => b.Team == sides[0]);
                Look(bases[0].X,bases[0].Z);
                if (_capture != "") _ = CaptureFrame(false,Capture("-observer-p1.png"));
                _fogPhase++; _fogMark = _fogTicks; break;
            case 3 when _fogTicks-_fogMark >= 10:
                PushKey(Key.V); _fogPhase++; _fogMark = _fogTicks; break;
            case 4 when _fogTicks-_fogMark >= 3:
                _fogChecks["v_shows_player_two_sight"] = _visionTeam == sides[1] && HazeTeams.SequenceEqual([sides[1]])
                    && _fogOverlay!.Shown(bases[0].X,bases[0].Z) == 0 && _fogOverlay.Shown(bases[1].X,bases[1].Z) == 1;
                PushKey(Key.V); _fogPhase++; _fogMark = _fogTicks; break;
            case 5 when _fogTicks-_fogMark >= 3:
                _fogChecks["v_then_shows_no_fog"] = HazeTeams.Length == 0 && _fogOverlay?.Node.Visible == false && _commands.DisplayedSight == "No fog"
                    && AllShown() && _commands.Minimap.ColouredAt(mx,mz);
                PushKey(Key.V); _fogPhase++; _fogMark = _fogTicks; break;
            case 6 when _fogTicks-_fogMark >= 3:
                _fogChecks["v_returns_to_all_players"] = HazeTeams.SequenceEqual(sides) && _fogOverlay?.Node.Visible == true && _commands.DisplayedSight == "All players";
                _fogChecks["simulation_keeps_fog_for_the_bots"] = world.FogOfWar && world.AiPlayers.Count == sides.Length
                    && sides.All(t => world.RememberedSite(t,world.ConstructionSites.Values.First(s => s.Team != t && s.Team >= 0 && s.Prebuilt).Id) == null);
                // The console's Sight list, as a click on its item does: player 2's sight.
                _commands.SightList.Select(2); _commands.SightList.EmitSignal(OptionButton.SignalName.ItemSelected,2L);
                _fogPhase++; _fogMark = _fogTicks; break;
            case 7 when _fogTicks-_fogMark >= 3:
                _fogChecks["sight_list_chooses_player_two"] = _visionTeam == sides[1] && HazeTeams.SequenceEqual([sides[1]])
                    && _commands.DisplayedSight.EndsWith($"(P{sides[1]+1})") && _fogOverlay!.Shown(bases[0].X,bases[0].Z) == 0;
                Look(bases[1].X,bases[1].Z);
                if (_capture != "") _ = CaptureFrame(false,Capture("-observer-p2.png"));
                _fogPhase++; _fogMark = _fogTicks; break;
            case 8 when _fogTicks-_fogMark >= 10:
                FinishFogSmoke(); break;
        }
    }

    private void FinishFogSmoke()
    {
        bool passed = _fogChecks.Count >= (_observing ? 12 : 19) && _fogChecks.Values.All(v => v);
        GD.Print("FOG_SMOKE "+JsonSerializer.Serialize(new { passed,tick = _world!.Tick,checks = _fogChecks,
            bot = _world.AiPlayers.Values.Select(a => a.Status).ToArray(),
            journal = _world.AiPlayers.Values.SelectMany(a => a.Journal.Entries.Where(e => e.Kind is AiJournalKind.Scout or AiJournalKind.Attack).Select(e => $"{e.Tick/20/60}:{e.Tick/20%60:00} {e.Text}")).ToArray() }));
        _fogPhase = 99;
        _ = FinishCombatAudio(passed ? 0 : 1);
    }
}
