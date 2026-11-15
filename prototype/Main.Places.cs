using Godot;
using Reforged.Simulation;
using System.Globalization;
using System.Text.Json;
using Reforged.Presentation;

/// <summary>Places of power in the game (build-54): the source places from the content pack, the map's place objects, the
/// match seed that picks a random place (--seed=N; the setup screen draws one per match and Restart keeps it), the control
/// zone and the benefit on selection, a notice when one changes hands, and the holder's ring on the minimap.</summary>
public partial class Main
{
    private int _matchSeed;
    private readonly Dictionary<int,MeshInstance3D> _placeZones = [];
    /// <summary>Every change of hands so far, for the observer report.</summary>
    private readonly List<string> _placeLog = [];
    private readonly Dictionary<string,string> _placeModels = [];

    private void LoadPlacesOfPower(JsonElement data)
    {
        if (!data.TryGetProperty("places_of_power",out var places))
            throw new InvalidOperationException("The rules give no places of power");
        var world = _world!; world.MatchSeed = _matchSeed;
        if (places.GetProperty("format_version").GetInt32() < 3)
            throw new InvalidOperationException("The rules give no skirmish places of power");
        world.ExcludedSkirmishPlaces.UnionWith(places.GetProperty("skirmish_excluded_objects").EnumerateArray().Select(p => p.GetString()!));
        foreach (var place in places.GetProperty("places").EnumerateArray())
        {
            var rule = new PlaceOfPowerRule(place.GetProperty("id").GetInt32(),place.GetProperty("name").GetString()!,
                place.GetProperty("display_name").GetString()!,place.GetProperty("radius_mm").GetInt32(),place.GetProperty("random").GetBoolean(),
                place.GetProperty("all_units").GetBoolean(),place.GetProperty("heroes_only").GetBoolean(),
                [.. place.GetProperty("unit_types").EnumerateArray().Select(t => t.GetInt32())],place.GetProperty("modifier").GetString()!,
                place.GetProperty("float_ppm").GetInt32(),place.GetProperty("long").GetInt32());
            world.PlaceRules.Add(rule.Id,rule);
        }
        foreach (var item in places.GetProperty("objects").EnumerateObject())
        {
            world.PlaceObjects.Add(item.Name,(GameText.Name(item.Name,item.Value.GetProperty("display_name").GetString()!),
                [.. item.Value.GetProperty("places").EnumerateArray().Select(p => p.GetInt32())]));
            _placeModels.Add(item.Name,item.Value.GetProperty("file").GetString()!);
            var power = item.Value.GetProperty("power");
            if (power.ValueKind == JsonValueKind.Object)
                world.PlacePowers.Add(item.Name,power.Deserialize<PlacePowerDefinition>()!);
            if (!_structureDefinitions.ContainsKey(item.Name))
                _structureDefinitions.Add(item.Name,JsonSerializer.SerializeToElement(new {
                    display_name = item.Value.GetProperty("display_name").GetString(),portrait = "Object_Place_of_Power_Bordered",
                    has_health = false,max_health = 0,armor = 0 }));
        }
    }

