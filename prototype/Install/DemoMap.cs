using System.Text.Json.Nodes;
using Reforged.Formats;

namespace Reforged.Install;

/// <summary>Build-215: what the Helm's Deep demo holds beside its map, being self-contained (it names no content pack):
/// its combat rules, built for the demo's four units, and the bodies its navigation's clearance profiles are for (the
/// rules' units', as prototype_clearance.refresh takes them).</summary>
public sealed record DemoContent(JsonObject Rules,(int RadiusMm,int HeightMm)[] Bodies);

/// <summary>Build-215: the Helm's Deep demo (converted/prototype) as prepare_prototype.prepare builds it: the level's
/// navigation with plain (not swept-circle) footprints and the walker audit, the squads' anchor near cell (215, 160) of
/// the main region, refresh_demo's six squads (demo_layout), the four units' models list, the slot colours, the map's
/// classic lighting and the file names of the cache's own rules, interface and audio. The scene is MapScene's and the
/// lighting pictures MapPictures', as for every map.</summary>
public sealed partial class PackMap
{
    /// <summary>The demo's level, Levels/Helms Deep.wor, by its file stem.</summary>
    public const string DemoStem = "Helms Deep";
    public const string DemoName = "Helm's Deep — Skirmish Sandbox";
    public const string DemoRules = "Imported combat stats, fixed-tick melee hits and Elven Archer projectiles; see combat/rules.json Policy for approximations.";

    public static bool IsDemo(string stem) => stem.Equals(DemoStem,StringComparison.OrdinalIgnoreCase);

    /// <summary>The level file a stem's map is read from, in the Levels archive (the Sandbox's is Bilbo's Bluffs).</summary>
    public static string LevelFile(string stem) => IsDemo(stem) ? DemoStem+".wor" : MapFile(IsSandbox(stem) ? BilboStem : stem);

    /// <summary>Whether the install holds the demo's level.</summary>
    public bool HasDemo => archives.Contains("Levels/"+LevelFile(DemoStem));

    /// <summary>prepare_prototype.prepare but the files: the demo's manifest (without its file hashes), its navigation with
    /// a clearance profile for every body of <paramref name="rules"/> (combat_rules.export for the demo roster,
    /// SkirmishCatalog.CombatDemoUnits) and the rules themselves.</summary>
    public MapBuild Demo(JsonObject rules)
    {
        var world = Read(DemoStem);
        lock (_lock)
        {
            var resolver = Resolver;
            var navigation = MapNavigation.Build(world,resolver,[],[],precise: false,audit: true);
            int width = navigation.Width, height = navigation.Height;
            var region = Connected(navigation.Allowed,width,height);
            (int X,int Z)[] offsets = [.. new[] { 0,2 }.SelectMany(z => new[] { 0,2,4,14,16,18 }.Select(x => (x,z)))];
            int anchor = -1; long best = long.MaxValue;
            for (int i = 0; i < region.Length; i++)
            {
                if (!region[i] || i%width+19 >= width || i/width+3 >= height || !offsets.All(o => region[i+o.Z*width+o.X])) continue;
                long d = Square(i%width-215)+Square(i/width-160);
                if (d < best) (best,anchor) = (d,i);
            }
            if (anchor < 0) throw new InvalidDataException("no connected sandbox spawn area");
            var models = PackModelRules.Models(resolver,SkirmishCatalog.CombatDemoUnits);
            var (spawns,groups,camera) = DemoLayout(navigation.Allowed,width,height,anchor);
            var bodies = rules["Units"]!.AsArray().Select(u => (u!["BodyRadiusMm"]!.GetValue<int>(),u["BodyHeightMm"]!.GetValue<int>())).Distinct().Order().ToArray();
            navigation.BuildProfiles(bodies);
            var manifest = new JsonObject
            {
                ["format_version"] = 1, ["name"] = DemoName, ["map"] = "map/scene.glb", ["team_colors"] = PackRules.TeamColors(db),
                ["models"] = models, ["spawns"] = spawns, ["groups"] = groups, ["camera_cell"] = camera, ["rules"] = DemoRules,
                ["lighting"] = Lighting(world), ["source_sha256"] = world.Sha256,
                ["interface"] = "interface/index.json", ["combat_rules"] = "combat/rules.json", ["audio"] = "audio/index.json",
            };
            return new(DemoStem,world,manifest,navigation,new(rules,bodies));
        }
    }

