# Game text and languages

From build-183 the game shows the original game's own text, taken from the player's install, in whatever language that
install is. Strings the original never had live in this repository in English and Russian. Original text is never
copied into the repository: `tools/game_text.py` reads it from the install into `converted/`, which git ignores.

- **The language follows the install's text tables.** There is no language setting. A Russian install shows Russian;
  an English install shows English. An install in any other language shows its own original text, with the custom
  strings in English.
- **Developer text stays English:** the F4 debug panel, the observer's bot journal, the build/FPS overlay, logs and
  smoke output. Since build-183 names are resolved as the rules load, so the journal shows unit and building names in
  the install's language inside English sentences.

## Refreshing the text

```bash
python tools/game_text.py --pack converted/content/good
```

This writes `converted/content/good/text/original.json` and prints the detected language and its evidence. A full
`python tools/content_pack.py` writes the same file. The game reads it on start (`Main.Text.cs`). Without the file the
game runs with the catalog's English and logs where the file should be.

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
- Some entries are stubs: empty, "Затычка", "xxx…", `<… Benefit>`, `<Название заклинания>`. The import lists them in
  `placeholders`, and the game treats them as missing.
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

The evidence is stored with the text. This install reads `ru` at 97.9% Cyrillic.

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

- **Set `original`** to use the install's text: `Menu:12`, or a symbol such as `TOOLTIPS_LEVEL`. Leave `en` and `ru`
  empty; `note` glosses the text in English. If an install lacks that entry, `en`/`ru` text in the same row is the
  fallback.
- **Leave `original` empty** for custom text, which needs both `en` and `ru`:
  - Plural rows end in `.one`/`.few`/`.many` (Russian) or `.one`/`.other` (English).
  - Arguments are `{0}`…`{9}`.
  - Russian uses no ё (the menu font lacks it).
  - Prefer "Label: value" to sentences that would have to decline a name.
- **Use `record.<SYMBOL>.<field>`, `ability.<id>.<field>` or `map.<stem>.<field>`** for a record whose original text
  is missing or a stub. `record.OBJECT_PLACE_OF_POWER_RANDOM.name` is one.

`tests/simulation` (`GameTextChecks`, run alone with `--game-text-checks`) fails when a custom row lacks a language,
when an original ref is malformed, or when Russian text contains ё.

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

**Smokes** expect what the game shows, in any language. Write `Shown("UNIT_G_RANGER","Ranger")`,
`ShownAbility(id,"…")`, `ShownPower(id,"…")` or `GameText.T("key")`, never the English literal.

## What is localized

| Build | Text |
| --- | --- |
| 183 | Names of units, heroes, buildings, upgrades and research, abilities, Fate powers, places of power and neutral objects, and maps. The AI personalities' names come from the install's `.rai` files |
| 184 (next) | Tooltips: construct, train, upgrade, research, ability, Fate, place power, status effect; the stat labels |
| 185 (next) | Menus, setup, settings, outcome, HUD labels, messages; refusal reasons become codes |
| Later | Campaign text, which the import already covers (briefings, objectives, hints, speakers, subtitles) |
