using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-201: the units' abilities, ports of tools/hero_abilities.py, hero_skills.py, dwarf_abilities.py,
/// ranger_abilities.py, elven_abilities.py, nature_abilities.py, evil_abilities.py and evil_upgrades.py, each traced
/// through the shipped spell graphs as the Python traces them.</summary>
public sealed partial class PackCombat
{
    // ---- Spell graphs -------------------------------------------------------------------------------------------

    private string ActionType(LqrRecord action) => ById(T("SpellActions"),I(action,"SpellActionType")).Name;
    private List<LqrRecord> Chain(string table,int first,string next) => Linked(T(table),first,next);
    private List<LqrRecord> EventsOf(LqrRecord spell) => Chain("SpellAndEvents",I(spell,"FirstSpellAndEvent"),"NextSpellAndEvent");
    private List<LqrRecord> ActionsOf(LqrRecord e) => Chain("SpellAndEventAndActions",I(e,"FirstSpellAndEventAndAction"),"NextSpellAndEventAndAction");
    /// <summary>elven_abilities.graph: (event, action) pairs, spawned child spells' after the action that spawns them.</summary>
    private List<(LqrRecord Event,LqrRecord Action)> Graph(LqrRecord spell) => SpellGraph(_db,spell);
    private List<LqrRecord> Typed(IEnumerable<(LqrRecord Event,LqrRecord Action)> actions,string suffix)
        => [.. actions.Select(p => p.Action).Where(a => ActionType(a).EndsWith(suffix,StringComparison.Ordinal))];

    /// <summary>evil_abilities.walk: graph, and also the events ENABLE_SPELL_AND_EVENT_TYPE actions switch on.</summary>
    private List<(LqrRecord Event,LqrRecord Action)> Walk(LqrRecord spell)
    {
        var seen = new HashSet<int> { spell.Id };
        var visited = new HashSet<int>();
        var result = new List<(LqrRecord,LqrRecord)>();
        void Visit(LqrRecord e)
        {
            if (!visited.Add(e.Id)) return;
            foreach (var action in ActionsOf(e))
            {
                result.Add((e,action));
                int child = I(action,"SpellType");
                if (child >= 0 && seen.Add(child))
                    foreach (var inner in EventsOf(ById(T("Spells"),child))) Visit(inner);
                if (ActionType(action).EndsWith("_ENABLE_SPELL_AND_EVENT_TYPE",StringComparison.Ordinal) && I(action,"SpellAndEventType") >= 0)
                    Visit(ById(T("SpellAndEvents"),I(action,"SpellAndEventType")));
            }
        }
        foreach (var e in EventsOf(spell)) Visit(e);
        return result;
    }

    private JsonArray ActionValues(IEnumerable<(LqrRecord Event,LqrRecord Action)> actions)
        => new([.. actions.Select(p => (JsonNode?)new JsonObject { ["Event"] = p.Event.Name, ["Type"] = ActionType(p.Action), ["Values"] = Values(p.Action) })]);

    private static int Ceil20(double seconds) => Ceil(seconds*20);
    private string UvaStem(int uva) => WindowsStem(S(ById(T("UVAs"),uva),"Filename"));
    private string CursorStem(int cursor) => WindowsStem(S(ById(T("Cursors"),cursor),"Filename"));
    private static JsonArray IntArray(IEnumerable<int> values) => new([.. values.Select(v => (JsonNode?)v)]);
    private static int Int(JsonNode? node) => int.Parse(node!.ToJsonString(),CultureInfo.InvariantCulture);