    private static long Square(long value) => value*value;

    /// <summary>prepare_prototype.connected: the largest 4-connected component of the allowed cells (the first of equal
    /// ones, in the order of their first cells).</summary>
    public static bool[] Connected(byte[] allowed,int width,int height)
    {
        var owner = new int[allowed.Length]; Array.Fill(owner,-1);
        var queue = new Queue<int>();
        int bestIndex = -1, bestSize = 0, components = 0;
        for (int start = 0; start < allowed.Length; start++)
        {
            if (allowed[start] == 0 || owner[start] >= 0) continue;
            int index = components++, size = 1;
            owner[start] = index; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(), x = cell%width, y = cell/width;
                foreach (int next in new[] { x > 0 ? cell-1 : -1,x+1 < width ? cell+1 : -1,y > 0 ? cell-width : -1,y+1 < height ? cell+width : -1 })
                    if (next >= 0 && allowed[next] != 0 && owner[next] < 0) { owner[next] = index; size++; queue.Enqueue(next); }
            }
            if (size > bestSize) (bestIndex,bestSize) = (index,size);
        }
        var region = new bool[allowed.Length];
        for (int cell = 0; cell < allowed.Length; cell++) region[cell] = owner[cell] >= 0 && owner[cell] == bestIndex;
        return region;
    }

    /// <summary>prepare_prototype.demo_layout: three separated six-unit squads per side, entirely in the main region
    /// (Aragorn leads Blue's first squad, Elven Knights fill it and the second, Elven Archers make the third; Red's are
    /// Uruk-hai), and the camera cell in the middle of them.</summary>
    public static (JsonArray Spawns,JsonArray Groups,int Camera) DemoLayout(byte[] allowed,int width,int height,int anchor)
    {
        var region = Connected(allowed,width,height);
        (int X,int Z)[] offsets = [.. new[] { 0,2 }.SelectMany(z => new[] { 0,2,4 }.Select(x => (x,z)))];
        var candidates = new List<int>();
        for (int cell = 0; cell < region.Length; cell++)
        {
            int cx = cell%width, cy = cell/width;
            if (!region[cell] || cx < 1 || cx >= width-5 || cy < 1 || cy >= height-3) continue;
            bool inside = true;
            for (int z = -1; z < 4 && inside; z++)
                for (int x = -1; x < 6 && inside; x++) inside = region[(cy+z)*width+cx+x];
            if (inside) candidates.Add(cell);
        }
        var spawns = new List<(int Team,int Cell,int Group,int Model)>();
        var groups = new JsonArray();
        int[] bands = [0,-14,14];
        for (int band = 0; band < bands.Length; band++)
            for (int team = 0; team < 2; team++)
            {
                int px = anchor%width+team*18, pz = anchor/width+bands[band];
                int[]? chosen = null;
                foreach (int cell in candidates.OrderBy(c => Square(c%width-px)+Square(c/width-pz)).ThenBy(c => c))
                {
                    int[] cells = [.. offsets.Select(o => cell+o.Z*width+o.X)];
                    if (cells.All(a => spawns.All(b => Square(a%width-b.Cell%width)+Square(a/width-b.Cell/width) >= Square(b.Team != team ? 12 : 8))))
                    { chosen = cells; break; }
                }
                if (chosen == null) throw new InvalidDataException("no separated six-squad demo layout in the main region");
                int group = groups.Count;
                groups.Add(new JsonObject { ["id"] = group, ["team"] = team, ["name"] = (team == 0 ? "Blue" : "Red")+$" squad {band+1}", ["anchor"] = chosen[0] });
                for (int index = 0; index < chosen.Length; index++)
                    spawns.Add((team,chosen[index],group,team == 0 ? band == 2 ? 3 : band == 0 && index == 0 ? 0 : 1 : 2));
            }
        int[] xs = [.. spawns.Select(s => s.Cell%width)], zs = [.. spawns.Select(s => s.Cell/width)];
        int camera = (zs.Min()+zs.Max())/2*width+(xs.Min()+xs.Max())/2;
        var list = new JsonArray([.. spawns.Select(s => (JsonNode)new JsonObject { ["team"] = s.Team, ["cell"] = s.Cell, ["group"] = s.Group, ["model"] = s.Model })]);
        return (list,groups,camera);
    }
}
