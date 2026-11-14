using System.Globalization;
using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-201: the sprite effects (tools/healing_visual.py export_sprite_effect and the visuals of
/// elven_abilities, nature_abilities, hero_skills, evil_abilities and global_powers), the global Fate powers
/// (global_powers.export) and the dropped objects' model files. A sprite effect's frames are named as the import writes
/// them; each frame's atlas rectangle is checked against the texture's size, as the import's crop does.</summary>
public sealed partial class PackCombat
{
    private const string SpritePolicy = "Original atlas crops, layer sizes/colors and durations. Bounded deterministic sprite motion and blending adapt legacy particle emission/transform fields; exact original motion and blending remain unverified.";

    private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>prototype_interface.crop_rect: an atlas rectangle in pixels, which must lie inside the atlas.</summary>
    internal static (long Left,long Top,long Right,long Bottom) CropRect(double x,double y,double w,double h,int width,int height)
    {
        long left = Round(x*width), top = Round(y*height), right = Round((x+w)*width), bottom = Round((y+h)*height);
        if (left < 0 || top < 0 || right > width || bottom > height || right <= left || bottom <= top) throw new InvalidDataException("UVA rectangle outside atlas");
        return (left,top,right,bottom);
    }

    /// <summary>healing_visual.layer_keys: a texture track's keys by time, with sprite size and colour.</summary>
    private static JsonArray LayerKeys(Dictionary<string,object?> track)
    {
        var keys = new JsonArray();
        foreach (var key in Dicts(track["keys"]).OrderBy(k => D(k["time"])))
        {
            var words = List(key["words"]);
            double time = D(key["time"]);
            double width = BitConverter.UInt32BitsToSingle((uint)L(words[4])), height = BitConverter.UInt32BitsToSingle((uint)L(words[5]));
            if (!double.IsFinite(time) || !double.IsFinite(width) || !double.IsFinite(height) || time is < 0 or > 1 || width is <= 0 or > 100 || height is <= 0 or > 100)
                throw new InvalidDataException("invalid healing sprite key");
            keys.Add(new JsonObject { ["Time"] = time, ["Width"] = width, ["Height"] = height, ["ColorArgb"] = L(words[10]) });
        }
        if (keys.Count == 0) throw new InvalidDataException("empty healing sprite timeline");
        return keys;
    }

    /// <summary>healing_visual.export_sprite_effect: an effect's drawable texture tracks as frame lists with their
    /// keys, and the effect's reading as its source.</summary>
    private JsonObject SpriteEffect(string effectName,string folder,bool clipAtlasEdges = false,ICollection<string>? trackNames = null)
    {
        var record = Named(T("Effects"),effectName);
        string source = Find("Effects",S(record,"Filename")) ?? throw new FileNotFoundException("missing effect "+effectName);
        var effect = Effect(source);
        Trace(source);
        TraceTable("Effects");
        var layers = new JsonArray();
        foreach (var track in Dicts(effect["texture_tracks"]))
        {
            string name = (string)track["name"]!;
            if (trackNames != null && !trackNames.Contains(name)) continue;
            var key = Dict(List(track["keys"])[0]);
            if (!key.TryGetValue("uva",out var uvaValue)) continue;
            var uva = Dict(uvaValue);
            if ((int)uva["texture"]! < 0) continue;
            string texture = (string)_resolver.Texture((int)uva["texture"]!)!["file"]!;
            Trace(texture);
            var (atlasWidth,atlasHeight) = ImageSize(texture);
            var rect = List(uva["rect"]).Select(D).ToArray();
            double x = rect[0], y = rect[1], width = rect[2], height = rect[3];
            long frameCount = L(uva["frame_count"]), perRow = L(uva["frames_per_row"]);
            var frames = new JsonArray();
            for (long frame = 0; frame < frameCount; frame++)
            {
                double ax = x+frame%perRow*width, ay = y+frame/perRow*height, aw = width, ah = height;
                if (clipAtlasEdges)
                {
                    // Blessed Wind's shipped hair track extends 1/64 beyond the atlas at its last column.
                    if (Math.Min(ax,ay) < 0 || Math.Max(ax+aw,ay+ah) > 1.02) throw new InvalidDataException("source effect atlas overhang exceeds two percent");
                    (aw,ah) = (Math.Min(aw,1-ax),Math.Min(ah,1-ay));
                }
                CropRect(ax,ay,aw,ah,atlasWidth,atlasHeight);
                frames.Add($"combat/{folder}/{name}_{frame}.png");
            }
            layers.Add(new JsonObject { ["Name"] = name, ["DurationMs"] = Py(track["duration_ms"]), ["Frames"] = frames, ["Keys"] = LayerKeys(track) });
        }
        return new JsonObject
        {
            ["DurationMs"] = Py(effect["duration_ms"]), ["Layers"] = layers,
            ["Source"] = new JsonObject { ["Id"] = record.Id, ["Name"] = record.Name, ["File"] = source, ["Data"] = Py(effect) },
            ["Policy"] = SpritePolicy,
        };
    }

    private static List<string> EmitterNames(JsonObject data) => [.. data["Source"]!["Data"]!["particle_emitters"]!.AsArray().Select(e => e!["name"]!.GetValue<string>())];
    private static string LayerName(JsonNode? layer) => layer!["Name"]!.GetValue<string>();

    /// <summary>Keeps the layers a predicate accepts.</summary>
    private static void Keep(JsonObject data,Func<string,bool> keep)
        => data["Layers"] = new JsonArray([.. data["Layers"]!.AsArray().Where(l => keep(LayerName(l))).Select(l => l!.DeepClone())]);

    private static void Lengthen(JsonObject data)
        => data["DurationMs"] = Math.Max(Int(data["DurationMs"]),data["Layers"]!.AsArray().Max(l => Int(l!["DurationMs"])));

    private static string RemovePrefix(string text,string prefix) => text.StartsWith(prefix,StringComparison.Ordinal) ? text[prefix.Length..] : text;

    /// <summary>elven_abilities.visuals.</summary>
    private JsonObject ElvenVisuals()
    {
        var result = new JsonObject();
        foreach (int i in new[] { 350,349,308,75,402,106,104 })
        {
            var data = SpriteEffect(ById(T("Effects"),i).Name,"elven-effects/"+i);
            var active = EmitterNames(data).Select(n => RemovePrefix(n,"E_").ToLowerInvariant()).ToHashSet();
            Keep(data,name => active.Contains(name.ToLowerInvariant()));
            if (data["Layers"]!.AsArray().Count == 0) throw new InvalidDataException("Elven effect has no bound sprite layers: "+i);
            Lengthen(data);
            result[Key(i)] = data;
        }
        return result;
    }

    /// <summary>nature_abilities.visuals; the roots' model file is named, not written.</summary>
    private JsonObject NatureVisuals()
    {
        var result = new JsonObject();
        foreach (int i in new[] { 134,190,363 })
        {
            var data = SpriteEffect(ById(T("Effects"),i).Name,"nature-effects/"+i);
            static string EmitterName(string name)
            {
                name = RemovePrefix(name.ToLowerInvariant(),"e_");
                return name.StartsWith('e') ? name[1..] : name.EndsWith('e') ? name[..^1] : name;
            }
            var active = EmitterNames(data).Select(EmitterName).ToHashSet();
            Keep(data,name => active.Contains(name.ToLowerInvariant()));
            if (i == 363)
            {
                var track = data["Source"]!["Data"]!["texture_tracks"]!.AsArray().First(t => t!["name"]!.GetValue<string>() == "shard_one");
                _ = ById(T("Models"),Int(track!["keys"]![0]!["texture"]));
                data["RootModel"] = "combat/nature-effects/roots.glb";
            }
            result[Key(i)] = data;
        }
        return result;
    }

    private static string Normalized(string name) => name.ToLowerInvariant().Replace(" ","").Replace("_","");

    /// <summary>hero_skills.visuals and evil_abilities.visuals: layers whose emitter is bound.</summary>
    private JsonObject BoundVisuals(IEnumerable<int> effects,bool pfire)
    {
        var result = new JsonObject();
        foreach (int i in effects)
        {
            var data = SpriteEffect(ById(T("Effects"),i).Name,"hero-effects/"+i);
            var active = EmitterNames(data).Select(n => Normalized(n.Length > 0 ? n[1..] : n)).ToHashSet();
            Keep(data,name => active.Contains(Normalized(name)) || active.Contains(RemovePrefix(Normalized(name),"p")) || pfire && i == 182 && name == "Pfire");
            if (data["Layers"]!.AsArray().Count == 0) continue;
            Lengthen(data);
            result[Key(i)] = data;
        }
        return result;
    }

    private JsonObject HeroVisuals() => BoundVisuals([145,146,253,127,328,142,143,185,186,177,178,180,182,388,176,335],true);
    private JsonObject EvilVisuals() => BoundVisuals([356,197,202,200,196,355,365,364,169,170,183,184,163,164,403,130,159,158,252],false);

    /// <summary>A dropped object's model file entry: Objects -> ModelEvents -> Models.</summary>
    private JsonObject DroppedObject(LqrRecord obj,string folder,int objectId)
    {
        var e = ById(T("ModelEvents"),I(obj,"ModelEvent"));
        var model = ById(T("Models"),I(e,"AssociatedModelResource1"));
        return new JsonObject { ["File"] = folder+model.Name+".glb", ["Object"] = objectId, ["Model"] = model.Name, ["Invisible"] = B(obj,"IsInvisible") };
    }

    /// <summary>evil_abilities.dropped_objects: Saleme's trap and Grishnakh's satchel.</summary>
    private JsonObject EvilDroppedObjects() => new()
    {
        ["trap"] = DroppedObject(ById(T("Objects"),530),"objects/hero-drops/",530),
        ["satchel"] = DroppedObject(ById(T("Objects"),538),"objects/hero-drops/",538),
    };

    /// <summary>global_powers.dropped_objects: the Obelisk.</summary>
    private JsonObject PowerDroppedObjects()
    {
        var obj = ById(T("Objects"),I(ById(T("GlobalSpells"),9),"ObjectType"));
        return new JsonObject { ["obelisk"] = DroppedObject(obj,"objects/global-powers/",obj.Id) };
    }

    // ---- global_powers --------------------------------------------------------------------------------------

    private static readonly int[] GoodPowers = [4, 6, 5, 3, 1], EvilPowers = [7, 8, 2, 9, 0];
    private static readonly Dictionary<int,string> PowerDescriptions = new()
    {
        [4] = "Blinds enemies in 10 m for 10 seconds. They can move but cannot attack.",
        [6] = "Creates brambles in 8 m for 25 seconds. Moving enemies lose 35 HP per second; standing still avoids damage.",
        [5] = "Permanently grants a friendly hero 35% more damage, +2 armor and 50% more maximum health. That hero cannot be summoned again after death.",
        [3] = "Grants friendly missile troops in 10 m knockback on their projectiles for 20 seconds.",
        [1] = "Summons a controllable Giant Ent for 120 seconds. Requires clear ground and costs no population.",
        [7] = "Raises a marsh in 10 m for 15 seconds. Enemy units standing in it move and attack at half speed.",
        [8] = "A murder of crows harries the 12 m area for 22 seconds: enemy missile weapons there do 1% of their damage.",
        [2] = "Heals friendly units in 12 m for 200 health and refreshes every ability they have.",
        [9] = "Raises an Obelisk with 5000 health. Friendly units within 15 m take half of any damage; the Obelisk soaks the other half. It loses 1.5% of its health each second and falls when its health is gone.",
        [0] = "Summons a controllable Balrog for 120 seconds. Requires clear ground and costs no population.",
    };

    /// <summary>global_powers.export: both sides' Fate powers in menu order.</summary>
    private JsonArray GlobalPowers()
    {
        var result = new JsonArray();
        foreach (var (faction,order) in new[] { ("good",GoodPowers),("evil",EvilPowers) })
            for (int slot = 0; slot < order.Length; slot++) result.Add(Power(faction,slot,order[slot]));
        return result;
    }

    private JsonObject Power(string faction,int slot,int ident)
    {
        var row = ById(T("GlobalSpells"),ident);
        var spell = I(row,"Spell") >= 0 ? ById(T("Spells"),I(row,"Spell")) : null;
        var actions = spell != null ? Graph(spell) : [];
        List<LqrRecord> Of(string suffix) => Typed(actions,suffix);
        var timers = Of("_START_TIMER");
        var cursor = I(row,"TargetCursor") >= 0 ? T("Cursors").Get(I(row,"TargetCursor")) : null;
        LqrRecord ChildAction(string suffix)
        {
            var child = ById(T("Spells"),I(actions.Select(p => p.Action).First(a => ActionType(a).EndsWith("_SPAWN_CHILD_SPELL",StringComparison.Ordinal)),"SpellType"));
            return Graph(child).Select(p => p.Action).First(a => ActionType(a).EndsWith(suffix,StringComparison.Ordinal));
        }
        var item = new JsonObject
        {
            ["Id"] = ident, ["Slot"] = slot, ["Name"] = S(row,"Name"), ["Icon"] = UvaStem(I(row,"Portrait")),
            ["Cursor"] = cursor != null ? WindowsStem(S(cursor,"Filename")) : faction == "evil" ? "cursor_e_move" : "cursor_g_move",
            ["Faction"] = faction, ["FateCost"] = V(row,"CostPower"), ["RadiusMm"] = Round(F(row,"AreaOfEffectRadius")*1000),
            ["DurationTicks"] = Round((timers.Count > 0 ? F(timers[0],"Float_01") : F(row,"Duration"))*20), ["RechargeTicks"] = Times(row,"RechargeTime",20),
            ["Description"] = PowerDescriptions[ident],
            ["SoundEvents"] = IntArray(Of("_PLAY_SOUND_EVENT").Select(a => I(a,"SoundEventType")).Distinct().Order()),
            ["BlockedInFog"] = !B(row,"CanCastInFOW"), ["BlockedOverBuildings"] = !B(row,"CanCastOverBuildings"),
            ["Source"] = new JsonObject { ["Global"] = Values(row), ["Spell"] = spell != null ? Values(spell) : null, ["Actions"] = ActionValues(actions) },
        };
        switch (ident)
        {
            case 6: item["DpsMilli"] = Round(-F(Of("_UPDATE_HEALTH")[0],"Float_01")*1000); break;
            case 5:
                item["DamagePpm"] = Ppm(F(Of("_APPLY_DAMAGE_MULTIPLIER")[0],"Float_01")); item["HealthPpm"] = Ppm(F(Of("_APPLY_HITPOINTS_MODIFIER")[0],"Float_01"));
                item["ArmorMilli"] = Round(F(Of("_SET_ARMOR")[0],"Float_01")*1000);
                break;
            case 3:
            {
                var knock = Of("_SET_MISSILE_WEAPON_KNOCKBACK")[0];
                item["KnockbackSpeedMm"] = Times(knock,"KnockbackForce",1000); item["KnockbackAngle"] = Round(F(knock,"KnockbackAngle"));
                break;
            }
            case 1: item["SummonUnit"] = "UNIT_G_GIANT_ENT"; item["SoundEvents"] = IntArray([400]); break;
            case 0: item["SummonUnit"] = "UNIT_E_HERO_BALROG"; item["SoundEvents"] = IntArray([140]); break;
            case 7:
                item["DurationTicks"] = Round(F(Of("_START_TIMER")[0],"Float_01")*20);
                item["TimeScalePpm"] = Ppm(F(ChildAction("_APPLY_TIMESCALE_MULTIPLIER"),"Float_01"));
                break;
            case 8:
                item["DurationTicks"] = Round(F(Of("_START_TIMER")[0],"Float_01")*20);
                item["MissilePpm"] = Ppm(F(ChildAction("_APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER"),"Float_01"));
                break;
            case 2: item["HealMilli"] = Round(F(Of("_APPLY_HEALTH")[0],"Float_01")*1000); item["DurationTicks"] = 0; break;
            case 9:
            {
                var obj = ById(T("Objects"),I(row,"ObjectType"));
                long decay = Round(F(row,"DegenerationRate")*10_000);
                item["ObjectHealth"] = V(obj,"MaxHealth"); item["DegenerationPpm"] = decay;
                item["DamageUnitPpm"] = Ppm(F(row,"DamageUnit")); item["DamageObeliskPpm"] = Ppm(F(row,"DamageObelisk"));
                item["DurationTicks"] = Ceil(1_000_000.0*20/decay);
                item["Source"]!["Object"] = Values(obj);
                break;
            }
        }
        return item;
    }

    private static readonly (int Effect,(string Track,int Count,string Style)[] Tracks)[] PowerTracks =
    [
        (49,[("Flames",7,"add")]),
        (52,[("flame",5,"add"),("embers",4,"add")]),
        (53,[("Flames",4,"mix")]),
        (64,[("Summon_Circle",1,"ground"),("Summon_Fire",16,"add")]),
        (65,[("Wing_Smoke",8,"mix")]),
        (67,[("Smoke",3,"mix"),("embers",4,"add")]),
        (227,[("leaves",12,"mix"),("initialsmoke",7,"mix"),("rocks",5,"mix")]),
        (228,[("leaves",16,"mix"),("moreleaves",8,"mix"),("initialsmoke",8,"mix"),("smoke3",6,"mix"),("rocks",6,"mix"),("dustring",1,"ground"),("light",4,"add")]),
        (306,[("fire",10,"add"),("glowtwo",1,"ground"),("smoke",8,"mix"),("endfire",4,"add")]),
        (391,[("ground",1,"ground"),("ground2",1,"ground")]),
    ];

    /// <summary>global_powers.visuals: the powers' sprite effects, the brambles' model file, and the summon, death and
    /// Balrog body effects with the counts and blends the game draws them with.</summary>
    private JsonObject GlobalVisuals()
    {
        var result = new JsonObject();
        foreach (int ident in new[] { 225,226,400,285,286,287,289,291,191,254 })
        {
            var data = SpriteEffect(ById(T("Effects"),ident).Name,"global-effects/"+ident,clipAtlasEdges: ident == 285);
            static string Norm(string name) => RemovePrefix(RemovePrefix(name.ToLowerInvariant(),"e_"),"e").Replace("_","").Replace(" ","");
            var active = EmitterNames(data).Select(Norm).ToHashSet();
            Keep(data,name => active.Contains(Norm(name)) || active.Contains(name.ToLowerInvariant().Replace("_","").Replace(" ","")) || ident == 286 && name == "Glow");
            if (ident == 287)
            {
                var obj = ById(T("Objects"),556);
                var e = ById(T("ModelEvents"),I(obj,"ModelEvent"));
                _ = ById(T("Models"),I(e,"AssociatedModelResource1"));
                data["AreaModel"] = "combat/global-effects/brambles.glb";
                data["Source"]!["Object"] = Values(obj);
                data["Source"]!["ModelEvent"] = Values(e);
                TraceTable("Objects");
                TraceTable("ModelEvents");
            }
            if (data["Layers"]!.AsArray().Count > 0) result[Key(ident)] = data;
        }
        foreach (var (ident,tracks) in PowerTracks)
        {
            var record = ById(T("Effects"),ident);
            var selected = tracks.ToDictionary(t => t.Track,t => (t.Count,t.Style));
            var data = SpriteEffect(record.Name,"global-effects/"+ident,trackNames: selected.Keys);
            var source = new Dictionary<string,JsonNode>();
            foreach (var track in data["Source"]!["Data"]!["texture_tracks"]!.AsArray()) source[track!["name"]!.GetValue<string>()] = track;
            bool delayed = ident is 64 or 227 or 228 or 306 or 391;
            foreach (var layer in data["Layers"]!.AsArray())
            {
                var (count,style) = selected[LayerName(layer)];
                layer!["SpriteCount"] = count; layer["Ground"] = style == "ground"; layer["Blend"] = style;
                layer["DelayMs"] = delayed ? source[LayerName(layer)]["delay_ms"]!.DeepClone() : 0;
            }
            data["DurationMs"] = data["Layers"]!.AsArray().Max(l => Int(l!["DurationMs"])+Int(l!["DelayMs"]));
            result[Key(ident)] = data;
        }
        return result;
    }


    private static JsonObject Policy() => new()
    {
        ["Armor"] = "Approximation: subtract armor points from DPS after type and explicit pair multipliers; clamp rate at zero.",
        ["Damage"] = "Use one fixed base hit from the rounded HUD damage (weapon DPS times weighted mean seconds between hits), or an explicit per-hit weapon value. Every animation hit uses that value times adjusted DPS / base weapon DPS; variants and source damage_scale affect event timing only. Missile and contact weapons have separate cadences.",
        ["Ranges"] = "Melee center reach is max(legacy 2000 mm, both body radii plus source weapon range as a surface gap). Elven Archer uses source missile minimum/maximum as center distances. Original range-origin semantics remain unverified.",
        ["Collision"] = "Static terrain and enemies use full envelopes. Allies compress personal space to 50% with a solid non-overlapping core; resting formations retain full spacing. Aragorn presentation scales 112%, radius floor 800 mm, height follows scale. Owner-requested feel policy; raw source bounds retained in Source.Collision. Original response semantics remain unverified.",
        ["Types"] = "The source has no type multipliers; its unit-pair rows are the type cycle (150% advantage, 90% reverse, 85% against heroes). A source pair row replaces TypeMatchups; TypeMatchups apply only to pairs without a row (build-61, was stacked). Pair invincibility takes precedence.",
        ["Timing"] = "ceil(source duration * event fraction * 20); cycle ceil(duration*20), final hit at most cycle-1.",
        ["Movement"] = "Run speed keeps the source ratios between units, scaled so the Gondor Swordsman runs at the accepted 3 m/s: RunScale = run clip speed / Swordsman run clip speed, where clip speed is the J_Position XY displacement/duration of the first positive Run choice. Walk speed is the run speed times the same ratio of the first positive Walk/Run choices. Original absolute world speed, random run variants, model scale, slopes and stamina remain unverified.",
        ["Ranged"] = "Source missile weapons, including the Uruk-Hai crossbow, with source release events and minimum/maximum range in center meters. Target-following missiles use an approximate 25 m/s travel speed, snapshot damage at release and survive shooter death/order replacement. Conservative navigation line of fire at release; no flight collision. Switch to source melee weapon/animations at the same body-based reach as infantry; switch cancellation preserves cooldown. Exact switch threshold/timing and LPO motion semantics remain unverified.",
        ["ArrowPresentation"] = "Per ranged unit: the weapon projectile model, its launch sound event from LPO event 7/opcode 46; original PCM variants at release with source minimum volume and voice cap, and the trail of its first bound effect (Archer 12, Axethrower 362). Track duration interpreted as point lifetime, key word 3 as full width, word 5 as ARGB. Runtime uses a 70% gray tint, feathered ribbon and smooth age fade; exact legacy blending and trail semantics remain unverified. Presentation never drives hits.",
        ["Abilities"] = "Hero purchasable gear starts locked and spends its source PurchaseCostPower in Fate; innate gear ignores nominal prices; hero levels use HeroExperienceSource. Greater Kingsfoil heals 250 HP within 10 m at source toolscript tick 8 of its 21-tick clip, cooldown 340 ticks. Out-of-range targets are approached using normal navigation. Casts replace orders and may be interrupted; spent cooldown is retained. Fury replaces DPS with 30 and applies 150% versus source Wraith/Black Rider types. Immunity records poison/stun/blind/slow protection; no current demo weapon produces these statuses. HealVisual adapts original target-effect sprite layers; poison cleansing and other original particle/effect attachments remain unsupported. Exact legacy range origins and interruption remain unverified. Frodo starts with innate Heroic Aura; The One Ring and Sting are purchased separately. The One Ring toggles (Q): OnActivate sets 50% visibility, passive and undetectable (enemies cannot target him) and OnContinuous drains 2 HP per second; the drain stops at 1 HP (adapter, the source has no floor); its OnActivate/OnDeactivate actions disable and re-enable regeneration (SPELL_ACTION_ENABLE_REGENERATION). Sting multiplies damage by 1.35 against source Orc Slasher, Orc Bowman, Uruk-Hai and Goblin Spearman; its blade-glow attachment (flag 31) is not shown. Heroic Aura gives allied units within 15 m a 1.5x ability recharge rate; its ground particle effect 148 is not shown.",
        ["Regeneration"] = "Build-52: a living unit regains its state rate from UnitAnimStates (Idle, Walk, Run, Ready = attack stance, Attack = mid-swing, Gather, Repair = building or repairing, Special = casting; heroes take the Hero column) times its Units HealthRegenerationMultiplier, read as HP per second, every tick and never above its maximum. Nothing while the One Ring is on, inside a Mill or Foundry, or dead. Adapters: the HP-per-second reading of the rates (the base constant is in Rings.exe, unverified), the mapping of simulation states to animation states, and integer milli-HP per tick (fractions dropped). Places of power (Ancient Fountain +1 HP/s) are not simulated.",
        ["Unsupported"] = "Purchase economy, levels, places of power, height bonuses, poison/status-effect producers and cleansing, other original spell particles/attachments, knockback, other ranged units and critical deaths.",
    };
}
