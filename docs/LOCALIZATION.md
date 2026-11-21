# Game text and languages

From build-183 the game shows the original game's own text, taken from the player's install, in whatever language that
install is. Strings the original never had live in this repository in English and Russian. Original text is never
copied into the repository: since build-193 the game reads it straight from the install at startup, and
`tools/game_text.py` still writes it into `converted/` (which git ignores) for a game started without one.

- **The language follows the install's text tables.** There is no language setting. A Russian install shows Russian;
  an English install shows English. An install in any other language shows its own original text, with the custom
  strings in English.
- **Developer text stays English:** the F4 debug panel, the observer's bot journal, the build/FPS overlay, logs and
  smoke output. Since build-183 names are resolved as the rules load, so the journal shows unit and building names in
  the install's language inside English sentences.

## Where the game gets the text

Build-193: at startup `Main.LoadGameText` looks for the install, the first of these with `Archives` and `Database`
folders:

1. `--install=<folder>` on the command line (`./prototype/run.ps1 -Install "C:/Games/War of the Ring"`),
2. the folder the launcher last checked and kept,
3. the install the last import read (`converted/install.json`),
4. `REFORGED_INSTALL`,
5. `TLOTR - War of the Ring` beside the data, the tools' default.

It builds the text from that install with `OriginalText.FromInstall` (`prototype/Install/OriginalText.cs`, a C# port of
`game_text.py` that gives the same result) in about 150 ms, and keeps it for later scene changes while the install's
Text.H2O, Levels.H2O and InterfaceTextIDs.lqr are unchanged. The log says which: `Text: en read from the install ...`.
So the text follows the install chosen, even when the content pack was built from another one.

Without an install, or when it cannot be read, the game reads the content pack's `text/original.json`, which

```bash
python tools/game_text.py --pack converted/content/good
```

writes, printing the detected language and its evidence (a full `python tools/content_pack.py` writes the same file).
Without either, the game runs with the catalog's English and logs where the file should be.

## Where the original keeps its text

`Archives/Text.H2O` holds 49 LIQDTEXT tables (`<Table>.ltx`):

| Field | Format |
| --- | --- |
| Header | `"LIQDTEXT"`, f32 1.0, u32 count |
| Entry | u32 id, u8 flag (always 0), u32 length in characters, UTF-16LE text |

- The length counts the closing NUL, except for an empty string, which has length 0 and no NUL.
- Entries are not sorted by id.
- The same id means different things in different tables, so text is addressed as `Table:id` (`GameConsole:30`).
- `<br>` is a line break, and `%s` / `%d` are arguments. The import rewrites them to `\n` and `{0}`, `{1}`….

| Table | Holds |
| --- | --- |
| GameConsole | The HUD: unit, building, upgrade, ability and Fate power names and descriptions; command card; tooltip labels (8300 Level, 8301 Requirements, 8302 Description, 8303 sec., 8304 Cooldown); stats (8404-8414); statuses (8420-8423); resources (8450-8453) |
| Game | Alerts (10-43), hints, campaign objectives (2050-2457), speaker and team names, music tracks |
| Menu | Front end, setup (1400-1420, 1500-1508, 3400-3422), game types (4400-4404), settings, buttons (9000-9037), victory/defeat (4600-4602, 4700-4703), briefings (1000-1059, 6000-6770) |
| MultiPlayerMaps | Map names (odd ids) and descriptions (even) |
| Hotkeys_Text, DIK_Keys | Key-binding screen and key names (DIK_Keys ids are DirectInput scan codes) |
| TechTreeTooltips | Tech-tree screen labels |
| 38 character tables | Spoken-line subtitles, matching `Dialogue/<Name>/<Name>_<id>.ogg` |

**How a record names its text.** A database column `InterfaceTextID*` holds a record of `InterfaceTextIDs.lqr`
(1,346 symbols such as `INTERFACE_TEXT_ID_UNIT_G_GONDOR_SWORDSMAN_NAME`). That record's `InterfaceTextIDNumber` is
the id inside an `.ltx` table. The value -1 means no text; 0 is a real record. No field names the table, so the
symbol's editor folder decides it:

| Folder | Table |
| --- | --- |
| Units, Buildings, Techniques, Battlegear, Objects, Global Spells, Console | GameConsole |
| Hints, Game Messages, Voice Message, In Game Menus | Game |
| Errors | RasterizerErrors |
| Missions, objectives and team names | Game |
| Missions, everything else | Menu |