    /// <summary>A map's place-of-power object: the simulated place at its ground anchor and its zone ring, shown when selected.</summary>
    private void AddPlaceOfPower(int id,string type,Node3D root)
    {
        var place = _world!.AddPlaceOfPower(id,type,(int)Math.Round(-root.GlobalPosition.X*1000),(int)Math.Round(root.GlobalPosition.Z*1000));
        // Keep the map placement's anchor, scale and rotation. Replace its placeholder before registering picking meshes.
        if (_placeModels.TryGetValue(place.Type,out string? file))
        {
            foreach (var child in root.GetChildren()) { root.RemoveChild(child); child.QueueFree(); }
            root.AddChild(LoadGlb(file));
        }
        const int Segments = 96;
        var points = new List<Vector3>();
        for (int i = 0; i < Segments; i++)
        {
            double a = Math.Tau*i/Segments,b = Math.Tau*(i+1)/Segments;
            var p = PositionOf(place.X+(int)(Math.Cos(a)*place.RadiusMm),place.Z+(int)(Math.Sin(a)*place.RadiusMm))+Vector3.Up*.1f;
            var q = PositionOf(place.X+(int)(Math.Cos(b)*place.RadiusMm),place.Z+(int)(Math.Sin(b)*place.RadiusMm))+Vector3.Up*.1f;
            var side = (q-p).Cross(Vector3.Up).Normalized()*.07f;
            foreach (var v in new[] {p-side,q-side,p+side,p+side,q-side,q+side}) points.Add(root.ToLocal(v));
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        var zone = new MeshInstance3D { Mesh = mesh,Visible = false,CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { CullMode = BaseMaterial3D.CullModeEnum.Disabled,ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        zone.SetMeta("place_control_zone",true);
        root.AddChild(zone); _placeZones.Add(id,zone);
    }

    private PlaceOfPower? PlaceAt(int id) => _world?.PlacesOfPower.FirstOrDefault(p => p.Id == id);

    private Color PlaceColor(int owner) => owner < 0 ? new Color("ded2a5") : _observing ? TeamColor(owner).Lightened(.25f)
        : new Color(owner == PlayerTeam ? "e9c22d" : "e65045");

    private string PlaceHolder(int team,bool capital = true) => team == PlayerTeam && !_observing ? capital ? "You" : "you"
        : _observing || _world!.Contenders.Count() > 2 ? $"Player {team+1}" : capital ? "The enemy" : "the enemy";

    /// <summary>Who holds the place and what it gives, for the structure panel.</summary>
    private string PlaceSummary(PlaceOfPower place)
    {
        // Build-184: in the install's language (the capture notices keep PlaceHolder's English until build-185).
        string Holder(int team) => team == PlayerTeam && !_observing ? GameText.T("place.holder.you")
            : _observing || _world!.Contenders.Count() > 2 ? GameText.T("place.holder.player",team+1) : GameText.T("place.holder.enemy");
        string holder = place.Owner < 0 ? GameText.T("place.unclaimed") : GameText.T("place.held_by",Holder(place.Owner));
        if (place.Contested) holder += " · "+GameText.T("place.contested");
        var benefits = place.Rules.Where(r => r.Modifier != "NONE").Select(PlaceBenefit).ToList();
        if (_world!.PlacePowers.TryGetValue(place.Type,out var power)) benefits.Add(CommandPanel.PlacePowerText(power));
        if (benefits.Count == 0) benefits.Add(GameText.T("place.landmark"));
        return holder+"\n"+string.Join("\n",benefits);
    }

    /// <summary>A source place's benefit in words; modifiers the simulation does not apply yet say so.</summary>
    private static string PlaceBenefit(PlaceOfPowerRule rule)
    {
        string who = GameText.T(rule.HeroesOnly ? "place.who.heroes" : rule.AllUnits ? "place.who.all" : rule.Name.Contains("STEED_OF_ROHAN") ? "place.who.cavalry"
            : rule.Name.Contains("ORC_MASK") ? "place.who.orcs" : rule.Name.Contains("BALROG") ? "place.who.balrog" : "place.who.affected");
        string Percent(int ppm) => ((ppm-1_000_000)/10_000).ToString("+0;-0",CultureInfo.InvariantCulture)+"%";
        string Signed(int value) => value.ToString("+0;-0",CultureInfo.InvariantCulture);
        string T(string key,params object?[] args) => GameText.T(key,args);
        string what = rule.Modifier switch {
            SimWorld.ArmorModifier => T("place.armor",rule.Long),
            SimWorld.RegenerationModifier => T("place_benefit.regeneration",GameText.Number(rule.FloatPpm/1_000_000m)),
            SimWorld.MaxHealthModifier => T("place_benefit.max_health",Percent(rule.FloatPpm)),
            SimWorld.DamageAdditiveModifier => T("place.attack",rule.Long),
            SimWorld.MeleeDamageModifier => T("place_benefit.melee_damage",Percent(rule.FloatPpm)),
            SimWorld.RangedDamageModifier => T("place_benefit.ranged_damage",Percent(rule.FloatPpm)),
            SimWorld.DamageModifier => T("place_benefit.damage",Percent(rule.FloatPpm)),
            SimWorld.SiegeDamageModifier => T("place_benefit.siege_damage",Percent(rule.FloatPpm)),
            SimWorld.CoolDownModifier => T("place_benefit.recharge",rule.FloatPpm/10_000),
            "RUNNING_SPEED" => T("place_benefit.running_speed",Percent(rule.FloatPpm)),
            "LINE_OF_SIGHT" => T("place_benefit.sight",Percent(rule.FloatPpm)),
            "EXPERIENCE" => T("place_benefit.experience",Percent(rule.FloatPpm)),
            SimWorld.FateGatherModifier => T("place_benefit.fate_gathering",Percent(rule.FloatPpm)),
            "COST" => T("place_benefit.cost",Percent(rule.FloatPpm)),
            "FATE_COST_ADDITIVE" => T("place_benefit.fate_cost",Signed(rule.Long)),
            "FOOD_COST_ADDITIVE" => T("place_benefit.food_cost",Signed(rule.Long)),
            "ORE_COST_ADDITIVE" => T("place_benefit.ore_cost",Signed(rule.Long)),
            "DAMAGE_ADDITIVE_VERSUS_SPECIFIC_UNITS" => T("place.attack_vs_orcs",rule.Long),
            _ => T("place_benefit.special") };
        string line = T("place.benefit",who,what);
        return SimWorld.SimulatedPlaceModifiers.Contains(rule.Modifier) ? line : T("place.not_simulated",line);
    }

    /// <summary>The selected place's zone in its holder's colour.</summary>
    private void PresentPlaceZones(int? selected)
    {
        foreach (var (id,zone) in _placeZones)
        {
            zone.Visible = selected == id;
            if (zone.Visible && PlaceAt(id) is { } place) ((StandardMaterial3D)zone.MaterialOverride).AlbedoColor = PlaceColor(place.Owner);
        }
    }

    /// <summary>A place changing hands this tick says so (the source PlayAlert).</summary>
    private void PresentPlaceCaptures()
    {
        PresentPlacePowerEvents();
        foreach (var capture in _world!.PlaceCaptures)
            if (PlaceAt(capture.Place) is { } place)
            {
                Notify($"{PlaceHolder(capture.Team)} took the {place.Name}"
                    +(capture.Previous >= 0 && capture.Previous == PlayerTeam && !_observing ? " from you" : ""));
                _placeLog.Add($"{TimeSpan.FromSeconds(capture.Tick/SimWorld.TicksPerSecond):mm\\:ss} Player {capture.Team+1} took {place.Name}");
            }
    }
}
