using System.Text.Json.Nodes;
using Reforged.Formats;
using static Reforged.Install.PackRules;

namespace Reforged.Install;

/// <summary>Build-204: the construction section's building defenses (tools/skirmish_construction.py defense_data):
/// each skirmish building's BuildingDefenses rows with their weapon, rates of fire by garrison size, the projectile's
/// source joint and its presentation, and the source files those read.</summary>
public sealed partial class PackCombat
{
    /// <summary>Where the defenses go in content.json.</summary>
    public const string DefensesSection = "skirmish.construction.defenses";

    private static readonly string[] DefenseRates = ["BaseRateOfFire", "RateOfFireWithOneUnitGarrisoned", "RateOfFireWithTwoUnitsGarrisoned",
        "RateOfFireWithThreeUnitsGarrisoned", "RateOfFireWithFourUnitsGarrisoned"];

    /// <summary>skirmish_construction.projectile_joint_mm: the model-space offset of the joint carrying a JointFlags
    /// bit, at the model's scale, in simulation axes (x across, y up, z the source y).</summary>
    private JsonArray ProjectileJoint(string building,int flag)
    {
        var model = ById(T("Models"),I(Named(T("Buildings"),building),"Model"));
        var resolved = _resolver.Resolve(model.Name,recurse: false);
        double scale = D(List(resolved["scale"])[0]);
        foreach (var entry in Dicts(resolved["skeletons"]))
            foreach (var joint in LskSkeleton.Parse(_resolver.Read((string)entry["file"]!)).Joints)
                if ((joint.Flags & (1u << flag)) != 0)
                {
                    double x = joint.BindPosition[0], y = joint.BindPosition[1], z = joint.BindPosition[2];
                    return [Round(x*scale*1000),Round(z*scale*1000),Round(y*scale*1000)];
                }
        throw new InvalidDataException($"no joint with flag {flag} on {building}");
    }

    /// <summary>skirmish_construction.defense_data: the defenses of every skirmish building, by building.</summary>
    public JsonObject Defenses()
    {
        var defenses = new JsonObject();
        var traced = _sources;
        try
        {
            foreach (string name in SkirmishCatalog.SkirmishBuildings)
            {
                var record = Named(T("Buildings"),name);
                for (int slot = 1; slot <= 3; slot++)
                {
                    int id = I(record,$"BuildingDefenses{slot}");
                    if (id < 0) continue;
                    var defense = ById(T("BuildingDefenses"),id);
                    var weapon = ById(T("Weapons"),I(defense,"WeaponType"));
                    _sources = [];
                    var origin = ProjectileJoint(name,I(defense,"ProjectileSourceJointFlag"));
                    var projectile = Projectile(null,I(weapon,"AssociatedProjectile"));
                    var sources = new JsonObject();
                    foreach (var (key,hash) in _sources) sources[key] = hash;
                    if (defenses[name] is not JsonArray list) defenses[name] = list = [];
                    list.Add(new JsonObject
                    {
                        ["defense"] = defense.Name, ["weapon"] = weapon.Name,
                        ["dps_milli"] = Round((F(weapon,"CuttingDamagePerSecond")+F(weapon,"FireDamagePerSecond"))*1000), ["fate_ppm"] = Ppm(F(weapon,"FateModifier")),
                        ["critical_kill"] = B(weapon,"CanCriticallyKill"), ["minimum_range_mm"] = Round(F(weapon,"MinimumRange")*1000),
                        ["range_mm"] = Round(F(weapon,"MaximumRange")*1000), ["rates_milli"] = new JsonArray([.. DefenseRates.Select(r => (JsonNode?)Round(F(defense,r)*1000))]),
                        ["max_garrison"] = V(record,"MaxGarrisonedUnits"), ["origin_mm"] = origin, ["projectile"] = projectile, ["sources"] = sources,
                    });
                }
            }
        }
        finally { _sources = traced; }
        return defenses;
    }
}