These columns hold an `.ltx` id directly, with no InterfaceTextIDs record in between:

| Column | Table |
| --- | --- |
| `MultiplayerGameModes.LocalizedNameTextID` | Menu |
| `StatsScreenAwards.AwardTextID` | Menu |
| `Hotkeys.InterfaceTextID` | Hotkeys_Text |
| `Actors.ActorNameTextID` | Game |
| `MusicTracks.TextID` | Game |

A multiplayer map's `.wor` header holds its MultiPlayerMaps name and description ids, right after the editor's English
name and description: MP02_Bilbos_Bluffs is 203/204, MP04_Wasteland is 415/416.

**Pitfalls:**
- Hero ability names come from BattleGear. The `Spells` names are stale placeholders: `SPELL_BOROMIR_HORNOFGONDOR`
  says "Лезвия с шипами" ("Barbed blades").
- Some entries are stubs: empty, "Затычка", "xxx…", `<… Benefit>`, `<Название заклинания>`. The English release
  writes "Empty description" and "Placeholder" instead (build-187). The import lists them in `placeholders`, and
  the game treats them as missing.
- 80 GameConsole ids belong to no table, because the exe uses them directly: 8410 Health, 8411 Kills, 8452 Magic power,
  8136 Level, and short speaker names. The catalog refers to them by id.

**Detecting the language.** Nothing in the files names it:
- The `.ltx` header has no language field.
- `Rings.exe` has no version resource.
- `Rings.ini [Localization] Language` is only read for KOR, TCH and SCH.
- The registry belongs to whichever installer was used. This copy came from a repack whose `Inno Setup: Language =
  english` is the installer's own interface language, not the game's.

So the import reads the text itself:
- Cyrillic is Russian (Ukrainian if і, ї, є and ґ are common).
- Hangul is Korean, kana is Japanese, and Han is Chinese, with the script taken from Rings.ini.
- Latin text is told apart by stop words among English, German, French, Spanish, Italian and Polish.

The evidence is stored with the text. This install reads `ru` at 97.9% Cyrillic. The English release reads `en`:
all Latin, with English stop words far ahead.

**Plain-text files.** The AI players' `.rai` files name no encoding. Since build-187 their names are read in the ANSI
code page of the detected language (`game_text.code_page`): cp1251 for Russian ("Арагорн"), cp1252 for English
("Grishnákh", which cp1251 would show as "Grishnбkh").

## `text/original.json`

| Key | Contents |
| --- | --- |
| `language`, `evidence` | Detected language and why |
| `tables` | Every table as `{id: text}`, in runtime form |
| `symbols` | Short InterfaceTextIDs symbol (no `INTERFACE_TEXT_ID_` prefix) → `Table:id` |
| `records` | Database record symbol → `{name, title, description, effect, hint, epilogue}` refs (Units, Buildings, Techniques, BattleGear, GlobalSpells, Objects, PlacesOfPower, Missions, and the direct-id tables) |
| `maps` | Map file stem → `{name, description}` |
| `abilities` | Combat-rules ability id → refs. A positive id is a BattleGear id; -1000 - id is a Technique's own ability (Elven Cloaks is -1022) |
| `global_powers` | GlobalSpells id → refs |
| `placeholders`, `dangling_symbols` | Stub entries, and symbols whose id no table holds |

## The catalog: `prototype/Locale/strings.csv`

The catalog is CSV with the columns `key, original, en, ru, note`. Quoted cells may hold commas, `""` and line breaks.
A key that starts with `#` marks a comment row. Godot keeps the file as it is (`strings.csv.import`), so the CSV is
not imported as a translation.

- **Set `original`** to use the install's text: `Menu:12`, or a symbol such as `TOOLTIPS_LEVEL`. `note` glosses the
  text in English. If an install lacks that entry, `en`/`ru` text in the same row is the fallback. Since build-184 a
  row whose text the simulation checks read keeps the English the HUD used before in `en` (`tip.level` is `Level`),
  because `tests/simulation` runs without install text.
- **Leave `original` empty** for custom text, which needs both `en` and `ru`:
  - Plural rows end in `.one`/`.few`/`.many` (Russian) or `.one`/`.other` (English).
  - Arguments are `{0}`…`{9}`.
  - Russian uses no ё (the menu font lacks it).
  - Prefer "Label: value" to sentences that would have to decline a name.
