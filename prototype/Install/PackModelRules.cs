using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-199: the content pack's rule sections that need the install's models as well as its database, built
/// at startup beside PackRules' and put in place of the pack's by CacheManifest: the pack's models list (unit, file, the
/// clips moved in place, the idle, walk, attack and death clips: prepare_prototype.export_units), skirmish.economy
/// (skirmish_economy.economy_data: the workers' gather clips) and skirmish.construction except its defenses (those are
/// PackCombat.Defenses', build-204)
/// (skirmish_construction.construction_data without sites and placement_data, with the oriented-collision footprints of
/// skirmish_map.building_footprint). tests/formats --python-rules compares them with tools/rules_fingerprint.py.</summary>
public static class PackModelRules
{
    public static readonly string[] ConstructionKeys = ["definitions", "faction_definitions", "buildings", "order", "palettes", "corruption", "placeable",
        "upgrades", "placement_radius_mm", "placement_adapter"];
    public static readonly string[] Sections = ["models", "skirmish.economy", .. ConstructionKeys.Select(k => "skirmish.construction."+k)];

    public static Dictionary<string,JsonNode> Build(LqrDatabase db,GameArchives archives)
    {
        var resolver = new ModelResolver(db,archives);
        var result = new Dictionary<string,JsonNode> { ["models"] = Models(resolver), ["skirmish.economy"] = Economy(resolver) };
        foreach (var (key,value) in Construction(resolver)) result["skirmish.construction."+key] = value!;
        return result;
    }

    private static double D(object? value) => ModelGltf.D(value);
    private static List<object?> List(object? value) => ModelGltf.List(value);
    private static Dictionary<string,object?> Dict(object? value) => ModelGltf.Dict(value);
    private static long Long(object? value) => System.Convert.ToInt64(value);

    /// <summary>A database value times an integer, keeping Python's type: an int stays an int, a float a float.</summary>
    private static JsonNode Times(LqrRecord record,string column,int factor) => record[column] switch
    {
        int i => i*factor, float f => (double)f*factor, _ => throw new InvalidCastException($"{record.Table.Name}.{column} is not a number"),
    };

    /// <summary>The first skeleton of a resolved model, parsed.</summary>
    private static LskSkeleton FirstSkeleton(ModelResolver resolver,Dictionary<string,object?> model)
        => resolver.Skeleton((string)Dict(List(model["skeletons"])[0])["file"]!) ?? throw new InvalidDataException("unreadable skeleton");

    /// <summary>A clip name to its duration, the last clip of a name winning (Python's dict of the clips).</summary>
    private static Dictionary<string,double> Durations(LskSkeleton skeleton)
    {
        var durations = new Dictionary<string,double>();
        foreach (var clip in skeleton.Clips) durations[clip.Name] = clip.Duration;
        return durations;
    }

    // ---- models: prepare_prototype.export_units ------------------------------------------------------------------

    private static bool Marker(string name) => name.Replace("_","").Replace(" ","").ToLowerInvariant() is "jposition" or "jpositional";

    /// <summary>The animations ModelGltf makes of a resolved model, in its order: per skeleton (the model's, then its
    /// attachments' depth first) one per clip that has a channel, named with the attachment's prefix; with the skin it
    /// belongs to and whether its J_Position marker joint has a translation channel in it.</summary>
    private static void Animations(ModelResolver resolver,Dictionary<string,object?> res,int depth,List<(string Name,int Skin,bool MarkerMoves,bool MarkerStep)> animations,List<bool> skinHasMarker)
    {
        string name = res.GetValueOrDefault("model") as string is { Length: > 0 } model ? model : Path.GetFileName((string?)res.GetValueOrDefault("file") ?? "?");
        var skeletons = res.GetValueOrDefault("skeletons") as List<object?>;
        if (skeletons is { Count: > 0 } && ModelGltf.FileOf(skeletons[0]) is { } file)
        {
            // ModelGltf exports a model whose skeleton does not parse without one (resolver.Skeleton gives null).
            if (resolver.Skeleton(file) is { } skeleton)
            {
                int skin = skinHasMarker.Count;
                int marker = Array.FindIndex(skeleton.Joints,j => Marker(j.Name));
                skinHasMarker.Add(marker >= 0);
                string prefix = depth == 0 ? "" : name+"/";
                foreach (var clip in skeleton.Clips)
                {
                    int channels = 0;
                    bool moves = false, step = false;
                    for (int j = 0; j < Math.Min(skeleton.Joints.Length,clip.Tracks.Length); j++)
                    {
                        var track = clip.Tracks[j];
                        if (track.Times.Length == 0) continue;
                        if (track.Rotations.Length > 0) channels++;
                        if (track.Positions.Length > 0) { channels++; moves |= j == marker; }
                    }
                    if (channels == 0 && skeleton.Joints.Length > 0)
                    {
                        // A static clip's one channel: a STEP translation of the first joint, which in_place refuses to move.
                        channels = 1;
                        step = marker == 0;
                    }
                    if (channels > 0) animations.Add((prefix+clip.Name,skin,moves,step));
                }
            }
        }
        foreach (var a in (res.GetValueOrDefault("attachments") as List<object?> ?? []).Select(Dict).Where(a => (string?)a["kind"] == "model"))
            if (Dict(a["model"]) is var sub && sub.ContainsKey("parts")) Animations(resolver,sub,depth+1,animations,skinHasMarker);
    }

