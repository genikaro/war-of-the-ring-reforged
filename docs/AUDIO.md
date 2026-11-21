# Audio — builds 109–120

**Debug sessions start muted (build-142).** A session started from `run.ps1` or the Godot editor (a debug build) mutes the master bus,
in memory only: the Mute setting and the volumes in `user://settings.cfg` are untouched, so a release build plays normally. Smokes and
captures keep their own audio checks. `./prototype/run.ps1 -Skirmish -Unmuted` (or `--unmuted` after `--`) plays the sound.

The original installation supplies the soundtrack, effects and unit speech.
Generated audio lives under `converted/`, stays out of source control and never
modifies the installation or extracted source assets.

In a match, open Menu → Settings → Audio. Master, Music, Sound effects and Unit
speech have independent 0–100% controls; zero silences that channel. Mute all
preserves all four levels. Test unit speech plays the selected friendly unit's
selection line, or another available friendly unit when no unit is selected. Volumes preview
immediately. Apply and OK commit them to `user://settings.cfg`; Cancel, Esc and
closing the menu restore the last applied mix. Reset previews the defaults
(80% master, 45% music, 80% effects, 90% speech) until applied. Older settings
files inherit these audio defaults.

`GameAudio` owns separate buses, sends them through its master bus and reuses
them across scene reloads. Existing ranged launch, impact, spell and level-up
players now use the effects bus too. Scene exit stops every player before
releasing its streams; temporary settings edits do not survive returning to setup.
Since build-112, native nodes record which audio controller attached their UI
handlers. Recursive discovery, tree entry and reparenting can revisit controls
without reconnecting signals, and newly added controls still receive audio.

The exporter reads `SpeechFXEvents`, `SpeechFXEventTypes` and `SpeechFX`, retaining
the source unit/event associations, variant weights, chance and minimum volume.
The shared pack includes available speech for both playable factions, their heroes,
both Beorning forms and the Evil bonus units. Selection, group recall,
portrait selection and submitted move/attack/worker/ability orders have one
foreground speaker, preferring a selected hero. Repeated input is throttled;
weighted variants avoid immediate repeats. Commands can interrupt a selection
response. Refusals, newly trained units, attack yells, damage and deaths use their
respective source events, with source chance and a bounded spatial voice pool.
Observer selection and bot orders do not produce foreground replies. Battlefield
voices are limited to visible units within 50 m of the camera focus.

Since build-222, the menu and setup play the original main menu's cue,
`MUSIC_TRACK_MORDOR_EVIL` (`Mordor_Evil_Build`): Rings.exe's main-menu screen
(0x48d849) adds MusicTracks id 17 to an empty playlist in the normal theme. Builds
120-221 played `MUSIC_TRACK_HOBBIT_MAIN_INTRO_ENDING`, a credits theme the original
unlocks at a Good victory. The menu cue repeats after 15 seconds of silence. A match
plays the normal tracks of the player's faction (Good or Evil, ten each) and the three
Generic tracks, which have no clan, so 13 per side; the menu cue is also Evil music.
They are shuffled: each round plays every track once in a random order, and a track
never plays twice running. Each track finishes in full, followed by a random 30-90
seconds of silence (build-120 to 221: source order, 15 seconds). Combat never switches to a battle variant or seeks
the playing track. Returning to setup restores the menu cue; the in-match menu
keeps the faction music. Playback and quiet intervals continue during pause,
while spatial effects and battlefield voices pause with the world.

The audio index now contains separate Menu, Good and Evil playlists (format 2).
Both factions are exported even from the shared Good content pack. Since build-143,
the player's seated faction selects match music, including an empty army or one
led by a converted enemy unit. Observers follow the first seated slot (lowest slot
number), so an Evil-only match never falls back to the shared pack's Good label.
Switching the commanded player in the Sandbox also switches faction music; switching
between two players of the same faction preserves the playing track and quiet interval.
Older/demo caches without seated factions use unit names, then the content faction.
Old format-1 caches can still play their Good normal
tracks and report how to refresh menu/Evil music. The quiet intervals and the
shuffle are presentation choices; no exact legacy scheduler timing is claimed.

Melee impact events resolve through the attacker's original melee weapon and the
target's composition, supplementing the existing ranged impact mappings. Source
effects also cover menu buttons, check boxes, menu open/close, rally markers,
building placement/completion, mining and building/repair work. Mining follows
the existing worker hit tick; hammering is limited to one attempt per worker per
second. Foreground reply throttles, pool sizes, spatial falloff, hammer cadence,
music track order and transition timing are presentation policies, not a claim
to reproduce the legacy engine's audio scheduler. Audio never changes combat,
orders or simulation randomness.

`tools/game_audio.py` decodes mono and stereo Microsoft ADPCM short clips to PCM
and copies Ogg music directly. The decoder accepts complete blocks and valid
short final blocks; shipped odd-sample speech can omit its final nibble, so that
single unavailable sample is trimmed. Larger sample-count mismatches still fail.
Audio assets and their index join the existing cache SHA-256 verification.

Refresh existing local shared/demo caches with `python tools/game_audio.py`.
Use `--cache PATH` for another self-contained cache (a slim map redirects to its
shared pack). New `content_pack.py` and `prepare_prototype.py` builds include
audio automatically; `--install` extraction includes SpeechFX and Music archives.
An older cache without audio remains playable and reports how to refresh it.

Validation: `python -m unittest discover -s tools -p test_game_audio.py` and
`test_combat_rules.py` cover source mappings, output protection and ADPCM channel,
partial-block and sample-count handling. `prototype/run.ps1 -AudioSmoke -NoAi`
checks playback, bus routing, command speech, throttles, persistence, preview /
Apply / Cancel / Esc / Reset, simulation independence, pause, combat leaving music
alone, all three playlists and a real Ogg completion followed by silence. The setup
smoke checks menu-to-match-to-menu music across scene reloads. The pure
playlist regression (`dotnet run --project tests/simulation -- --music-playlist-checks`)
also checks source order, quiet intervals, repeated completion, a single cue,
missing music, faction selection and legacy caches. The audio smoke also revisits detached/populated UI subtrees,
reparents controls, adds new controls and checks one callback per button/checkbox.
Its launcher treats Godot error output as failure even when the engine exits zero.
Add `-CachePath converted/skirmish` to exercise the shared pack,
or `-CapturePath converted/build109-validation/audio.png` for the settings capture.
