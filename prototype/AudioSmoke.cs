using Godot;
using Reforged.Simulation;
using Reforged.Presentation;
using System.Text.Json;

public partial class Main
{
    private bool _audioSmoke;
    private readonly Dictionary<string,bool> _audioChecks = [];
    private int _audioSmokeSpeech;
    private string _audioSmokeDigest = "";
    private string _audioSmokeTrack = "";
    private ulong _audioEndDeadline;
    private static bool AudioVolume(string bus,double percent) => percent <= 0
        ? AudioServer.IsBusMute(AudioServer.GetBusIndex(bus))
        : !AudioServer.IsBusMute(AudioServer.GetBusIndex(bus)) && Math.Abs(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(bus)))-percent/100) < .001;

    private void AudioControlHookChecks()
    {
        // Hook a populated subtree before it enters the tree, then let the already-hooked HUD
        // discover it again. Recursive traversal and child-entry signals visit the same nodes.
        var branch = new Control { Name = "AudioHookRegression",Visible = false };
        var group = new Control(); var button = new Button(); var check = new CheckBox();
        branch.AddChild(group); group.AddChild(button); group.AddChild(check);
        _audio.HookControls(branch); _audio.HookControls(branch); _hud.AddChild(branch);
        _audio.HookControls(_hud);
        bool Single(Node node,StringName signal) => node.GetSignalConnectionList(signal).Count == 1;
        _audioChecks["audio_control_tree_watcher_connects_once"] = new Node[]{branch,group,button,check}
            .All(node => Single(node,Node.SignalName.ChildEnteredTree));
        int effects = _audio.EffectsPlayed;
        button.EmitSignal(BaseButton.SignalName.ButtonDown);
        _audioChecks["repeated_audio_hook_plays_one_button_sound"] = _audio.EffectsPlayed == effects+1
            && Single(button,BaseButton.SignalName.ButtonDown);
        effects = _audio.EffectsPlayed; check.ButtonPressed = true;
        _audioChecks["repeated_audio_hook_plays_one_checkbox_sound"] = _audio.EffectsPlayed == effects+1
            && Single(check,BaseButton.SignalName.Toggled);
        branch.RemoveChild(group); branch.AddChild(group);
        effects = _audio.EffectsPlayed; button.EmitSignal(BaseButton.SignalName.ButtonDown);
        _audioChecks["audio_hook_survives_subtree_reentry"] = _audio.EffectsPlayed == effects+1
            && Single(group,Node.SignalName.ChildEnteredTree) && Single(button,BaseButton.SignalName.ButtonDown);
        group.RemoveChild(button); branch.AddChild(button);
        effects = _audio.EffectsPlayed; button.EmitSignal(BaseButton.SignalName.ButtonDown);
        _audioChecks["audio_hook_survives_button_reparenting"] = _audio.EffectsPlayed == effects+1;
        var added = new Button(); group.AddChild(added);
        effects = _audio.EffectsPlayed; added.EmitSignal(BaseButton.SignalName.ButtonDown);
        _audioChecks["audio_hook_discovers_new_controls"] = _audio.EffectsPlayed == effects+1
            && Single(added,Node.SignalName.ChildEnteredTree);
        branch.QueueFree();
    }

    private void AudioSmokeInput()
    {
        switch (_frames)
        {
            case 4:
                AudioControlHookChecks();
                _audioChecks["debug_session_mute_leaves_automated_runs_alone"] = !GameAudio.SessionMuted
                    && !AudioServer.IsBusMute(AudioServer.GetBusIndex(GameAudio.MasterBus));
                _audioChecks["original_audio_loaded_and_music_playing"] = _audio.Loaded && _audio.MusicPlaying;
                _audioChecks["human_and_uruk_selection_move_attack_speech"] = new[]{"UNIT_G_HUMAN_WORKER","UNIT_G_HERO_ARAGORN","UNIT_E_URUK_HAI"}
                    .Where(name => _rules.Units.ContainsKey(name)).All(name => new[]{0,1,4}.All(kind => _audio.HasSpeech(name,kind)));
                _audioChecks["all_existing_combat_players_use_effects_bus"] = _impactPlayers.Concat(_launchPlayers).All(p => p.Bus == GameAudio.EffectsBus);
                OpenGameMenu(); _gameMenu.ShowSettings(); _gameMenu.TabButton(3).EmitSignal(BaseButton.SignalName.Pressed);
                break;
            case 6:
                _audioChecks["audio_tab_visible"] = _gameMenu.VolumeSlider(0).IsVisibleInTree() && _gameMenu.VoicePreviewButton.IsVisibleInTree();
                _gameMenu.VolumeSlider(0).Value = 25; _gameMenu.VolumeSlider(1).Value = 0;
                _gameMenu.VolumeSlider(2).Value = 35; _gameMenu.VolumeSlider(3).Value = 65;
                _audioChecks["mix_previews_without_committing"] = AudioVolume(GameAudio.MasterBus,25) && AudioVolume(GameAudio.MusicBus,0)
                    && AudioVolume(GameAudio.EffectsBus,35) && AudioVolume(GameAudio.VoiceBus,65) && _settings.MasterVolume == 80;
                _gameMenu.MuteBox.ButtonPressed = true;
                _audioChecks["master_mute"] = AudioServer.IsBusMute(AudioServer.GetBusIndex(GameAudio.MasterBus));
                _gameMenu.CancelButton.EmitSignal(BaseButton.SignalName.Pressed);
                _audioChecks["cancel_restores_mix"] = AudioVolume(GameAudio.MasterBus,80) && AudioVolume(GameAudio.MusicBus,45)
                    && AudioVolume(GameAudio.EffectsBus,80) && AudioVolume(GameAudio.VoiceBus,90);
                break;
            case 5:
                if (_capture != "") _ = CaptureFrame(false,System.IO.Path.ChangeExtension(_capture,"audio-settings.png"));
                break;
            case 8:
                _gameMenu.ShowSettings(); _gameMenu.TabButton(3).EmitSignal(BaseButton.SignalName.Pressed);
                _gameMenu.VolumeSlider(0).Value = 73; _gameMenu.VolumeSlider(1).Value = 32;
                _gameMenu.VolumeSlider(2).Value = 44; _gameMenu.VolumeSlider(3).Value = 66;
                _gameMenu.ApplyButton.EmitSignal(BaseButton.SignalName.Pressed);
                _audioChecks["apply_commits_mix"] = _settings.MasterVolume == 73 && _settings.MusicVolume == 32
                    && _settings.EffectsVolume == 44 && _settings.VoiceVolume == 66;
                string path = "user://audio-smoke-"+Guid.NewGuid()+".cfg";
                _settings.Save(path); var loaded = GameSettings.Load(path);
                _audioChecks["mix_round_trips_through_settings_file"] = loaded.MasterVolume == 73 && loaded.MusicVolume == 32
                    && loaded.EffectsVolume == 44 && loaded.VoiceVolume == 66 && !loaded.Muted;
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
                var bounded = new GameSettings {MasterVolume = -1,MusicVolume = 200,EffectsVolume = double.NaN,VoiceVolume = double.PositiveInfinity};
                _audioChecks["invalid_volumes_are_bounded"] = bounded.MasterVolume == 0 && bounded.MusicVolume == 100 && bounded.EffectsVolume == 80 && bounded.VoiceVolume == 90;
                _gameMenu.VolumeSlider(0).Value = 0; _gameMenu.Back();
                _audioChecks["escape_restores_last_applied_mix"] = AudioVolume(GameAudio.MasterBus,73);
                CloseGameMenu(); SetPaused(true);
                _selected.Clear(); _selected.Add(_world!.Units.First(u => u.Team == PlayerTeam).Id);
                _audioSmokeSpeech = _audio.SpeechPlayed; SelectionSpeech();
                _audioChecks["selection_speaks_once_for_group"] = _audio.SpeechPlayed == _audioSmokeSpeech+1 && _audio.LastSpeechEvent == 4;
                _audioSmokeSpeech = _audio.SpeechPlayed; SelectionSpeech();
                _audioChecks["rapid_selection_is_throttled"] = _audio.SpeechPlayed == _audioSmokeSpeech;
                break;
            case 24:
                _audioSmokeSpeech = _audio.SpeechPlayed;
                SendOrder(new(OrderKind.Move,_world!.Units[_selected.First()].Node)); PresentOrderSpeech();
                _audioChecks["move_command_uses_move_speech"] = _audio.SpeechPlayed == _audioSmokeSpeech+1 && _audio.LastSpeechEvent == 1;
                break;
            case 40:
                _audioSmokeSpeech = _audio.SpeechPlayed;
                SendOrder(new(OrderKind.AttackMove,_world!.Units[_selected.First()].Node)); PresentOrderSpeech();
                _audioChecks["attack_command_uses_attack_speech"] = _audio.SpeechPlayed == _audioSmokeSpeech+1 && _audio.LastSpeechEvent == 0;
                _audioSmokeDigest = _world.StateDigest();
                _audioSmokeTrack = _audio.MusicTrackName;
                double musicPosition = _audio.MusicPosition;
                var events = _world.CombatEvents.ToArray(); _world.CombatEvents.Clear();
                _world.CombatEvents.Add(new(_world.Tick,CombatEventKind.Start,_selected.First(),
                    _world.Units.First(u => u.Team != PlayerTeam).Id,0,0,0,_world.Tick,0,0,0));
                PresentWorldAudio(); _world.CombatEvents.Clear(); _world.CombatEvents.AddRange(events);
                _audioChecks["combat_keeps_faction_track"] = _audio.MusicContext == MusicContext.Good && _audio.MusicPlaying
                    && _audio.MusicTransitions == 1 && _audio.MusicTrackName == _audioSmokeTrack;
                _audioChecks["combat_does_not_restart_music"] = Math.Abs(_audio.MusicPosition-musicPosition) < .25;
                _audio.Speak(_world.Units[_selected.First()].Definition.Name,3,preview:true);
                _audioChecks["death_speech_decodes_and_plays"] = _audio.LastSpeechEvent == 3;
                _audioChecks["audio_does_not_change_simulation"] = _world.StateDigest() == _audioSmokeDigest;
                _audioChecks["sound_effect_decodes_and_plays"] = _audio.Effect(276);
                _audioChecks["world_effects_pause_but_ui_remains_available"] = !_audio.Effect(148,Vector3.Zero) && _audio.MusicPlaying;
                _gameMenu.Open(_commands.Art,_settings,_commands.ScalePercent,false); _gameMenu.ShowSettings();
                _gameMenu.ResetButton.EmitSignal(BaseButton.SignalName.Pressed);
                _audioChecks["reset_previews_default_mix"] = AudioVolume(GameAudio.MasterBus,80) && AudioVolume(GameAudio.MusicBus,45);
                _gameMenu.Close();
                _audioChecks["closing_unapplied_settings_restores_mix"] = AudioVolume(GameAudio.MasterBus,73);
                _selected.Clear(); _audioSmokeSpeech = _audio.SpeechPlayed;
                _gameMenu.VoicePreviewButton.EmitSignal(BaseButton.SignalName.Pressed);
                _audioChecks["speech_test_works_without_a_selection"] = _audio.SpeechPlayed == _audioSmokeSpeech+1 && _audio.LastSpeechEvent == 4;
                break;
            case 44:
                _audio.SetWorldPaused(false); break;
            case 305:
                _audioChecks["quiet_keeps_same_faction_track"] = _audio.MusicContext == MusicContext.Good && _audio.MusicPlaying
                    && _audio.MusicTransitions == 1 && _audio.MusicTrackName == _audioSmokeTrack;
                AudioPlaylistChecks();
                // Seek a real Ogg near its end to verify Godot's natural Finished signal too.
                var player = _audio.GetChildren().OfType<AudioStreamPlayer>().Single(p => p.Bus == GameAudio.MusicBus);
                _audio.SetWorldPaused(true); player.Play((float)(player.Stream.GetLength()-.05));
                _audioEndDeadline = Time.GetTicksMsec()+2000;
                break;
            case 320:
                // Fixed-FPS smoke frames can run faster than the audio mixer clock.
                if (_audio.MusicGapRemaining <= 0 && Time.GetTicksMsec() < _audioEndDeadline) { _frames--; break; }
                _audioChecks["natural_track_end_waits_in_silence"] = !_audio.MusicPlaying && _audio.MusicGapRemaining > 13
                    && _audio.MusicTrackName == _audioSmokeTrack;
                _audio._Process(MusicPlaylist.GapSeconds);
                _audioChecks["next_track_starts_after_gap_while_paused"] = _audio.MusicPlaying && _audio.MusicGapRemaining == 0
                    && _audio.MusicTrackName != _audioSmokeTrack;
                bool passed = _audioChecks.Values.All(value => value);
                GD.Print("AUDIO_SMOKE "+JsonSerializer.Serialize(new {passed,checks=_audioChecks,build=BuildInfo.Label}));
                _ = FinishCombatAudio(passed ? 0 : 1); break;
        }
    }

    private void AudioPlaylistChecks()
    {
        var player = _audio.GetChildren().OfType<AudioStreamPlayer>().Single(p => p.Bus == GameAudio.MusicBus);
        foreach (var context in new[] {MusicContext.Good,MusicContext.Evil,MusicContext.Menu})
        {
            _audio.PlayMusic(context);
            string first = _audio.MusicTrackName; int count = context == MusicContext.Menu ? 1 : 10;
            var heard = new HashSet<string>(); bool passed = true;
            for (int i=0;i<count;i++)
            {
                string name = _audio.MusicTrackName;
                passed &= _audio.MusicPlaying && heard.Add(name);
                // Simulate a completed track through the same signal as natural playback.
                player.Stop(); player.EmitSignal(AudioStreamPlayer.SignalName.Finished);
                _audio._Process(MusicPlaylist.GapSeconds-.1);
                passed &= !_audio.MusicPlaying && _audio.MusicTrackName == name && _audio.MusicGapRemaining > 0;
                _audio._Process(.1);
                passed &= _audio.MusicPlaying && _audio.MusicGapRemaining == 0;
            }
            _audioChecks[context+"_playlist_loads_all_tracks_and_waits_between_them"] = passed
                && heard.Count == count && _audio.MusicTrackName == first;
        }
        _audio.PlayMusic(MusicContext.Good);
        _audioSmokeTrack = _audio.MusicTrackName;
    }
}