    /// <summary>A model's clip names as its glTF has them and the run and walk clips prototype_assets.adapt moves in place.</summary>
    public static (List<string> Clips,List<string> InPlace) Clips(ModelResolver resolver,string model,bool centerPivot)
    {
        var animations = new List<(string Name,int Skin,bool MarkerMoves,bool MarkerStep)>();
        var skins = new List<bool>();
        Animations(resolver,resolver.Resolve(model),0,animations,skins);
        static bool Locomotion(string name) => name.ToLowerInvariant().Contains("_run") || name.ToLowerInvariant().Contains("_walk");
        if (animations.Any(a => a.MarkerStep && (centerPivot || Locomotion(a.Name))))
            throw new InvalidDataException("prototype root motion requires linear source keys");
        var changed = animations.Where(a => centerPivot ? skins.Any(m => m) : Locomotion(a.Name) && skins[a.Skin] && a.MarkerMoves).Select(a => a.Name);
        return ([.. animations.Select(a => a.Name)],[.. changed.Where(Locomotion).Distinct().Order(StringComparer.Ordinal)]);
    }

    public static JsonArray Models(ModelResolver resolver)
    {
        var models = new JsonArray();
        foreach (string unit in SkirmishCatalog.Roster)
        {
            string model = ById(resolver.Db["Models"],I(Named(resolver.Db["Units"],unit),"Model")).Name;
            var (clips,inPlace) = Clips(resolver,model,SkirmishCatalog.RangedUnits.Contains(unit));
            string Clip(params string[] terms) => clips.FirstOrDefault(c => terms.All(t => c.ToLowerInvariant().Contains(t.ToLowerInvariant()))) ?? "";
            string Or(string first,string second) => first.Length > 0 ? first : second;
            models.Add(new JsonObject
            {
                ["unit"] = unit, ["file"] = "units/"+model+".glb", ["in_place_locomotion"] = Strings(inPlace),
                ["idle"] = Clip("Idle_01"), ["walk"] = Or(Clip("Run"),Clip("Walk")), ["attack"] = Clip("Attack","01"), ["death"] = Or(Clip("Death"),Clip("Defeated")),
            });
        }
        return models;
    }

    // ---- skirmish.economy: skirmish_economy.economy_data --------------------------------------------------------

    private static readonly (string Key,string State)[] WorkerStates =
    [
        ("mine","UNIT_ANIM_STATE_RESOURCE_ORE_GATHER"), ("ore_pick_up","UNIT_ANIM_STATE_RESOURCE_ORE_PICK_UP"),
        ("ore_walk","UNIT_ANIM_STATE_RESOURCE_ORE_WALK"), ("ore_idle","UNIT_ANIM_STATE_RESOURCE_ORE_IDLE"),
        ("ore_put_down","UNIT_ANIM_STATE_RESOURCE_ORE_PUT_DOWN"), ("food_walk","UNIT_ANIM_STATE_RESOURCE_FOOD_WALK"),
        ("food_idle","UNIT_ANIM_STATE_RESOURCE_FOOD_IDLE"), ("food_put_down","UNIT_ANIM_STATE_RESOURCE_FOOD_PUT_DOWN"),
        ("build","UNIT_ANIM_STATE_REPAIR"),
    ];
    private const int InsideTicks = 40, RawOreLoad = 5;
    private const string EconomyAdapter = "Owner-defined trips: a raw ore pile yields one ore per pickaxe hit (source gather clip and its supply-gather event), five per trip, then the source pick-up clip. A completed owned Mill or Foundry admits one worker at a time for two seconds, who leaves with a full load (MaxCarriedFood / MaxCarriedOre). Loads are credited after the source put-down clip at an owned depot. Source gather rates only gate availability; the max raw percentage is the recoverable fraction of initial stock. Interaction radii use footprint corner distance plus worker radius and one navigation cell. Food wells require a Mill.";

