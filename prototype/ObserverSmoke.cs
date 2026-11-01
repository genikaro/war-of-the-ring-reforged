using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Observer mode end to end: on Bilbo's Bluffs Gimli's bot against Aragorn's (--observe --bots=Gimli,Aragorn),
/// or a free-for-all of one bot per start point on any imported map (-Map MP04_Wasteland -Bots a,b,c,d), watched by
/// nobody's side. Checks the spectator HUD (the top-right overview's tabs, fold, unit counts, end-of-match stats and player names, and
/// the console's match panel with its speed slider and pause button), read-only inspection of another side, that no
/// player command is ever issued, the speed keys, the bots' decision journals (build-148), and a fast-forwarded match
/// to its result.</summary>
public partial class Main
{
    private bool _observerSmoke;
    private readonly Dictionary<string,bool> _observerChecks = [];
    private int _observerPhase,_observerStep,_observerJournalStep,_observerTickMark,_observerFrameMark,_observerBurst,_observerInspected = -1,_observerSnapshotTick,_observerCaptures;
    private bool _observerOk = true; // the running result of a check spread over several frames
    private const int ObserverSmokeChecks = 19;
    private readonly List<string> _observerTimeline = [];
    /// <summary>A report key per bot: the player as well as the bot, so a mirror match (Gimli against Gimli) keeps both.</summary>
    private static string BotKey(AiPlayer bot) => $"Player {bot.Team+1} {bot.Personality.Id}";

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
                    && world.AiPlayers.Values.All(a => _bots.ElementAtOrDefault(a.Team) is not { Length: > 0 } bot
                        || AiRoster.Parse(bot) is var choice && string.Equals(a.Personality.Id,choice.Id,StringComparison.OrdinalIgnoreCase) && a.Traits.SequenceEqual(choice.Traits));
                _observerChecks["nobody_plays"] = PlayerTeam == ObserverTeam && _commands.PlayerTeam == ObserverTeam && FriendlyHeroes().Count == 0;
                // Build-49: each name's tooltip gives the bot's difficulty and style.
                _observerChecks["observer_panel_lists_every_player"] = _observerHud.Visible && _observerHud.BodyVisible && _observerHud.RowCount == world.Contenders.Count
                    && world.AiPlayers.Values.Select((a,i) => _observerHud.RowName(i) == a.Personality.Id
                        && _observerHud.NameCell(i).TooltipText.Contains(AiRoster.Summary(a.Personality.Difficulty,a.Profile.Style))
                        && (a.Traits.Length == 0 || _observerHud.NameCell(i).TooltipText.Contains("Traits: "+AiRoster.TraitList(a.Traits)))).All(v => v);
                // The action grid shows the match: the map without the mode suffix, the clock, no groups or pace button.
                int seconds = world.Tick/SimWorld.TicksPerSecond;
                _observerChecks["match_panel_replaces_the_commands"] = _commands.MatchVisible && _mapName != "" && _commands.DisplayedMap == _mapName
                    && !_mapName.Contains("Skirmish") && _commands.DisplayedClock == $"{seconds/60}:{seconds%60:00}" && _commands.CommandPaletteEmpty
                    && !_commands.GroupButton(0).IsVisibleInTree() && !_commands.WalkRunButton.IsVisibleInTree()
                    && _commands.DisplayedMatchState == (world.Contenders.Count == 2 ? "Duel" : $"Free-for-all · {world.Contenders.Count} players");
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
                if (_observerStep == 4) PushKey(Key.H); // Hold ground; S opens the Stats tab while observing.
                if (_observerStep < 6) break;
                _observerChecks["observer_inspects_either_side_read_only"] = _selected.SetEquals([_observerInspected])
                    && !_commands.ButtonFor(OrderKind.Attack).Visible && _commands.DisplayedOrder.StartsWith("Player 2 · Bot ");
                _observerPhase = 2; _observerStep = 0; break;
            case 2:
                // The overview's tabs and fold, clicked like a person would: press and release on separate frames.
                var units = _observerHud.TabFor(ObserverHud.Page.Units).GetGlobalRect().GetCenter();
                var fold = _observerHud.FoldButton.GetGlobalRect().GetCenter();
                if (_observerStep == 0) Press(units);
                if (_observerStep == 1) Release(units);
                if (_observerStep == 3)
                {
                    var teams = world.Contenders.ToArray();
                    _observerChecks["units_tab_counts_every_unit"] = _observerHud.Tab == ObserverHud.Page.Units && _observerHud.BodyVisible
                        && teams.Select((team,i) => _observerHud.DisplayedUnitCounts(i).Sum() == world.Units.Count(u => u.Team == team && u.Health > 0)
                            && _observerHud.RowName(i) == world.AiPlayers[team].Personality.Id).All(v => v);
                    PushKey(Key.E);
                }
                if (_observerStep == 4) { _observerOk &= _observerHud.Tab == ObserverHud.Page.Economy && !_observerHud.Collapsed; Press(fold); }
                if (_observerStep == 5) Release(fold);
                if (_observerStep == 7) { _observerOk &= _observerHud.Collapsed && !_observerHud.BodyVisible; PushKey(Key.E); }
                if (_observerStep == 8) { _observerOk &= !_observerHud.Collapsed && _observerHud.Tab == ObserverHud.Page.Economy; PushKey(Key.E); }
                if (_observerStep == 9) { _observerOk &= _observerHud.Collapsed; PushKey(Key.Tab); }
                if (_observerStep == 10) Click(_observerHud.NameCell(0).GetGlobalRect().GetCenter());
                if (++_observerStep < 12) break;
                _observerChecks["observer_tabs_switch_and_fold"] = _observerOk && !_observerHud.Collapsed && _observerHud.BodyVisible;
                var home = world.ConstructionSites.Values.First(s => s.Team == world.Contenders.Min && s.Prebuilt);
                var (homeX,homeZ) = world.SitePosition(home); var homeAt = PositionOf(homeX,homeZ);
                _observerChecks["player_name_centres_on_base"] = new Vector2(_focus.X,_focus.Z).DistanceTo(new Vector2(homeAt.X,homeAt.Z)) < 1;
                _observerPhase = 3; _observerStep = 0; break;
            case 3:
                // The speed slider and pause button, then the keys.
                var sixteen = _commands.SpeedSlider.NotchGlobal(Speeds.Length-1);
                var pause = _commands.PauseButton.GetGlobalRect().GetCenter();
                if (_observerStep == 0) Press(sixteen);
                if (_observerStep == 1) Release(sixteen);
                if (_observerStep == 3) { _observerTickMark = world.Tick; _observerFrameMark = _frames; }
                if (_observerStep == 13)
                {
                    _observerBurst = (world.Tick-_observerTickMark)/Math.Max(1,_frames-_observerFrameMark);
                    _observerChecks["speed_slider_runs_sixteen_ticks_a_frame"] = _speed == 16 && _commands.SpeedSlider.Index == Speeds.Length-1 && _observerBurst >= 12;
                    Press(pause);
                }
                if (_observerStep == 14) Release(pause);
                if (_observerStep == 16) { _observerTickMark = world.Tick; _observerOk = _paused && _commands.PauseButton.ButtonPressed && _commands.DisplayedMatchState.StartsWith("Paused"); }
                if (_observerStep == 18) { _observerOk &= world.Tick == _observerTickMark; Press(pause); }
                if (_observerStep == 19) Release(pause);
                if (_observerStep == 21)
                {
                    _observerChecks["pause_button_pauses_and_resumes"] = _observerOk && !_paused && !_commands.PauseButton.ButtonPressed;
                    PushKey(Key.Minus);
                }
                if (++_observerStep < 23) break;
                _observerChecks["minus_key_halves_the_speed"] = _speed == 8 && _commands.SpeedSlider.Index == Speeds.Length-2;
                PushKey(Key.Equal);
                _observerPhase = 4; _observerStep = 0; break;
            case 4:
                // 40 game minutes for a duel, 20 more per extra player.
                if (!world.MatchOver && world.Tick < 48000+24000*(world.Contenders.Count-2))
                {
                    if (_observerJournalStep <= 12 && _observerCaptures >= 3 && world.Tick >= 15*60*SimWorld.TicksPerSecond) { ObserverJournalSmoke(world); break; }
                    if (++_observerStep == 2 && _capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-observer.png");
                    // Mid-game frames of every tab, for reviewing the spectator HUD with armies on the field.
                    if (_capture != "" && _observerCaptures < 3 && world.Tick >= (10+2*_observerCaptures)*60*SimWorld.TicksPerSecond)
                    {
                        _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+new[] {"-economy.png","-units.png","-stats.png"}[_observerCaptures++]);
                        // The next capture's tab, chosen now: the overview redraws a frame after a switch.
                        if (_observerCaptures < 3) _observerHud.Choose(_observerCaptures == 1 ? ObserverHud.Page.Units : ObserverHud.Page.Stats);
                    }
                    break;
                }
                _observerChecks.TryAdd("journal_buttons_open_switch_and_close",false); // The match ended before 15:00.
                // The Stats tab, checked once the match has its kills and losses (a capture may have left it open already).
                if (_observerHud.Tab == ObserverHud.Page.Stats && !_observerHud.Collapsed) _observerHud.Choose(ObserverHud.Page.Economy);
                PushKey(Key.S);
                _observerChecks["fast_forward_reaches_a_result"] = world.MatchOver && world.Winner >= 0 && _speed == 16;
                _observerChecks["observer_result_names_the_winner"] = _outcome.Visible && _outcomeTitle.Text == $"Player {world.Winner+1} wins!"
                    && _commands.DisplayedMatchState == $"Paused · Player {world.Winner+1} · {world.AiPlayers[world.Winner].Personality.Id} wins";
                // Every bot order goes straight into the simulation; nothing came from the keyboard or mouse.
                _observerChecks["observer_never_issued_a_command"] = world.Commands.Count == 0;
                // Bodies leave a minute after death: heavy children and managed view references are released.
                var gone = _views.Where(p => p.Value.Buried).ToArray();
                _observerChecks["corpses_leave_after_a_minute"] = gone.Length > 0
                    && gone.All(p => !p.Value.Root.Visible && p.Value.Root.ProcessMode == Node.ProcessModeEnum.Disabled && world.Units[p.Key].Health <= 0
                        && p.Value.Root.GetChildCount() == 1 && p.Value.Ring.Mesh == null && p.Value.Player == null
                        && p.Value.Appearance == null && p.Value.Health == null && p.Value.Ghost == null && p.Value.HeroGlow == null
                        && world.Tick-p.Value.DeathTick >= CorpseTicks)
                    // A unit killed on the final tick has no presented death yet (DeathTick -1).
                    && _views.Where(p => !p.Value.Buried && world.Units[p.Key].Health <= 0 && p.Value.DeathTick >= 0)
                        .All(p => world.Tick-p.Value.DeathTick < CorpseTicks+CorpseSinkTicks+SimWorld.TicksPerSecond);
                // Build-76: battle earns both sides Fate (bots do not spend it), within the source cap of 20.
                _observerChecks["bots_earn_fate_in_battle"] = world.Contenders.All(t => world.Economies[t] is var bank
                    && (bank.Fate > 0 || bank.FateProgressMicro > 0) && bank.Fate == bank.FateEarned && bank.Fate <= world.MaxFate);
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-result.png");
                _observerPhase = 5; _observerStep = 0; break;
            case 5:
                if (++_observerStep < 4) break; // Let the capture finish writing.
                // Build-55: each row shows its side's gathered totals and record; every fallen unit is someone's loss.
                var contenders = world.Contenders.ToArray();
                var records = contenders.Select(t => world.Records.GetValueOrDefault(t) ?? MatchRecord.Empty).ToArray();
                _observerChecks["stats_tab_totals_gathering_kills_and_losses"] = _observerHud.Tab == ObserverHud.Page.Stats && _observerHud.BodyVisible
                    && contenders.Select((team,i) => _observerHud.DisplayedStats(i).SequenceEqual([(int)(world.Economies[team].GatheredFoodMilli/1000),
                        (int)(world.Economies[team].GatheredOreMilli/1000),records[i].UnitKills.Values.Sum(),records[i].UnitLosses.Values.Sum()])).All(v => v)
                    && world.Records.Values.Sum(r => r.UnitLosses.Values.Sum()) == world.Units.Count(u => u.Health <= 0)
                    && world.Records.Values.Sum(r => r.UnitKills.Values.Sum()) is int kills && kills > 0 && kills <= world.Units.Count(u => u.Health <= 0);
                // Build-148: each bot's journal holds its macro decisions in order, one line per attack, raid and place squad it sent.
                bool Sent(AiJournalEntry entry,AiJournalKind kind,string word) => entry.Kind == kind
                    && System.Text.RegularExpressions.Regex.IsMatch(entry.Text,$"^{word} #[0-9]+: [0-9]+ units ");
                _observerChecks["bot_journals_log_macro_decisions"] = world.AiPlayers.Values.All(a => a.Journal.Entries is var entries
                    && entries.Count == a.Journal.Written && entries.Zip(entries.Skip(1)).All(p => p.First.Tick <= p.Second.Tick)
                    && entries.Any(e => e.Kind == AiJournalKind.Build) && (a.Composition != null) == entries.Any(e => e.Text.StartsWith("Army plan: "))
                    && entries.Count(e => Sent(e,AiJournalKind.Attack,"Attack")) == a.PartiesSent && entries.Count(e => Sent(e,AiJournalKind.Raid,"Raid")) == a.RaidsSent
                    && entries.Count(e => Sent(e,AiJournalKind.Place,"Squad")) == a.PlaceSquadsSent);
                var report = new { build = BuildInfo.Label,checks = _observerChecks,
                    passed = _observerChecks.Count == ObserverSmokeChecks && _observerChecks.Values.All(v => v),
                    fixture = $"{System.IO.Path.GetFileName(_cache)} --observe --bots={string.Join(',',_bots)}; the match runs at 16x after the speed checks",
                    timeline = _observerTimeline,
                    // Every unit each bot fielded, fallen included, and its research: how far up the tree it climbed (build-47).
                    armies = world.AiPlayers.Values.ToDictionary(BotKey,a => string.Join(",",world.Units.Where(u => u.Team == a.Team && !world.Workers.ContainsKey(u.Definition.Name))
                        .GroupBy(u => u.Definition.DisplayName).OrderBy(g => g.Key,StringComparer.Ordinal).Select(g => g.Key+" "+g.Count()))),
                    research = world.AiPlayers.Values.ToDictionary(BotKey,a => string.Join(",",(world.Researched.GetValueOrDefault(a.Team) ?? []).Order(StringComparer.Ordinal))),
                    // Places of power (build-54): who took which, when, and the squads each bot sent.
                    places = _placeLog,place_squads = world.AiPlayers.Values.ToDictionary(BotKey,a => a.PlaceSquadsSent),
                    // The Stats tab's numbers (build-55).
                    stats = world.AiPlayers.Values.ToDictionary(BotKey,a => {
                        var r = world.Records.GetValueOrDefault(a.Team) ?? MatchRecord.Empty; var bank = world.Economies[a.Team];
                        return $"gathered food {bank.GatheredFoodMilli/1000} ore {bank.GatheredOreMilli/1000} · units killed {r.UnitKills.Values.Sum()} lost {r.UnitLosses.Values.Sum()}"
                            +$" · buildings razed {r.BuildingKills.Values.Sum()} lost {r.BuildingLosses.Values.Sum()}"
                            +$" · Fate {bank.Fate} and {world.FateProgressPercent(a.Team)}%";
                    }),
                    corpses_buried = _corpsesBuried,winner = world.Winner,outcome_tick = world.OutcomeTick,ticks_per_frame_at_16x = _observerBurst,
                    bots = world.AiPlayers.Values.Select(a => $"{a.Personality.Id}: {a.Status}").ToArray(),
                    journals = world.AiPlayers.Values.ToDictionary(BotKey,a => a.Journal.Entries.Select(e =>
                        $"{TimeSpan.FromSeconds(e.Tick/SimWorld.TicksPerSecond):mm\\:ss} {e.Kind} {e.Text}").ToArray()),
                    ticks = world.Tick,status = _status.Text,digest = world.StateDigest() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"observer-smoke-report.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("OBSERVER_SMOKE "+JsonSerializer.Serialize(report)); _observerPhase = 6;
                _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }

    /// <summary>Build-148, mid-match: the first bot's journal button opens its decision journal, newest first, the second
    /// bot's switches to that bot, and the cross closes it. The match waits paused meanwhile, so view and journal agree.</summary>
    private void ObserverJournalSmoke(SimWorld world)
    {
        var teams = world.Contenders.ToArray();
        bool Shows(int team) => world.AiPlayers[team].Journal.Entries is var entries && entries.Count > 0 && _observerHud.JournalTeam == team
            && _observerHud.JournalVisible && _observerHud.JournalLines.Count == entries.Count && _observerHud.JournalLines[0].EndsWith(entries[^1].Text);
        Vector2 At(Control control) => control.GetGlobalRect().GetCenter();
        switch (_observerJournalStep++)
        {
            case 0: SetPaused(true); break;
            case 1: Press(At(_observerHud.JournalButton(0))); break;
            case 2: Release(At(_observerHud.JournalButton(0))); break;
            case 4:
                _observerOk = Shows(teams[0]) && _observerHud.JournalButton(0).ButtonPressed;
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,null)+"-journal.png");
                break;
            case 6: Press(At(_observerHud.JournalButton(1))); break;
            case 7: Release(At(_observerHud.JournalButton(1))); break;
            case 9: _observerOk &= Shows(teams[1]) && _observerHud.JournalButton(1).ButtonPressed && !_observerHud.JournalButton(0).ButtonPressed; break;
            case 10: Press(At(_observerHud.JournalCloseButton)); break;
            case 11: Release(At(_observerHud.JournalCloseButton)); break;
            case 12:
                _observerChecks["journal_buttons_open_switch_and_close"] = _observerOk && _observerHud.JournalTeam < 0 && !_observerHud.JournalVisible;
                SetPaused(false); break;
        }
    }
}