    /// <summary>Python's str.title(): a letter after a letter is lowered, any other raised.</summary>
    private static string Title(string text)
    {
        var s = new StringBuilder(text.Length);
        bool previous = false;
        foreach (char c in text)
        {
            s.Append(char.IsLetter(c) ? previous ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c) : c);
            previous = char.IsLetter(c);
        }
        return s.ToString();
    }

    /// <summary>Python's %g: six significant digits, trailing zeros dropped.</summary>
    private static string G(double value)
    {
        string text = value.ToString("G6",CultureInfo.InvariantCulture);
        int e = text.IndexOf('E');
        if (e < 0) return text;
        int exponent = int.Parse(text[(e+1)..],CultureInfo.InvariantCulture);
        return text[..e]+"e"+(exponent < 0 ? "-" : "+")+Math.Abs(exponent).ToString("00",CultureInfo.InvariantCulture);
    }

    private Dictionary<string,object?> StateById(Dictionary<string,object?> model,long id) => States(model).First(s => L(s["id"]) == id);
    private static LskClip ClipNamed(LskSkeleton skeleton,object? name) => skeleton.Clips.First(c => c.Name == (string?)name);
    private static List<JsonNode?> ClipSounds(List<Dictionary<string,object?>> events,Dictionary<string,object?> choice)
        => [.. EventIndices(choice).Where(i => Name(events[i]) == "sound").Select(i => Py(events[i]["sound"]))];

    // ---- Dispatch: hero_abilities.export ---------------------------------------------------------------------

    private static readonly Dictionary<string,Dictionary<int,string>> Heroes = new()
    {
        ["UNIT_G_HERO_ARAGORN"] = new() { [25] = "Andúril's Fury", [41] = "Greater Kingsfoil", [44] = "Immunity" },
        ["UNIT_G_HERO_FRODO"] = new() { [7] = "The One Ring", [12] = "Heroic Aura", [50] = "Sting" },
        ["UNIT_G_HERO_GIMLI"] = new() { [10] = "Siege Aura", [27] = "Sunder", [45] = "Capture" },
        ["UNIT_G_HERO_LEGOLAS"] = new() { [0] = "Trueshot", [6] = "Elven Speed", [19] = "Guardian Wind" },
        ["UNIT_G_HERO_GANDALF"] = new() { [15] = "Blinding Light", [40] = "Negate Ability", [42] = "Flame Shield", [43] = "Rain of Fire" },
        ["UNIT_G_ELVEN_LIGHTBRINGER"] = new() { [24] = "Protection" },
    };
    private static readonly string[] EvilHeroes = ["UNIT_E_HERO_GOLLUM", "UNIT_E_HERO_NAZGUL_KING", "UNIT_E_HERO_QAMARA", "UNIT_E_HERO_GRISHNAKH", "UNIT_E_HERO_SARUMAN"];

    private static JsonArray BySlot(IEnumerable<JsonObject> abilities) => new([.. abilities.OrderBy(a => Int(a["Slot"])).Select(a => (JsonNode?)a)]);

    private JsonArray Abilities(LqrRecord unit,Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        string name = unit.Name;
        if (name.StartsWith("UNIT_E_",StringComparison.Ordinal) && !EvilHeroes.Contains(name)) return EvilUpgrades(unit,model,skeleton);
        if (name is "UNIT_G_BEORNING" or "UNIT_G_BEAR" or "UNIT_G_HUORN") return NatureAbilities(unit,model,skeleton);
        if (name is "UNIT_G_ELVEN_ARCHER" or "UNIT_G_ELVEN_LIGHTBRINGER") return ElvenAbilities(unit,model,skeleton);
        if (name == "UNIT_G_RANGER") return RangerAbilities(model,skeleton);
        if (name is "UNIT_G_DWARVEN_AXETHROWER" or "UNIT_G_DWARVEN_SHIELDBREAKER") return DwarfAbilities(unit);
        if (EvilHeroes.Contains(name)) return EvilHeroAbilities(unit,model,skeleton);
        if (!Heroes.TryGetValue(name,out var names)) return [];
        var abilities = new List<JsonObject>();
        foreach (int gearId in Enumerable.Range(1,4).Select(i => I(unit,$"BattleGear{i}")))
        {
            if (gearId < 0) continue;
            var gear = ById(T("BattleGear"),gearId);
            var spell = ById(T("Spells"),I(gear,"SpellType"));
            var target = T("SpellValidTargets").Get(I(spell,"ValidTargetType"));
            var events = EventsOf(spell);
            var actions = events.SelectMany(e => ActionsOf(e).Select(a => (Event: e,Action: a))).ToList();
            LqrRecord Find(string suffix) => actions.Select(p => p.Action).First(a => ActionType(a).EndsWith(suffix,StringComparison.Ordinal));
            string icon = S(ById(T("UVAs"),I(gear,"PortraitBorderless")),"Filename").Replace('\\','/').Split('/')[^1];
            var source = new JsonObject
            {
                ["Gear"] = Values(gear), ["Spell"] = Values(spell), ["Target"] = target != null ? Values(target) : null,
                ["Events"] = new JsonArray([.. events.Select(e => (JsonNode?)new JsonObject { ["Id"] = e.Id, ["Name"] = e.Name, ["Values"] = Values(e) })]),
                ["Actions"] = new JsonArray([.. actions.Select(p => (JsonNode?)new JsonObject
                    { ["Id"] = p.Action.Id, ["Name"] = p.Action.Name, ["Type"] = ActionType(p.Action), ["Values"] = Values(p.Action) })]),
            };
            var definition = new JsonObject
            {
                ["Id"] = gear.Id, ["Slot"] = V(gear,"SpecificIndex"), ["Name"] = names[gear.Id], ["Icon"] = icon[..Math.Max(0,icon.Length-4)],
                ["Passive"] = I(spell,"UsageType") == 0, ["Source"] = source,
            };
            var modelEvents = Events(model);
            void Cast()
            {
                var animation = Find("_PLAY_ANIMATION");
                var state = StateById(model,I(animation,"UnitAnimType"));
                var choice = Positive(state).First();
                double duration = ClipNamed(skeleton,choice["clip_name"]).Duration;
                var e = events.First(x => I(x,"ToolscriptType") >= 0);
                var release = EventIndices(choice).Select(i => modelEvents[i]).First(x => L(x["type"]) == I(e,"ToolscriptType"));
                int castTicks = Ceil20(duration);
                definition["RechargeTicks"] = Ceil20(F(spell,"RechargeTime"));
                definition["CastClip"] = choice["clip_name"] as string;
                definition["CastTicks"] = castTicks;
                definition["EffectTick"] = Math.Min(castTicks-1,Ceil(duration*D(release["time"])*20));
                source["Animation"] = new JsonObject { ["State"] = Py(state["id"]), ["Duration"] = duration, ["Event"] = Py(release), ["Toolscript"] = I(e,"ToolscriptType") };
                source["SoundEvents"] = new JsonArray([.. ClipSounds(modelEvents,choice)]);
                definition["SoundEvents"] = source["SoundEvents"]!.DeepClone();
            }
            List<LqrRecord> ChildActions()
            {
                var child = ById(T("Spells"),I(Find("_SPAWN_CHILD_SPELL"),"SpellType"));
                var found = EventsOf(child).SelectMany(ActionsOf).ToList();
                source["Child"] = new JsonObject
                {
                    ["Spell"] = Values(child),
                    ["Actions"] = new JsonArray([.. found.Select(a => (JsonNode?)new JsonObject { ["Id"] = a.Id, ["Type"] = ActionType(a), ["Values"] = Values(a) })]),
                };
                return found;
            }
            JsonArray SpecificUnits() => IntArray(Enumerable.Range(1,4).Select(i => I(target!,$"SpecificUnitType{i}")).Where(u => u >= 0));
            switch (gear.Id)
            {
                case 41:
                    Cast();
                    definition["Kind"] = 0; definition["HealMilli"] = Round(F(Find("_APPLY_HEALTH"),"Float_01")*1000);
                    definition["RangeMm"] = Round(F(target!,"MaxSpecifiedRange")*1000); definition["MaxTargetHealthPpm"] = Ppm(F(target!,"MaxTargetHealthPct"));
                    break;
                case 25:
                {
                    var weapon = ById(T("Weapons"),I(Find("_SWITCH_MELEE_WEAPON"),"WeaponType"));
                    definition["Kind"] = 1; definition["DpsMilli"] = Round(F(weapon,"CuttingDamagePerSecond")*1000);
                    definition["BonusScale"] = Ppm(F(Find("_APPLY_DAMAGE_MODIFIER_VERSUS_SPECIFIC_UNIT"),"Float_01")); definition["BonusTargets"] = SpecificUnits();
                    source["Weapon"] = Values(weapon);
                    break;
                }
                case 44:
                {
                    var flags = new (string Suffix,int Flag)[] { ("_SET_IMMUNE_TO_POISON",1),("_SET_IMMUNE_TO_STUN",2),("_SET_IMMUNE_TO_BLIND",4),("_SET_IMMUNE_TO_SLOW",8) };
                    definition["Kind"] = 2; definition["Immunities"] = flags.Where(f => B(Find(f.Suffix),"Boolean_01")).Sum(f => f.Flag);
                    break;
                }
                case 7:
                {
                    var on = events.First(e => e.Name.EndsWith("_ONACTIVATE",StringComparison.Ordinal));
                    var hide = ActionsOf(on).First(a => ActionType(a).EndsWith("_SET_VISIBLE",StringComparison.Ordinal));
                    definition["Kind"] = 3; definition["DrainMilliPerTick"] = Round(-F(Find("_UPDATE_HEALTH"),"Float_01")*1000/20);
                    definition["AlphaPpm"] = Ppm(F(hide,"Float_01"));
                    definition["SoundEvents"] = IntArray(Typed(actions,"_PLAY_SOUND_EVENT").Select(a => I(a,"SoundEventType")));
                    source["SoundEvents"] = definition["SoundEvents"]!.DeepClone();
                    break;
                }
                case 50:
                    definition["Kind"] = 4; definition["BonusScale"] = Ppm(F(Find("_APPLY_DAMAGE_MODIFIER_VERSUS_SPECIFIC_UNIT"),"Float_01"));
                    definition["BonusTargets"] = SpecificUnits(); definition["RangeMm"] = Round(F(target!,"MaxSpecifiedRange")*1000);
                    break;
                case 12:
                {
                    var recharge = ChildActions().First(a => ActionType(a).EndsWith("_SET_BATTLEGEAR_RECHARGE_MULTIPLIER",StringComparison.Ordinal));
                    definition["Kind"] = 5; definition["RangeMm"] = Round(F(target!,"MaxSpecifiedRange")*1000); definition["RechargeScalePpm"] = Ppm(F(recharge,"Float_01"));
                    break;
                }
                case 10:
                {
                    var modifier = ChildActions().First(a => ActionType(a).EndsWith("_APPLY_BUILDING_DAMAGE_MODIFIER",StringComparison.Ordinal));
                    definition["Kind"] = 6; definition["RangeMm"] = Round(F(target!,"MaxSpecifiedRange")*1000); definition["BonusScale"] = Ppm(F(modifier,"Float_01"));
                    break;
                }
                case 19:
                {
                    Cast();
                    var found = ChildActions();
                    var ward = found.First(a => ActionType(a).EndsWith("_APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER",StringComparison.Ordinal));
                    var timer = found.First(a => ActionType(a).EndsWith("_START_TIMER",StringComparison.Ordinal));
                    definition["Kind"] = 7; definition["Passive"] = false; definition["RangeMm"] = Round(F(spell,"MaxAreaOfEffectRange")*1000);
                    definition["BonusScale"] = Ppm(F(ward,"Float_01")); definition["DurationTicks"] = Ceil20(F(timer,"Float_01"));
                    break;
                }
                default:
                    definition = HeroSkill(gear,model,skeleton,names[gear.Id]);
                    break;
            }
            definition["Purchasable"] = B(gear,"IsPurchasable");
            definition["PurchaseFateCost"] = B(gear,"IsPurchasable") ? V(gear,"PurchaseCostPower") : 0;
            abilities.Add(definition);
        }
        return BySlot(abilities);
    }

    // ---- hero_skills.export ---------------------------------------------------------------------------------

    private static readonly Dictionary<int,int> SkillKinds = new() { [27] = 27, [45] = 28, [0] = 29, [6] = 30, [15] = 31, [40] = 32, [42] = 33, [43] = 34 };

    private JsonObject HeroSkill(LqrRecord gear,Dictionary<string,object?> model,LskSkeleton skeleton,string name)
    {
        var spell = ById(T("Spells"),I(gear,"SpellType"));
        var target = ById(T("SpellValidTargets"),I(spell,"ValidTargetType"));
        var actions = Graph(spell);
        List<LqrRecord> Of(string suffix) => Typed(actions,suffix);
        var ability = new JsonObject
        {
            ["Id"] = gear.Id, ["Slot"] = V(gear,"SpecificIndex"), ["Name"] = name, ["Passive"] = false, ["Kind"] = SkillKinds[gear.Id],
            ["Icon"] = UvaStem(I(gear,"PortraitBorderless")), ["Cursor"] = CursorStem(I(spell,"TargetCursor")), ["RechargeTicks"] = Ceil20(F(spell,"RechargeTime")),
            ["RangeMm"] = Round(F(target,"MaxSpecifiedRange")*1000), ["AreaRadiusMm"] = Round(F(spell,"MaxAreaOfEffectRange")*1000),
        };
        var animations = Of("_PLAY_ANIMATION");
        var sounds = Of("_PLAY_SOUND_EVENT").Select(a => I(a,"SoundEventType")).ToList();
        if (animations.Count > 0)
        {
            var events = Events(model);
            var choice = Positive(StateById(model,I(animations[0],"UnitAnimType"))).First();
            var clip = ClipNamed(skeleton,choice["clip_name"]);
            var tools = actions.Select(p => I(p.Event,"ToolscriptType")).Where(t => t >= 0).ToHashSet();
            var release = EventIndices(choice).Select(i => events[i]).First(e => tools.Contains((int)L(e["type"])));
            int ticks = Ceil20(clip.Duration);
            ability["CastClip"] = clip.Name; ability["CastTicks"] = ticks;
            ability["EffectTick"] = Math.Min(ticks-1,Ceil(clip.Duration*D(release["time"])*20));
            sounds.AddRange(EventIndices(choice).Where(i => Name(events[i]) == "sound").Select(i => (int)L(events[i]["sound"])));
        }
        var soundEvents = sounds.Distinct().Order().ToList();
        ability["SoundEvents"] = IntArray(soundEvents);
        var source = new JsonObject
        {
            ["Gear"] = Values(gear), ["Spell"] = Values(spell), ["Target"] = Values(target), ["SoundEvents"] = IntArray(soundEvents), ["Actions"] = ActionValues(actions),
        };
        ability["Source"] = source;
        switch (gear.Id)
        {
            case 27 or 15:
            {
                int duration = Ceil20(F(Of("_SET_DAZED")[0],"Float_01"));
                ability["DurationTicks"] = duration;
                ability["Description"] = $"Dazes enemies within {G(Int(ability["AreaRadiusMm"])/1000.0)} m for {G(duration/20.0)} seconds.";
                break;
            }
            case 45:
                ability["CaptureBuildings"] = Strings(Enumerable.Range(1,3).Select(i => ById(T("Buildings"),I(target,$"SpecificBuildingType{i}")).Name));
                ability["Description"] = "Approaches and captures an enemy Watchtower or Ballista Tower.";
                break;
            case 0:
            {
                var weapon = ById(T("Weapons"),I(Of("_SWITCH_MISSILE_WEAPON")[0],"WeaponType"));
                ability["HealMilli"] = Round(F(weapon,"CuttingDamagePerSecond")*1000); ability["RangeMm"] = Round(F(weapon,"MaximumRange")*1000);
                ability["KnockbackSpeedMm"] = Times(weapon,"KnockbackStrength",1000); ability["KnockbackAngle"] = Round(F(weapon,"KnockbackAngle"));
                source["Weapon"] = Values(weapon);
                ability["ExperiencePpm"] = Ppm(F(weapon,"ExperienceModifier"));
                ability["Description"] = "Fires a powerful arrow for 125 damage that knocks its target back.";
                break;
            }
            case 6:
                ability["BonusScale"] = Ppm(F(Of("_APPLY_TIMESCALE_MULTIPLIER")[0],"Float_01")); ability["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01"));
                ability["Description"] = "Allies within 12 m move and attack 20% faster for 15 seconds.";
                break;
            case 40:
                ability["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01"));
                ability["Description"] = "Disables enemy active and passive abilities in a 13 m area for 15 seconds.";
                break;
            case 42:
                ability["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01")); ability["AreaRadiusMm"] = 3000;
                ability["DpsMilli"] = Round(-F(Of("_UPDATE_HEALTH")[0],"Float_01")*1000);
                ability["Description"] = "Surrounds an ally with fire for 25 seconds, burning enemies within 3 m for 9 damage per second.";
                break;
            case 43:
            {
                var weapon = ById(T("Weapons"),I(Of("_APPLY_DAMAGE")[0],"WeaponType"));
                var timers = Of("_START_TIMER");
                ability["FirePpmPerSecond"] = Ppm(F(weapon,"FireDamagePerSecond")); ability["DelayTicks"] = Ceil20(F(timers[0],"Float_01"));
                ability["DurationTicks"] = Ceil20(F(timers[1],"Float_01")); ability["DpsMilli"] = Round(-F(Of("_UPDATE_HEALTH")[0],"Float_01")*1000);
                source["Weapon"] = Values(weapon);
                ability["ExperiencePpm"] = Ppm(F(spell,"ExperienceModifier"));
                ability["Description"] = "After 1.5 seconds, ignites enemy buildings in a 10 m area and burns enemy units for 18 damage per second for 5.5 seconds.";
                break;
            }
        }
        return ability;
    }

    // ---- dwarf_abilities, ranger_abilities -----------------------------------------------------------------------

    private static readonly Dictionary<string,string[]> DwarfTechniques = new()
    {
        ["UNIT_G_DWARVEN_AXETHROWER"] = ["TECHNIQUE_RAZORED_EDGE", "TECHNIQUE_FLAMING_AXE"], ["UNIT_G_DWARVEN_SHIELDBREAKER"] = ["TECHNIQUE_SHIELD_BREAK"],
    };

    /// <summary>The descriptions the ability modules give (the research panel's are the same: PackRules.Descriptions).</summary>
    private static string Description(string technique) => TechniqueDescription(technique);

    private JsonObject TechniqueAbility(LqrRecord technique,string name)
        => new()
        {
            ["Id"] = -1000-technique.Id, ["Slot"] = V(technique,"SpecificIndexForUVA"), ["Name"] = name, ["Icon"] = UvaStem(I(technique,"PortraitBorderless")),
        };

    private JsonArray DwarfAbilities(LqrRecord unit)
    {
        var result = new JsonArray();
        foreach (string name in DwarfTechniques.GetValueOrDefault(unit.Name) ?? [])
        {
            var technique = Named(T("Techniques"),name);
            var ability = TechniqueAbility(technique,Title(name["TECHNIQUE_".Length..].Replace('_',' ')));
            ability["Passive"] = true; ability["RequiredTechnique"] = name; ability["Description"] = Description(name);
            var source = new JsonObject { ["Technique"] = Values(technique) };
            ability["Source"] = source;
            if (I(technique,"AddBattleGear") >= 0)
            {
                var gear = ById(T("BattleGear"),I(technique,"AddBattleGear"));
                var spell = ById(T("Spells"),I(gear,"SpellType"));
                List<LqrRecord> Actions(LqrRecord s) => [.. EventsOf(s).SelectMany(ActionsOf)];
                var parent = Actions(spell);
                var child = ById(T("Spells"),I(parent.First(a => ActionType(a).EndsWith("_SPAWN_CHILD_SPELL",StringComparison.Ordinal)),"SpellType"));
                var found = Actions(child);
                LqrRecord Find(string suffix) => found.First(a => ActionType(a).EndsWith(suffix,StringComparison.Ordinal));
                ability["Id"] = gear.Id; ability["Slot"] = V(gear,"SpecificIndex"); ability["Kind"] = 12;
                ability["ArmorReductionMilli"] = Round(-F(Find("_SET_ARMOR"),"Float_01")*1000); ability["DurationTicks"] = Ceil20(F(Find("_START_TIMER"),"Float_01"));
                ability["MaxStacks"] = V(child,"StackableMax");
                source["Gear"] = Values(gear); source["Spell"] = Values(spell); source["Child"] = Values(child);
                source["Actions"] = new JsonArray([.. parent.Concat(found).Select(a => (JsonNode?)Values(a))]);
            }
            else
            {
                var weapon = ById(T("Weapons"),I(technique,"MissileWeapon"));
                ability["Kind"] = F(weapon,"FireDamagePerSecond") > 0 ? 14 : 13; ability["ChainTargets"] = V(weapon,"MaxChainTargets");
                ability["RangeMm"] = Round(F(weapon,"MaxChainTargetDistance")*1000); ability["ChainStartRangeMm"] = Round(F(weapon,"MaxChainStartDistance")*1000);
                ability["BonusScale"] = Ppm(F(weapon,"ChainTargetDamageReduction")); ability["FirePpmPerSecond"] = Ppm(F(weapon,"FireDamagePerSecond"));
                source["Weapon"] = Values(weapon);
            }
            result.Add(ability);
        }
        return result;
    }

    private JsonArray RangerAbilities(Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        var result = new JsonArray();
        foreach (var (name,kind) in new[] { ("TECHNIQUE_CAMOUFLAGE",15),("TECHNIQUE_EAGLE_EYE",16),("TECHNIQUE_DETECTION",17) })
        {
            var technique = Named(T("Techniques"),name);
            var ability = TechniqueAbility(technique,Title(name["TECHNIQUE_".Length..].Replace('_',' ')));
            ability["Passive"] = kind != 15; ability["Kind"] = kind; ability["Description"] = Description(name);
            var source = new JsonObject { ["Technique"] = Values(technique) };
            ability["Source"] = source;
            if (!B(technique,"IsInnate")) ability["RequiredTechnique"] = name;
            if (kind == 15)
            {
                var gear = ById(T("BattleGear"),I(technique,"AddBattleGear"));
                var spell = ById(T("Spells"),I(gear,"SpellType"));
                var events = EventsOf(spell);
                var actions = events.SelectMany(ActionsOf).ToList();
                var animation = actions.First(a => ActionType(a).EndsWith("_PLAY_ANIMATION",StringComparison.Ordinal));
                var choices = Dicts(StateById(model,I(animation,"UnitAnimType"))["choices"]).ToList();
                int variant = I(animation,"UnitAnimVariant")-1; // One-based; Python's index, so 0 picks the last.
                var choice = choices[variant < 0 ? choices.Count+variant : variant];
                var clip = ClipNamed(skeleton,choice["clip_name"]);
                var sounds = IntArray(actions.Where(a => ActionType(a).EndsWith("_PLAY_SOUND_EVENT",StringComparison.Ordinal)).Select(a => I(a,"SoundEventType")));
                ability["Id"] = gear.Id; ability["RechargeTicks"] = Ceil20(F(spell,"RechargeTime")); ability["CastClip"] = clip.Name;
                ability["CastTicks"] = Ceil20(clip.Duration); ability["AlphaPpm"] = 500000; ability["SoundEvents"] = sounds;
                source["Gear"] = Values(gear); source["Spell"] = Values(spell);
                source["Events"] = new JsonArray([.. events.Select(e => (JsonNode?)Values(e))]);
                source["Actions"] = new JsonArray([.. actions.Select(a => (JsonNode?)Values(a))]);
                source["SoundEvents"] = sounds.DeepClone();
                source["Adapter"] = "Idle variant is one-based. Friendly/detected camouflage uses 50% opacity; source SET_VISIBLE has no friendly alpha.";
            }
            result.Add(ability);
        }
        return result;
    }

    // ---- elven_abilities, nature_abilities --------------------------------------------------------------------

    private JsonArray ElvenAbilities(LqrRecord unit,Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        if (unit.Name == "UNIT_G_ELVEN_ARCHER")
        {
            var tech = Named(T("Techniques"),"TECHNIQUE_ELVEN_CLOAKS");
            var cloak = TechniqueAbility(tech,"Elven Cloaks");
            cloak["Passive"] = true; cloak["Kind"] = 18; cloak["AlphaPpm"] = 500000; cloak["RequiredTechnique"] = tech.Name; cloak["Description"] = Description(tech.Name);
            cloak["Source"] = new JsonObject
            {
                ["Technique"] = Values(tech),
                ["Adapter"] = "IsVisible=0 applies continuously; no movement/attack reveal rule in this technique. Friendly alpha is an adapter.",
            };
            var fletching = Named(T("Techniques"),"TECHNIQUE_EAGLE_FLETCHING");
            var range = TechniqueAbility(fletching,"Eagle Fletching");
            range["Passive"] = true; range["Kind"] = 22; range["RequiredTechnique"] = fletching.Name;
            range["BonusScale"] = Ppm(F(fletching,"MissileWeaponRangeModifier")); range["Description"] = Description(fletching.Name);
            range["Source"] = new JsonObject { ["Technique"] = Values(fletching) };
            return [cloak,range];
        }
        var result = new JsonArray();
        var events = Events(model);
        foreach (var (gearName,kind,name,technique) in new (string,int,string,string?)[]
                 {
                     ("BATTLE_GEAR_ELVEN_PROTECTION",19,"Protection",null), ("BATTLE_GEAR_ELVEN_NEGATE_MAGIC",20,"Negate Magic","TECHNIQUE_NEGATE_MAGIC"),
                     ("BATTLE_GEAR_ELVEN_LIGHT_OF_LOTHLORIEN",21,"Light of Lothlórien","TECHNIQUE_LIGHT_OF_LOTHLORIEN"),
                 })
        {
            var gear = Named(T("BattleGear"),gearName);
            var spell = ById(T("Spells"),I(gear,"SpellType"));
            var target = ById(T("SpellValidTargets"),I(spell,"ValidTargetType"));
            var actions = Graph(spell);
            List<LqrRecord> Of(string suffix) => Typed(actions,suffix);
            var choice = Positive(StateById(model,I(Of("_PLAY_ANIMATION")[0],"UnitAnimType"))).First();
            var clip = ClipNamed(skeleton,choice["clip_name"]);
            int tool = actions.Select(p => I(p.Event,"ToolscriptType")).First(t => t >= 0);
            var release = EventIndices(choice).Select(i => events[i]).First(e => L(e["type"]) == tool);
            var sounds = ClipSounds(events,choice);
            var endSounds = Of("_PLAY_SOUND_EVENT").Select(a => I(a,"SoundEventType")).ToList();
            int castTicks = Ceil20(clip.Duration);
            var ability = new JsonObject
            {
                ["Id"] = gear.Id, ["Slot"] = V(gear,"SpecificIndex"), ["Name"] = name, ["Cursor"] = CursorStem(I(spell,"TargetCursor")),
                ["Icon"] = UvaStem(I(gear,"PortraitBorderless")), ["Passive"] = false, ["Kind"] = kind, ["RangeMm"] = Round(F(target,"MaxSpecifiedRange")*1000),
                ["AreaRadiusMm"] = Round(F(spell,"MaxAreaOfEffectRange")*1000), ["RechargeTicks"] = Ceil20(F(spell,"RechargeTime")), ["CastClip"] = clip.Name,
                ["CastTicks"] = castTicks, ["EffectTick"] = Math.Min(castTicks-1,Ceil(clip.Duration*D(release["time"])*20)),
                ["SoundEvents"] = new JsonArray([.. sounds.Select(n => n?.DeepClone())]), ["EndSoundEvents"] = IntArray(endSounds),
                ["Description"] = technique != null ? Description(technique) : "Absorbs up to 150 damage on a friendly unit for 50 seconds. Recasting refreshes the shield.",
                ["Source"] = new JsonObject
                {
                    ["Gear"] = Values(gear), ["Spell"] = Values(spell), ["Target"] = Values(target),
                    ["SoundEvents"] = IntArray(sounds.Select(Int).Concat(endSounds).Distinct().Order()), ["Actions"] = ActionValues(actions),
                },
            };
            if (technique != null) ability["RequiredTechnique"] = technique;
            if (kind == 19) { ability["ShieldMilli"] = Round(F(Of("_ADD_BONUS_HEALTH")[0],"Float_01")*1000); ability["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01")); }
            if (kind == 21) { ability["DpsMilli"] = Round(-F(Of("_UPDATE_HEALTH")[0],"Float_01")*1000); ability["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01")); }
            result.Add(ability);
        }
        return result;
    }

    /// <summary>nature_abilities.animation: a state's first positive clip, its ticks, its duration and its events.</summary>
    private static (string? Clip,int Ticks,double Duration,List<Dictionary<string,object?>> Events) Animation(Dictionary<string,object?> model,LskSkeleton skeleton,long state)
    {
        var choice = Positive(States(model).First(s => L(s["id"]) == state)).First();
        double duration = ClipNamed(skeleton,choice["clip_name"]).Duration;
        var events = Events(model);
        return (choice["clip_name"] as string,Ceil20(duration),duration,[.. EventIndices(choice).Select(i => events[i])]);
    }

    private JsonArray NatureAbilities(LqrRecord unit,Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        var gears = new Dictionary<string,(int Gear,int Kind,string Name,string? Technique)[]>
        {
            ["UNIT_G_BEORNING"] = [(32,23,"Bear Form","TECHNIQUE_BEAR_FORM")],
            ["UNIT_G_BEAR"] = [(31,23,"Human Form",null),(28,25,"Savage Blow","TECHNIQUE_SAVAGE_BLOW")],
            ["UNIT_G_HUORN"] = [(33,26,"Treeform","TECHNIQUE_TREEFORM")],
        };
        var result = new List<JsonObject>();
        foreach (var (gearId,kind,name,technique) in gears[unit.Name])
        {
            var gear = ById(T("BattleGear"),gearId);
            var spell = ById(T("Spells"),I(gear,"SpellType"));
            var actions = Graph(spell);
            List<LqrRecord> Of(string suffix) => Typed(actions,suffix);
            var (clip,ticks,duration,events) = Animation(model,skeleton,I(Of("_PLAY_ANIMATION")[0],"UnitAnimType"));
            var tools = actions.Select(p => I(p.Event,"ToolscriptType")).Where(t => t >= 0).ToHashSet();
            double release = events.Where(e => tools.Contains((int)L(e["type"]))).Select(e => D(e["time"])).Cast<double?>().FirstOrDefault() ?? 0;
            var sounds = new JsonArray([.. Of("_PLAY_SOUND_EVENT").Select(a => (JsonNode?)I(a,"SoundEventType"))
                .Concat(events.Where(e => Name(e) == "sound").Select(e => Py(e["sound"])))]);
            var ability = new JsonObject
            {
                ["Id"] = gear.Id, ["Slot"] = V(gear,"SpecificIndex"), ["Name"] = name, ["Icon"] = UvaStem(I(gear,"PortraitBorderless")), ["Kind"] = kind,
                ["Passive"] = false, ["RechargeTicks"] = Ceil20(F(spell,"RechargeTime")), ["RangeMm"] = 10000, ["CastClip"] = clip, ["CastTicks"] = ticks,
                ["EffectTick"] = Math.Min(ticks-1,Ceil(duration*release*20)), ["SoundEvents"] = sounds.DeepClone(),
                ["Description"] = technique != null ? Description(technique) : "Returns to human form, allowing the Beorning to heal wounded allies.",
                ["Source"] = new JsonObject { ["Gear"] = Values(gear), ["Spell"] = Values(spell), ["SoundEvents"] = sounds, ["Actions"] = ActionValues(actions) },
            };
            if (technique != null) ability["RequiredTechnique"] = technique;
            if (kind == 23) ability["MorphUnit"] = ById(T("Units"),I(Of("_APPLY_MORPH")[0],"MorphUnitType")).Name;
            else if (kind == 25)
            {
                var damage = Of("_APPLY_DAMAGE")[0];
                var knock = Of("_APPLY_KNOCKBACK")[0];
                ability["HealMilli"] = Times(damage,"DamageAmount",1000); ability["AreaRadiusMm"] = Round(F(damage,"OccuranceRadius")*1000);
                ability["ArcDegrees"] = Round(F(damage,"OccuranceRadiusFacingAngle")); ability["KnockbackSpeedMm"] = Times(knock,"KnockbackForce",1000);
                ability["KnockbackAngle"] = Round(F(knock,"KnockbackAngle"));
            }
            else
            {
                var loop = Animation(model,skeleton,91).Clip;
                var (exitClip,exitTicks,_,exitEvents) = Animation(model,skeleton,92);
                var aura = Of("_SPAWN_CHILD_SPELL").First(a => B(a,"OccursAtAllUnitsInRadius"));
                var endSounds = exitEvents.Where(e => Name(e) == "sound").Select(e => Py(e["sound"])).ToList();
                ability["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[^1],"Float_01")); ability["AreaRadiusMm"] = Round(F(aura,"OccuranceRadius")*1000);
                ability["DpsMilli"] = Round(F(Of("_HEAL")[0],"Float_01")*1000); ability["LoopClip"] = loop; ability["ExitClip"] = exitClip; ability["ExitTicks"] = exitTicks;
                ability["EndSoundEvents"] = new JsonArray([.. endSounds]);
                foreach (var sound in endSounds) ability["Source"]!["SoundEvents"]!.AsArray().Add(sound?.DeepClone());
                ability["SoundEvents"] = new JsonArray([.. events.Where(e => Name(e) == "sound").Select(e => Py(e["sound"]))]);
            }
            result.Add(ability);
        }
        if (unit.Name == "UNIT_G_BEORNING")
        {
            var gear = ById(T("BattleGear"),4);
            var (clip,ticks,duration,events) = Animation(model,skeleton,91);
            var release = events.First(e => L(e["type"]) == 29);
            result.Add(new JsonObject
            {
                ["Id"] = gear.Id, ["Slot"] = V(gear,"SpecificIndex"), ["Name"] = "Healing Herbs", ["Kind"] = 24, ["Passive"] = false,
                ["Icon"] = UvaStem(I(gear,"PortraitBorderless")), ["Cursor"] = "cursor_g_kingsfoil", ["HealMilli"] = Round(F(unit,"HealAmount")*1000),
                ["RechargeTicks"] = Ceil20(F(unit,"TimeBetweenHealing")), ["RangeMm"] = Round(F(unit,"MaxHealRange")*1000),
                ["ResponseRangeMm"] = Round(F(unit,"HealResponseRange")*1000), ["CastClip"] = clip, ["CastTicks"] = ticks,
                ["EffectTick"] = Math.Min(ticks-1,Ceil(duration*D(release["time"])*20)),
                ["Description"] = "Heals a wounded ally for 75 health every 6 seconds. Idle Beornings automatically tend nearby allies.",
                ["Source"] = new JsonObject
                {
                    ["Unit"] = Values(unit), ["Gear"] = Values(gear), ["InnateTechnique"] = Values(Named(T("Techniques"),"TECHNIQUE_HEALING_HERBS")),
                },
            });
        }
        return BySlot(result);
    }

    // ---- evil_abilities, evil_upgrades ---------------------------------------------------------------------------

    private static readonly Dictionary<string,Dictionary<int,string>> EvilHeroGear = new()
    {
        ["UNIT_E_HERO_GOLLUM"] = new() { [37] = "Sneak", [38] = "Surprise Attack", [20] = "Dark Tracking" },
        ["UNIT_E_HERO_NAZGUL_KING"] = new() { [8] = "Aura of Terror", [29] = "Shadow Walk", [36] = "Shadow Bind" },
        ["UNIT_E_HERO_QAMARA"] = new() { [39] = "Mystic Blades", [35] = "Poison Trap", [21] = "Blood Mark" },
        ["UNIT_E_HERO_GRISHNAKH"] = new() { [13] = "Speed of the Wolf", [34] = "Satchel Charge", [5] = "Torch" },
        ["UNIT_E_HERO_SARUMAN"] = new() { [46] = "Prison of Ice", [30] = "Invisible Force", [47] = "Pestilence", [18] = "Twisting Influence" },
    };
    private static readonly Dictionary<int,string> EvilGearDescriptions = new()
    {
        [8] = "Enemies within 8 m lose 2 armor.", [13] = "Grishnakh runs and strikes 50% faster for 10 seconds.",
    };
    private const int KindUnsimulated = 8;

    /// <summary>evil_abilities.cast_clip: the cast clip, its ticks, the tick the spell applies and its sounds.</summary>
    private JsonObject CastClip(List<(LqrRecord Event,LqrRecord Action)> actions,Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        long stateId = actions.Select(p => p.Action).Where(a => ActionType(a).EndsWith("_PLAY_ANIMATION",StringComparison.Ordinal)).Select(a => I(a,"UnitAnimType")).First();
        var choice = Positive(StateById(model,stateId)).First();
        var clip = ClipNamed(skeleton,choice["clip_name"]);
        var tools = actions.Select(p => I(p.Event,"ToolscriptType")).Where(t => t >= 0).ToHashSet();
        var all = Events(model);
        var events = EventIndices(choice).Select(i => all[i]).ToList();
        var release = events.FirstOrDefault(e => tools.Contains((int)L(e["type"]))) ?? events.First(e => Name(e) == "missile_release");
        int ticks = Ceil20(clip.Duration);
        var sounds = actions.Select(p => p.Action).Where(a => ActionType(a).EndsWith("_PLAY_SOUND_EVENT",StringComparison.Ordinal) && I(a,"SoundEventType") >= 0)
            .Select(a => I(a,"SoundEventType")).Concat(EventIndices(choice).Where(i => Name(all[i]) == "sound").Select(i => (int)L(all[i]["sound"])));
        return new JsonObject
        {
            ["CastClip"] = clip.Name, ["CastTicks"] = ticks, ["EffectTick"] = Math.Min(ticks-1,Ceil(clip.Duration*D(release["time"])*20)),
            ["SoundEvents"] = IntArray(sounds.Distinct().Order()),
        };
    }

    private static void Update(JsonObject target,JsonObject values)
    {
        foreach (var (key,value) in values) target[key] = value?.DeepClone();
    }

    private JsonArray EvilHeroAbilities(LqrRecord unit,Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        var abilities = new List<JsonObject>();
        foreach (int gearId in Enumerable.Range(1,4).Select(i => I(unit,$"BattleGear{i}")))
        {
            if (gearId < 0) continue;
            var gear = ById(T("BattleGear"),gearId);
            var spell = ById(T("Spells"),I(gear,"SpellType"));
            var target = T("SpellValidTargets").Get(I(spell,"ValidTargetType"));
            var actions = Graph(spell);
            List<LqrRecord> Of(string suffix) => Typed(actions,suffix);
            JsonArray Excluded() => Strings(new[] { 1,2 }.Select(i => I(target!,$"ExcludeUnitType{i}")).Where(u => u >= 0).Select(u => ById(T("Units"),u).Name));
            JsonArray Specific(LqrRecord t) => IntArray(Enumerable.Range(1,4).Select(i => I(t,$"SpecificUnitType{i}")).Where(u => u >= 0));
            LqrRecord InGraph(LqrRecord s,string suffix) => Graph(s).Select(p => p.Action).First(a => ActionType(a).EndsWith(suffix,StringComparison.Ordinal));
            JsonObject Cast() => CastClip(actions,model,skeleton);
            bool active = I(spell,"UsageType") != 0;
            var cursor = I(spell,"TargetCursor") >= 0 ? T("Cursors").Get(I(spell,"TargetCursor")) : null;
            var ability = new JsonObject
            {
                ["Id"] = gear.Id, ["Slot"] = V(gear,"SpecificIndex"), ["Name"] = EvilHeroGear[unit.Name][gear.Id], ["Icon"] = UvaStem(I(gear,"PortraitBorderless")),
                ["Passive"] = !active, ["Kind"] = KindUnsimulated, ["Description"] = EvilGearDescriptions.GetValueOrDefault(gear.Id,""),
                ["RechargeTicks"] = Ceil20(F(spell,"RechargeTime")), ["RangeMm"] = target != null ? Round(F(target,"MaxSpecifiedRange")*1000) : 0,
                ["Cursor"] = cursor != null ? WindowsStem(S(cursor,"Filename")) : "", ["Purchasable"] = B(gear,"IsPurchasable"),
                ["PurchaseFateCost"] = B(gear,"IsPurchasable") ? V(gear,"PurchaseCostPower") : 0, ["ExperiencePpm"] = Ppm(F(spell,"ExperienceModifier")),
                ["Source"] = new JsonObject
                {
                    ["Gear"] = Values(gear), ["Spell"] = Values(spell), ["Target"] = target != null ? Values(target) : null, ["Actions"] = ActionValues(actions),
                },
            };
            var source = ability["Source"]!.AsObject();
            void Set(params (string Key,JsonNode? Value)[] values) { foreach (var (key,value) in values) ability[key] = value; }
            switch (gear.Id)
            {
                case 13:
                    Set(("Kind",30),("Passive",false),("BonusScale",Ppm(F(Of("_APPLY_TIMESCALE_MULTIPLIER")[0],"Float_01"))),
                        ("DurationTicks",Ceil20(F(Of("_START_TIMER")[0],"Float_01"))),("AreaRadiusMm",500),("RangeMm",0));
                    break;
                case 8:
                {
                    var child = ById(T("Spells"),I(Of("_SPAWN_CHILD_SPELL")[0],"SpellType"));
                    var armor = InGraph(child,"_SET_ARMOR");
                    Set(("Kind",35),("Passive",true),("ArmorReductionMilli",Round(-F(armor,"Float_01")*1000)),("RangeMm",Round(F(target!,"MaxSpecifiedRange")*1000)));
                    break;
                }
                case 46:
                    Set(("Kind",36),("Passive",false),("ExcludedUnits",Excluded()));
                    Update(ability,Cast());
                    Set(("DurationTicks",Ceil20(F(Of("_SET_FROZEN")[0],"Float_01"))),("HealMilli",Round(F(Of("_APPLY_DAMAGE")[0],"DamageAmount")*1000)),
                        ("Description","Freezes an enemy within 15 m solid for 15 seconds and hurts it for 25."));
                    break;
                case 30:
                {
                    var child = ById(T("Spells"),I(Of("_SPAWN_CHILD_SPELL")[0],"SpellType"));
                    var kinds = new Dictionary<string,LqrRecord>();
                    foreach (var (_,a) in Graph(child)) kinds[ActionType(a)] = a;
                    var knock = kinds["SPELL_ACTION_APPLY_KNOCKBACK"];
                    var hurt = kinds["SPELL_ACTION_APPLY_DAMAGE"];
                    Set(("Kind",37),("Passive",false));
                    Update(ability,Cast());
                    Set(("AreaRadiusMm",Round(F(knock,"OccuranceRadius")*1000)),("KnockbackSpeedMm",Round(F(knock,"KnockbackForce")*1000)),
                        ("KnockbackAngle",Round(F(knock,"KnockbackAngle"))),("HealMilli",Round(F(hurt,"DamageAmount")*1000)),
                        ("Description","Hurls an unseen force at a spot within 15 m: enemy units (not heroes) within 10 m of it take 45 damage and are thrown back."));
                    break;
                }
                case 47:
                {
                    var spawn = Of("_SPAWN_CHILD_SPELL").First(a => F(a,"OccuranceRadius") > 0);
                    var child = ById(T("Spells"),I(spawn,"SpellType"));
                    var drain = InGraph(child,"_UPDATE_HEALTH");
                    var timer = InGraph(child,"_START_TIMER");
                    Set(("Kind",38),("Passive",false));
                    Update(ability,Cast());
                    Set(("AreaRadiusMm",Round(F(spawn,"OccuranceRadius")*1000)),("DurationTicks",Ceil20(F(timer,"Float_01"))),("DpsMilli",Round(-F(drain,"Float_01")*1000)),
                        ("Description","Sickens every enemy within 7.5 m of a spot within 15 m: 3 damage a second for 40 seconds, which never kills."));
                    break;
                }
                case 18:
                    Set(("Kind",39),("Passive",false),("ExcludedUnits",Excluded()));
                    Update(ability,Cast());
                    Set(("Description","Bends the will of an enemy unit within 10 m (not a hero, a Giant Ent or a Balrog): it joins your army."));
                    break;
                case 36:
                {
                    var child = ById(T("Spells"),I(Of("_SPAWN_CHILD_SPELL")[0],"SpellType"));
                    var morph = InGraph(child,"_APPLY_MORPH");
                    Set(("Kind",40),("Passive",false));
                    Update(ability,Cast());
                    Set(("BonusTargets",Specific(target!)),("MorphUnit",ById(T("Units"),I(morph,"MorphUnitType")).Name),("Limit",V(target!,"MaxUnitsOfType")),
                        ("ActivateFateCost",V(gear,"ActivateCostPower")),
                        ("Description","Turns one of your Wraiths within 5 m into a Black Rider for 1 Fate. At most eight Black Riders may stand at once."));
                    break;
                }
                case 29:
                {
                    var affected = Of("_ADD_TARGETS_TO_AFFECTED_UNITS_LIST")[0];
                    var carried = ById(T("SpellValidTargets"),I(affected,"ValidTargetTypeForUnitsInRadius"));
                    var sounds = Of("_PLAY_SOUND_EVENT").Select(a => I(a,"SoundEventType")).ToList();
                    Set(("Kind",41),("Passive",false),("AreaRadiusMm",Round(F(spell,"MaxAreaOfEffectRange")*1000)),("CarryRadiusMm",Round(F(affected,"OccuranceRadius")*1000)),
                        ("DelayTicks",Ceil20(F(Of("_START_TIMER")[0],"Float_01"))),("BonusTargets",Specific(carried)),("SoundEvents",IntArray(sounds.Take(1))),
                        ("EndSoundEvents",IntArray(sounds.Skip(1))),
                        ("Description","Instantly carries the Nazgul King and the Wraiths and Black Riders within 15 m of him to a spot within 10 m of one of your War Posts."));
                    break;
                }
                case 39:
                {
                    var weapon = ById(T("Weapons"),I(Of("_SWITCH_MISSILE_WEAPON")[0],"WeaponType"));
                    Set(("Kind",42),("Passive",true),("ChainTargets",V(weapon,"MaxChainTargets")),("RangeMm",Round(F(weapon,"MaxChainTargetDistance")*1000)),
                        ("ChainStartRangeMm",Round(F(weapon,"MaxChainStartDistance")*1000)),("BonusScale",Ppm(F(weapon,"ChainTargetDamageReduction"))),
                        ("DpsMilli",Round(F(weapon,"CuttingDamagePerSecond")*1000)),("Purchasable",false),("PurchaseFateCost",0),
                        ("Description","Saleme throws mystic blades for 20 damage up to 14 m. Each blade ricochets into up to five more enemies, each within 10 m of the one before, for 30% of the hit."));
                    source["Weapon"] = Values(weapon);
                    break;
                }
                case 21:
                {
                    var found = Walk(spell);
                    var updates = Typed(found,"_UPDATE_HEALTH");
                    var timers = Typed(found,"_START_TIMER");
                    var post = ById(T("SpellValidTargets"),22);
                    Set(("Kind",43),("Passive",false));
                    Update(ability,Cast());
                    Set(("DpsMilli",Round(F(updates[0],"Float_01")*1000)),("DurationTicks",Ceil20(F(timers[0],"Float_01"))),
                        ("FieldDpsMilli",Round(F(updates[1],"Float_01")*1000)),("FieldDurationTicks",Ceil20(F(timers[1],"Float_01"))),
                        ("AreaRadiusMm",Round(F(post,"MaxSpecifiedRange")*1000)),
                        ("Description","Heals an ally within 5 m by 13 a second for 12 seconds; on a War Post it heals the allies within 12 m by 5 a second for 15 seconds."));
                    break;
                }
                case 35:
                {
                    var dropped = ById(T("Objects"),I(Of("_DROP_OBJECT")[0],"Object"));
                    var trap = ById(T("Spells"),I(dropped,"DefaultSpellObject"));
                    var inside = Walk(trap);
                    var cloud = inside.Select(p => p.Action).First(a => F(a,"OccuranceRadius") > 0 && ActionType(a).EndsWith("_SPAWN_CHILD_SPELL",StringComparison.Ordinal));
                    var hit = Typed(inside,"_APPLY_DAMAGE")[0];
                    var weapon = ById(T("Weapons"),I(hit,"WeaponType"));
                    var poison = ById(T("Poisons"),I(weapon,"PoisonType"));
                    var sprung = inside.Select(p => p.Event).First(e => e.Name.EndsWith("_ONUNITENTEREDRADIUS",StringComparison.Ordinal));
                    Set(("Kind",44),("Passive",false));
                    Update(ability,Cast());
                    Set(("ResponseRangeMm",Round(F(ById(T("SpellValidTargets"),I(sprung,"ValidTargetForUnitInRadius")),"MaxSpecifiedRange")*1000)),
                        ("AreaRadiusMm",Round(F(cloud,"OccuranceRadius")*1000)),("DpsMilli",Round(F(poison,"DamagePerSecond")*1000)),
                        ("DurationTicks",Ceil(F(poison,"Duration")/50)),("Limit",V(dropped,"MaxObjectsPerPlayer")),
                        ("Description","Lays a hidden trap at her feet (three at most). An enemy within 4 m springs it: every enemy within 10 m is poisoned for 6 a second for 25 seconds."));
                    source["Weapon"] = Values(weapon);
                    source["Poison"] = Values(poison);
                    break;
                }
                case 5:
                {
                    var weapon = ById(T("Weapons"),I(Of("_SWITCH_MISSILE_WEAPON")[0],"WeaponType"));
                    Set(("Kind",45),("Passive",false));
                    Update(ability,Cast());
                    Set(("RangeMm",Round(F(weapon,"MaximumRange")*1000)),("HealMilli",Round(F(weapon,"CuttingDamagePerSecond")*1000)),
                        ("FirePpmPerSecond",Ppm(F(weapon,"FireDamagePerSecond"))),("Description","Hurls a torch at an enemy building within 15 m, setting it alight at once."));
                    source["Weapon"] = Values(weapon);
                    break;
                }
                case 34:
                {
                    var dropped = ById(T("Objects"),I(Of("_DROP_OBJECT")[0],"Object"));
                    var kinds = new Dictionary<string,List<LqrRecord>>();
                    foreach (var (_,a) in Walk(ById(T("Spells"),I(dropped,"ActivationSpellObject"))))
                    {
                        if (!kinds.TryGetValue(ActionType(a),out var list)) kinds[ActionType(a)] = list = [];
                        list.Add(a);
                    }
                    var damage = kinds["SPELL_ACTION_APPLY_DAMAGE"];
                    if (damage.Count != 2) throw new InvalidDataException("satchel charge damage actions");
                    var knock = kinds["SPELL_ACTION_APPLY_KNOCKBACK"][0];
                    Set(("Kind",46),("Passive",false));
                    Update(ability,Cast());
                    Set(("AreaRadiusMm",Round(F(damage[0],"OccuranceRadius")*1000)),("HealMilli",Round(F(damage[0],"DamageAmount")*1000)),
                        ("BuildingDamageMilli",Round(F(damage[1],"DamageAmount")*1000)),("KnockbackSpeedMm",Round(F(knock,"KnockbackForce")*1000)),
                        ("KnockbackAngle",Round(F(knock,"KnockbackAngle"))),("DelayTicks",Round(F(dropped,"Lifespan")*20)),("Limit",V(dropped,"MaxObjectsPerPlayer")),
                        ("Description","Drops a satchel charge at his feet (two at most). It blows up after 5 seconds, or when he presses the button again: enemies within 4 m take 70 and are thrown back, enemy buildings take 35."));
                    break;
                }
                case 37:
                {
                    var found = Walk(spell);
                    LqrRecord Find(string suffix) => Typed(found,suffix)[0];
                    Set(("Kind",47),("Passive",false),("BonusScale",Ppm(F(Find("_APPLY_TIMESCALE_MULTIPLIER"),"Float_01"))),
                        ("DamageTakenPpm",Ppm(F(Find("_APPLY_DAMAGE_MULTIPLIER"),"Float_01"))),("AlphaPpm",500_000),("DelayTicks",Ceil20(F(Find("_START_TIMER"),"Float_01"))),
                        ("Description","Gollum slips out of sight: invisible to enemies without detection, he does not fight on his own, creeps at half speed and takes double damage. Moving does not give him away; a second after his first blow he is seen again."));
                    break;
                }
                case 38:
                {
                    var sneak = ById(T("Spells"),I(ById(T("BattleGear"),37),"SpellType"));
                    var weapon = ById(T("Weapons"),I(Typed(Walk(sneak),"_SWITCH_MELEE_WEAPON")[0],"WeaponType"));
                    Set(("Kind",48),("Passive",true),("DpsMilli",Round(F(weapon,"CuttingDamagePerSecond")*1000)),
                        ("Description","While he sneaks, Gollum strikes with a surprise weapon for 45 damage a second instead of 15."));
                    source["Weapon"] = Values(weapon);
                    break;
                }
                case 20:
                {
                    var child = ById(T("Spells"),I(Of("_SPAWN_CHILD_SPELL")[0],"SpellType"));
                    var timer = InGraph(child,"_START_TIMER");
                    Set(("Kind",49),("Passive",false));
                    Update(ability,Cast());
                    Set(("DurationTicks",Ceil20(F(timer,"Float_01"))),("Limit",V(child,"MaxNumAffectedGlobally")),
                        ("Description","Marks an enemy within 15 m for up to 200 seconds (three at most): Gollum, and his side, see it wherever it goes, hidden or not."));
                    break;
                }
            }
            if (Int(ability["Kind"]) == KindUnsimulated) { ability["Purchasable"] = false; ability["PurchaseFateCost"] = 0; }
            abilities.Add(ability);
        }
        return BySlot(abilities);
    }

    private static readonly Dictionary<string,string[]> EvilTechniques = new()
    {
        ["UNIT_E_ORC_BOWMAN"] = ["TECHNIQUE_FLAME_ARROWS"], ["UNIT_E_GOBLIN_SLAVE_MASTER"] = ["TECHNIQUE_SAVAGE_INFLUENCE"],
        ["UNIT_E_HARADRIM_ASSASSIN"] = ["TECHNIQUE_POISON_DART", "TECHNIQUE_ASSASSINS_CLOAK"], ["UNIT_E_GIANT_SPIDER"] = ["TECHNIQUE_PARALYZE", "TECHNIQUE_SPRAY_VENOM"],
        ["UNIT_E_TROLL_BONECLEAVER"] = ["TECHNIQUE_SWEEPING_AXE"], ["UNIT_E_WRAITH"] = ["TECHNIQUE_DARKNESS", "TECHNIQUE_PERPETUAL_DARKNESS", "TECHNIQUE_GREATER_PERCEPTION"],
        ["UNIT_E_BONUS_BLACK_RIDER"] = ["TECHNIQUE_MORGUL_BLADE"],
    };

    private JsonArray EvilUpgrades(LqrRecord unit,Dictionary<string,object?> model,LskSkeleton skeleton)
    {
        var result = new JsonArray();
        foreach (string name in EvilTechniques.GetValueOrDefault(unit.Name) ?? [])
        {
            var technique = Named(T("Techniques"),name);
            var gear = T("BattleGear").Get(I(technique,"AddBattleGear"));
            var a = new JsonObject
            {
                ["Id"] = gear != null ? gear.Id : -1000-technique.Id, ["Slot"] = gear != null ? V(gear,"SpecificIndex") : V(technique,"SpecificIndexForUVA"),
                ["Name"] = Title(name["TECHNIQUE_".Length..].Replace('_',' ')).Replace("Assassins","Assassin's"),
                ["Icon"] = UvaStem(gear != null ? I(gear,"PortraitBorderless") : I(technique,"PortraitBorderless")), ["Passive"] = true, ["RequiredTechnique"] = name,
                ["Description"] = Description(name), ["Source"] = new JsonObject { ["Technique"] = Values(technique) },
            };
            var source = a["Source"]!.AsObject();
            LqrRecord? spell = null;
            List<(LqrRecord Event,LqrRecord Action)> actions = [];
            List<LqrRecord> Of(string suffix) => Typed(actions,suffix);
            if (gear != null)
            {
                spell = ById(T("Spells"),I(gear,"SpellType"));
                actions = Walk(spell);
                source["Gear"] = Values(gear); source["Spell"] = Values(spell);
                source["Actions"] = new JsonArray([.. actions.Select(p => (JsonNode?)Values(p.Action))]);
                var cursor = T("Cursors").Get(I(spell,"TargetCursor"));
                a["Cursor"] = cursor != null ? WindowsStem(S(cursor,"Filename")) : "";
            }
            switch (name)
            {
                case "TECHNIQUE_FLAME_ARROWS":
                    a["Kind"] = 57; a["FirePpmPerSecond"] = Ppm(F(ById(T("Weapons"),I(technique,"MissileWeapon")),"FireDamagePerSecond"));
                    break;
                case "TECHNIQUE_ASSASSINS_CLOAK":
                    a["Kind"] = 18; a["AlphaPpm"] = 500000;
                    source["Adapter"] = "GUIDE.txt: same invisibility as Elven Cloaks; no effect in the hidden technique row.";
                    break;
                case "TECHNIQUE_GREATER_PERCEPTION":
                    a["Kind"] = 17;
                    break;
                case "TECHNIQUE_MORGUL_BLADE":
                    a["Kind"] = 50;
                    foreach (var (key,value) in Poison(_db,ById(T("Weapons"),I(Of("_SWITCH_MELEE_WEAPON")[0],"WeaponType")))) a[key] = value;
                    break;
                case "TECHNIQUE_PARALYZE":
                {
                    var weapon = ById(T("Weapons"),I(technique,"MeleeWeapon"));
                    var baseWeapon = ById(T("Weapons"),I(unit,"MeleeWeapon"));
                    a["Kind"] = 56; a["DurationTicks"] = Ceil20(F(weapon,"StunDuration"));
                    a["RangeMm"] = Round((F(weapon,"MaximumRange")-F(baseWeapon,"MaximumRange"))*1000);
                    break;
                }
                default:
                {
                    a["Passive"] = false; a["RechargeTicks"] = Ceil20(F(spell!,"RechargeTime")); a["RangeMm"] = 10000;
                    Update(a,CastClip(actions,model,skeleton));
                    if (name == "TECHNIQUE_POISON_DART")
                    {
                        var weapon = ById(T("Weapons"),I(Of("_SWITCH_MISSILE_WEAPON")[0],"WeaponType"));
                        var target = ById(T("SpellValidTargets"),I(spell!,"ValidTargetType"));
                        a["Kind"] = 51; a["RangeMm"] = Round(F(target,"MaxSpecifiedRange")*1000);
                        a["ActivateFoodCost"] = V(gear!,"ActivateCostFood"); a["ActivateOreCost"] = V(gear!,"ActivateCostOre");
                        foreach (var (key,value) in Poison(_db,weapon)) a[key] = value;
                    }
                    else if (name == "TECHNIQUE_SAVAGE_INFLUENCE")
                    {
                        a["Kind"] = 52; a["AreaRadiusMm"] = Round(F(Of("_SPAWN_CHILD_SPELL")[0],"OccuranceRadius")*1000);
                        a["HealMilli"] = Round(-F(Of("_APPLY_HEALTH")[0],"Float_01")*1000); a["BonusScale"] = Ppm(F(Of("_APPLY_DAMAGE_MULTIPLIER")[0],"Float_01"));
                        a["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01"));
                    }
                    else if (name is "TECHNIQUE_DARKNESS" or "TECHNIQUE_PERPETUAL_DARKNESS")
                    {
                        a["Kind"] = 53; a["AreaRadiusMm"] = Round(F(Of("_SPAWN_CHILD_SPELL")[0],"OccuranceRadius")*1000);
                        a["BonusScale"] = Ppm(F(Of("_APPLY_TIMESCALE_MULTIPLIER")[0],"Float_01")); a["DurationTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01"));
                        a["Blinds"] = Of("_SET_BLIND").Count > 0;
                        if (name == "TECHNIQUE_DARKNESS") a["ReplacedByTechnique"] = "TECHNIQUE_PERPETUAL_DARKNESS";
                    }
                    else if (name == "TECHNIQUE_SPRAY_VENOM")
                    {
                        var stun = Of("_SET_DAZED")[0];
                        a["Kind"] = 54; a["AreaRadiusMm"] = Round(F(stun,"OccuranceRadius")*1000); a["ArcDegrees"] = Round(F(stun,"OccuranceRadiusFacingAngle"));
                        a["DurationTicks"] = Ceil20(F(stun,"Float_01")); a["DelayTicks"] = Ceil20(F(Of("_START_TIMER")[0],"Float_01"));
                    }
                    else if (name == "TECHNIQUE_SWEEPING_AXE")
                    {
                        var damage = Of("_APPLY_DAMAGE")[0];
                        a["Kind"] = 55; a["AreaRadiusMm"] = Round(F(damage,"OccuranceRadius")*1000); a["HealMilli"] = Times(damage,"DamageAmount",1000);
                    }
                    break;
                }
            }
            result.Add(a);
        }
        return result;
    }
}