    /// <summary>skirmish_economy.ticks: ceil(seconds x fraction x 20) in Python's decimal arithmetic.</summary>
    public static long Ticks(double seconds,double fraction = 1) => (long)(PyDecimal.Of(seconds)*PyDecimal.Of(fraction)*PyDecimal.Of(20)).ToIntegral(PyDecimal.Rounding.Ceiling);
    private static long Ticks(double seconds,long fraction) => (long)(PyDecimal.Of(seconds)*PyDecimal.Of(fraction)*PyDecimal.Of(20)).ToIntegral(PyDecimal.Rounding.Ceiling);

    /// <summary>The first choice of positive weight in a state.</summary>
    private static Dictionary<string,object?> Positive(Dictionary<string,object?> state) => List(state["choices"]).Select(Dict).First(c => Long(c["weight"]) > 0);

    /// <summary>skirmish_economy.gather_animations: a worker's resource and repair clips, durations in 20 Hz ticks.</summary>
    private static JsonObject WorkerAnimations(ModelResolver resolver,string unit)
    {
        var model = resolver.Resolve(unit,recurse: false);
        var durations = Durations(FirstSkeleton(resolver,model));
        var states = new Dictionary<string,Dictionary<string,object?>>();
        foreach (var state in List(model["animation_states"]).Select(Dict)) states[(string?)state["name"] ?? ""] = state;
        var result = new JsonObject();
        foreach (var (key,name) in WorkerStates)
        {
            var choice = Positive(states[name]);
            string clip = (string)choice["clip_name"]!;
            result[key] = new JsonObject { ["state"] = name, ["clip"] = clip, ["duration_ticks"] = Ticks(durations[clip],1L) };
        }
        // The pickaxe impact is the clip's supply-gather sound event, which coincides with the ore-mining effect key.
        var mine = Positive(states[WorkerStates[0].State]);
        var events = List(model["events"]).Select(Dict).ToList();
        var hit = List(mine["event_indices"]).Select(i => events[(int)Long(i)]).First(e => e.GetValueOrDefault("name") as string == "sound");
        double time = D(hit["time"]);
        var mineNode = result["mine"]!.AsObject();
        mineNode["hit_tick"] = Math.Min(mineNode["duration_ticks"]!.GetValue<long>()-1,Ticks(durations[(string)mine["clip_name"]!],time));
        mineNode["hit_source_fraction"] = time;
        if (result["ore_put_down"]!["duration_ticks"]!.GetValue<long>() != result["food_put_down"]!["duration_ticks"]!.GetValue<long>())
            throw new InvalidDataException("food and ore put-down clips differ in length");
        return result;
    }

    public static JsonObject Economy(ModelResolver resolver)
    {
        var db = resolver.Db;
        var variables = db["WorldVariables"].Records[0];
        var sources = new JsonObject();
        foreach (string name in new[] { "OBJECT_RESOURCE_ORE_PILE","OBJECT_RESOURCE_FOOD_WELL" })
        {
            var record = Named(db["Objects"],name);
            sources[name.EndsWith("ORE_PILE",StringComparison.Ordinal) ? "ore" : "food"] = new JsonObject
            {
                ["rate_milli_per_tick"] = Round(F(record,"ResourceGatherRateRaw")*1000/20), ["raw_fraction_ppm"] = Ppm(F(record,"MaxGatherPercentageRaw")),
                ["improved_rate_milli_per_tick"] = Round(F(record,"ResourceGatherRateWithBuilding")*1000/20),
                ["improved_fraction_ppm"] = Ppm(F(record,"MaxGatherPercentageWithBuilding")),
                ["building_clearance_mm"] = Round(F(record,"MinBuildingPlacementRadius")*1000),
            };
        }
        var workers = new JsonObject();
        foreach (var faction in SkirmishCatalog.Factions)
        {
            var unit = Named(db["Units"],faction.Worker);
            workers[faction.Worker] = new JsonObject
            {
                ["food_capacity"] = V(unit,"MaxCarriedFood"), ["ore_capacity"] = V(unit,"MaxCarriedOre"), ["raw_ore_load"] = RawOreLoad,
                ["inside_ticks"] = InsideTicks, ["animations"] = WorkerAnimations(resolver,faction.Worker),
            };
        }
        return new JsonObject
        {
            ["format_version"] = 2, ["ticks_per_second"] = 20, ["workers"] = workers,
            ["food_capacity"] = V(variables,"DefaultInitialFoodCapacity"), ["ore_capacity"] = V(variables,"DefaultInitialOreCapacity"),
            ["sources"] = sources, ["adapter"] = EconomyAdapter,
        };
    }