- **Use `record.<SYMBOL>.<field>`, `ability.<id>.<field>` or `map.<stem>.<field>`** for a record whose original text
  is missing or a stub. `record.OBJECT_PLACE_OF_POWER_RANDOM.name` is one.

`tests/simulation` (`GameTextChecks`, run alone with `--game-text-checks`) fails when a custom row lacks a language,
when an original ref is malformed, or when Russian text contains ё. The other checks run with the repository catalog
and no install tables (`GameTextChecks.LoadRepositoryCatalog`), so they read the catalog's English.

## In code

The service is `Reforged.Presentation.GameText`, in `Presentation/GameText.cs`. It is plain C#, so the simulation
checks can use it.

| Call | Returns |
| --- | --- |
| `GameText.T("key", args…)` | A catalog string; `⟦key⟧` when the key has no text |
| `GameText.Ref("Menu:4602")` | Original text by `Table:id` or symbol |
| `GameText.Name(symbol, english)` | A record's name, or the English internal name when the install has none |
| `GameText.Ability(id, field)`, `GameText.GlobalPower(id, field)`, `GameText.Map(stem, field)` | Text by id |
| `GameText.Plural(key, n, args…)` | The form for `n` |

Text that resolves to nothing is collected in `GameText.Missing` and logged once.

**Names (build-183)** are resolved once, as the content loads: `GameNames.Localize` for units, abilities and Fate
powers, and the production, construction, research and place loaders for the rest. Every panel therefore shows them
with no change. Nothing in the simulation decides by a display name. Lookups go by symbolic names: unit portraits
are `CommandPanel.PortraitName(UNIT_…)`.

**Tooltips (build-184)** are built by `Presentation/HudTip.cs`, as the original lays them out: the title with the
button's hotkey (`HudTip.Keyed`), a cost line, then the body. The cost line is a marked list of numbers
(`HudTip.Costs(food, ore, fate, population)`, `HudTip.FateCost`), which the tooltip box turns into the original's
resource cells, so it no longer reads costs out of English words. Since build-185 a marked time line
(`CommandPanel.Seconds`, `HudTip.Timed`) follows it and shows on its own above the Description strip. Bodies use the record's own description from the
install (`GameText.Record(symbol, "description")`, `GameNames.AbilityText`, `GameText.GlobalPower(id, "description")`),
then the lines with the simulation's numbers from the catalog. Labels are the original's where it has them: Level,
Description, Sec. and Cooldown (GameConsole 8300-8304), Armor, Attack, Health, Victories, Ballista and Lifetime
(8404-8414), Poisoned (8420), Food, Ore, Fate and Population (8450-8453), the Hold, Guard and Patrol commands with their
descriptions (8104-8115), Idle workers (8006-8007) and the attack chart's Weapon Rating (8122).

**Smokes** expect what the game shows, in any language. Write `Shown("UNIT_G_RANGER","Ranger")`,
`ShownAbility(id,"…")`, `ShownPower(id,"…")` or `GameText.T("key")`, never the English literal. For tooltips,
`ShownTip(name, key, HudTip.Costs(…), ticks)` is a construct, train, upgrade or research tooltip's head, and
`ShownBefore`/`ShownAfter(key, …Slot…)` give the text on either side of an argument a check does not pin.

Still English after build-184, for build-188: refusal reasons (they come from the simulation), the order readouts in
the console and portrait tooltips, the group, walk/run and pause labels, notices, the observer HUD and the menus.

## What is localized

| Build | Text |
| --- | --- |
| 183 | Names of units, heroes, buildings, upgrades and research, abilities, Fate powers, places of power and neutral objects, and maps. The AI personalities' names come from the install's `.rai` files |
| 184 | Tooltips: construct, train, upgrade, research, queue, ability, Fate, place power, status effect, hero bar, garrison; the stat labels and the place-of-power summary |
| 188 (next) | Outcome, HUD labels, messages; refusal reasons become codes (menus, setup and settings followed in 222-223) |
| 222 | The main menu (the install's Skirmish, Options, Quit, exit question, OK and Cancel; catalog rows for the rest), the loading screen's steps, and the Settings camera-distance row |
| 223 | The setup screens (headers, slots, sides, difficulties, bot names from the install, traits, bot card, status line, game settings), the in-game menu and every Settings tab, including the Controls reference |
| Later | Campaign text, which the import already covers (briefings, objectives, hints, speakers, subtitles) |
