namespace Reforged.Presentation;

/// <summary>Build-172: which colour a health bar takes. The original fills green at high health, yellow at moderate and red at low
/// health, for friends and enemies alike (the Evil compendium: Grishnakh's 620 was yellow at 270 and red at 153); a burning building
/// is burnt orange whatever its health (the same level-five Citadel turned from green at 1737/1750 to orange at 1698/1750). The 50%
/// and 25% thresholds are an adapter that fits those samples; the original's own were not measured. A summoned unit's second meter
/// (the Balrog's remaining life) is turquoise.</summary>
public enum HealthTint { Healthy, Wounded, Critical, Burning }

public static class HealthTints
{
    public static HealthTint Of(long healthMilli,long maxHealthMilli,bool burning = false)
        => burning ? HealthTint.Burning
            : maxHealthMilli <= 0 || healthMilli*2 > maxHealthMilli ? HealthTint.Healthy
            : healthMilli*4 > maxHealthMilli ? HealthTint.Wounded : HealthTint.Critical;

    /// <summary>Hex colours sampled from the original's console bars (green and orange) or chosen beside them (yellow, red).</summary>
    public static string Hex(HealthTint tint,bool console = true) => tint switch {
        HealthTint.Wounded => "c8aa1e",
        HealthTint.Critical => "b83428",
        HealthTint.Burning => "b0600a",
        _ => console ? "48972e" : "3fbb35" };

    public const string Lifetime = "02c3b7";
}