    // ---- skirmish.construction: skirmish_construction ---------------------------------------------------------

    private const int CorruptionRadiusMm = 25000;
    private const string CorruptionAdapter = "The source flags the buildings that create corruption (War Post) and those that need it, and stores no radius; the radius is the Good Citadel building radius (25 m). The start Goblin Hovel (start_creators, only while it is the building the map started with) corrupts the same radius, since nothing else could be built at the start. A building needs its origin cell on corrupted ground.";
    private const string PlacementAdapter = "Footprints use source building passability (PATHING_CATEGORY_BUILDING) and current walker clearance under the oriented-collision box, no rotation. MustBePlacedNearOtherBuildings uses center distance to completed buildings within their MaxRadiusAllowingNewBuildings. An upgraded building keeps the footprint it was placed with (Citadel levels share one box; the War Camp box is 1 cm wider than the Camp).";
    private const string Slavemaster = "UNIT_E_GOBLIN_SLAVE_MASTER";

    /// <summary>skirmish_construction.display_name: Citadel N and Fortress N for the town centre levels.</summary>
    private static string DisplayName(string name,LqrRecord record)
    {
        if (name.StartsWith("BUILDING_G_TOWN_CENTER_LEVEL_",StringComparison.Ordinal)) return "Citadel "+name[^1];
        return name.StartsWith("BUILDING_E_TOWN_CENTER_LEVEL_",StringComparison.Ordinal) ? "Fortress "+name[^1] : S(record,"Name");
    }

    private static JsonObject BuildingRules(LqrDatabase db,string name)
    {
        var variables = db["WorldVariables"].Records[0];
        var record = Named(db["Buildings"],name);
        return new JsonObject
        {
            ["name"] = name, ["display_name"] = DisplayName(name,record), ["food_cost"] = V(record,"CostFood"), ["ore_cost"] = V(record,"CostOre"),
            ["duration_ticks"] = Math.Max(1,FloorDiv(I(record,"BuildTime")+49,50)), ["max_health"] = V(record,"MaxHealth"),
            ["food_refund_ppm"] = Ppm(F(record,"RefundFoodPct")), ["ore_refund_ppm"] = Ppm(F(record,"RefundOrePct")),
            ["extra_worker_ppm"] = Ppm(F(variables,"AddlWorkerBuildFactor")), ["initial_health_ppm"] = Ppm(F(variables,"BldgInitialHealthPct")),
            ["prerequisites"] = Strings(Enumerable.Range(1,3).Select(i => I(record,$"BuildingRequired{i}")).Where(b => b >= 0).Select(b => ById(db["Buildings"],b).Name)),
        };
    }

    private static readonly (string Kind,string Building)[] GoodSites = [("food","BUILDING_G_FOOD_STRUCTURE"), ("ore","BUILDING_G_BLACKSMITH")];

    /// <summary>prototype_obstacles.rotate: a point turned by a unit quaternion (w x y z).</summary>
    private static double[] Rotate(double[] p,double[] q)
    {
        double w = q[0], x = q[1], y = q[2], z = q[3];
        double length = Math.Sqrt(w*w+x*x+y*y+z*z);
        if (!double.IsFinite(length) || Math.Abs(length-1) > .01) throw new InvalidDataException("Obstacle box has non-unit quaternion");
        (w,x,y,z) = (w/length,x/length,y/length,z/length);
        double px = p[0], py = p[1], pz = p[2];
        double tx = 2*(y*pz-z*py), ty = 2*(z*px-x*pz), tz = 2*(x*py-y*px);
        return [px+w*tx+y*tz-z*ty, py+w*ty+z*tx-x*tz, pz+w*tz+x*ty-y*tx];
    }

    /// <summary>prototype_navigation.transform of an unrotated object at the origin with unit scale.</summary>
    private static double[] Transform(double[] point,IList<object?> modelScale)
    {
        double[] value = [point[0]*1*D(modelScale[0]),point[1]*1*D(modelScale[1]),point[2]*1*D(modelScale[2])];
        for (int axis = 0; axis < 3; axis++)
        {
            var (a,b) = axis switch { 0 => (1,2), 1 => (2,0), _ => (0,1) };
            double c = Math.Cos(0.0), s = Math.Sin(0.0);
            (value[a],value[b]) = (c*value[a]-s*value[b],s*value[a]+c*value[b]);
        }
        return [value[0]+0,value[1]+0,value[2]+0];
    }

    /// <summary>prototype_obstacles.hull: the convex hull of the points' first two coordinates (monotone chain).</summary>
    private static List<(double X,double Y)> Hull(IEnumerable<double[]> points)
    {
        var distinct = new List<(double X,double Y)>();
        foreach (var p in points) if (!distinct.Any(d => d.X == p[0] && d.Y == p[1])) distinct.Add((p[0],p[1]));
        var sorted = distinct.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        static double Cross((double X,double Y) a,(double X,double Y) b,(double X,double Y) c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        static List<(double,double)> Chain(IEnumerable<(double X,double Y)> sequence)
        {
            var result = new List<(double X,double Y)>();
            foreach (var p in sequence)
            {
                while (result.Count > 1 && Cross(result[^2],result[^1],p) <= 0) result.RemoveAt(result.Count-1);
                result.Add(p);
            }
            if (result.Count > 0) result.RemoveAt(result.Count-1);
            return result;
        }
        return [.. Chain(sorted), .. Chain(Enumerable.Reverse(sorted))];
    }

    /// <summary>skirmish_map.building_footprint at the origin: the building model's oriented-collision box (one) as a
    /// horizontal polygon and height range, at the model's scale.</summary>
    public static (List<(double X,double Y)> Polygon,double YMin,double YMax) Footprint(ModelResolver resolver,string building)
    {
        var db = resolver.Db;
        var record = Named(db["Buildings"],building);
        var resolved = resolver.Resolve(ById(db["Models"],I(record,"Model")).Name,recurse: false);
        int layer = Named(db["BBoxes"],"BBOX_LAYER_ORIENTED_COLLISION").Id;
        var boxes = resolver.Model((string)resolved["file"]!).BoundingBoxes.Where(b => b.Layer == layer).ToList();
        if (boxes.Count != 1) throw new InvalidDataException("expected one oriented collision box for "+building);
        var box = boxes[0];
        double[] v = [.. box.Vectors.Select(f => (double)f)];
        if (box.Joint != -1 || box.Flag != 0) throw new InvalidDataException("Animated/flagged obstacle boxes need an explicit adapter");
        if (v.Length != 12 || !v.All(double.IsFinite) || v[..3].Min() <= 0) throw new InvalidDataException("Invalid obstacle box dimensions");
        if (v[3] != 1 || v[7] != 1) throw new InvalidDataException("Unknown obstacle box vector format");
        var scale = List(resolved["scale"]);
        var corners = new List<double[]>();
        foreach (double x in new[] { -v[0],v[0] })
            foreach (double z in new[] { -v[1],v[1] })
                foreach (double y in new[] { -v[2],v[2] })
                {
                    var p = Rotate([x,z,y],v[8..]);
                    corners.Add(Transform([p[0]+v[4],p[1]+v[5],p[2]+v[6]],scale));
                }
        var polygon = Hull(corners);
        if (polygon.Count < 3) throw new InvalidDataException("Obstacle has degenerate horizontal footprint");
        return (polygon,corners.Min(p => p[2]),corners.Max(p => p[2]));
    }

    /// <summary>The construction section without its sites (construction_data) and defenses: definitions, buildings,
    /// then placement_data's order, palettes, corruption, placeable buildings with footprints, upgrades and radii.</summary>
    public static JsonObject Construction(ModelResolver resolver)
    {
        var db = resolver.Db;
        LqrTable buildings = db["Buildings"];
        var factionDefinitions = new JsonObject();
        foreach (var faction in SkirmishCatalog.Factions)
            factionDefinitions[faction.Key] = new JsonObject { ["food"] = BuildingRules(db,faction.Food), ["ore"] = BuildingRules(db,faction.Ore) };
        var described = new JsonObject();
        foreach (string name in SkirmishCatalog.SkirmishBuildings)
        {
            var record = Named(buildings,name);
            described[name] = new JsonObject
            {
                ["display_name"] = DisplayName(name,record), ["max_health"] = V(record,"MaxHealth"), ["armor"] = V(record,"ArmorPoints"),
                ["portrait"] = Stem(S(ById(db["UVAs"],I(record,"Portrait")),"Filename")),
                ["selection_radius_mm"] = Round(F(record,"SelectionRingRadius")*1000), ["sight_mm"] = Times(record,"SightRange",1000),
            };
        }
        string[] order = [.. SkirmishCatalog.ConstructionOrder, .. SkirmishCatalog.EvilConstructionOrder, SkirmishCatalog.EvilWarpost];
        var resource = SkirmishCatalog.Factions.SelectMany(f => new[] { f.Food,f.Ore }).ToHashSet();
        var placeable = new JsonObject();
        foreach (string name in order.Where(n => !resource.Contains(n)))
        {
            var record = Named(buildings,name);
            var (polygon,yMin,yMax) = Footprint(resolver,name);
            var rules = BuildingRules(db,name);
            rules["footprint"] = new JsonObject
            {
                ["polygon_mm"] = new JsonArray([.. polygon.Select(p => (JsonNode?)new JsonArray(Round(p.X*1000),Round(p.Y*1000)))]),
                ["min_height_mm"] = Round(yMin*1000), ["max_height_mm"] = Round(yMax*1000),
            };
            rules["must_be_near"] = B(record,"MustBePlacedNearOtherBuildings");
            rules["drop_off"] = B(record,"IsResourceDropOffBuilding");
            if (B(record,"CanOnlyBePlacedOnCorruption")) rules["corrupted_only"] = true;
            if (B(record,"CreatesCorruption")) rules["creates_corruption"] = true;
            placeable[name] = rules;
        }
        var scope = SkirmishCatalog.SkirmishBuildings;
        var creators = new JsonObject();
        foreach (string name in scope.Where(n => B(Named(buildings,n),"CreatesCorruption"))) creators[name] = CorruptionRadiusMm;
        var corruption = new JsonObject
        {
            ["creators"] = creators, ["start_creators"] = new JsonObject { [SkirmishCatalog.EvilTownCenter] = CorruptionRadiusMm },
            ["only"] = Strings(scope.Where(n => B(Named(buildings,n),"CanOnlyBePlacedOnCorruption"))), ["adapter"] = CorruptionAdapter,
        };
        var palettes = new JsonObject
        {
            ["UNIT_G_HUMAN_WORKER"] = Strings(SkirmishCatalog.ConstructionOrder), ["UNIT_E_GOBLIN_SLAVE"] = Strings(SkirmishCatalog.EvilConstructionOrder),
            [Slavemaster] = Strings([SkirmishCatalog.EvilWarpost]),
        };
        var upgrades = new JsonObject();
        foreach (string name in scope)
        {
            int promotes = I(Named(buildings,name),"PromotesTo");
            if (promotes < 0) continue;
            var promoted = ById(buildings,promotes);
            if (!SkirmishCatalog.IsSkirmishBuilding(promoted.Name)) continue;
            var rules = BuildingRules(db,promoted.Name);
            rules["duration_ticks"] = FloorDiv(I(promoted,"UpgradeTime")+49,50);
            upgrades[name] = rules;
        }
        var radii = new JsonObject();
        foreach (string name in scope) radii[name] = Math.Max(0,Round(F(Named(buildings,name),"MaxRadiusAllowingNewBuildings")*1000));
        return new JsonObject
        {
            ["definitions"] = new JsonObject { [GoodSites[0].Kind] = BuildingRules(db,GoodSites[0].Building), [GoodSites[1].Kind] = BuildingRules(db,GoodSites[1].Building) },
            ["faction_definitions"] = factionDefinitions, ["buildings"] = described,
            ["order"] = Strings(order), ["palettes"] = palettes, ["corruption"] = corruption, ["placeable"] = placeable, ["upgrades"] = upgrades,
            ["placement_radius_mm"] = radii, ["placement_adapter"] = PlacementAdapter,
        };
    }
}
