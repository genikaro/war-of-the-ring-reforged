namespace Reforged.Presentation;

/// <summary>How much a hit bleeds (build-131, owner rule): harder blows throw more blood, and a unit bleeds more often
/// the more it is wounded. The original bleeds on every hit at its source counts; this Reforged option
/// (<c>scaled_blood</c>) tones that down. Critical bursts, death pools and weapon flashes are not scaled.</summary>
public static class BloodIntensity
{
    public const string Option = "scaled_blood";

    /// <summary>Particle multiplier by the hit's damage: half at no damage, the source counts at 25, half again from
    /// 50 damage upward, linear in between.</summary>
    public static double AmountScale(long damageMilli) => Math.Clamp(.5+damageMilli/50_000.0,.5,1.5);

    /// <summary>The chance a hit bleeds, by the health the victim has left after it: never at 80% or more, always at
    /// 20% or less, linear in between. A killing blow always bleeds.</summary>
    public static double Chance(long healthMilli,long maxHealthMilli)
        => maxHealthMilli <= 0 ? 1 : Math.Clamp((.8-(double)Math.Max(0,healthMilli)/maxHealthMilli)/.6,0,1);

    /// <summary>A fixed draw in [0,1) per hit, so a replay or a smoke bleeds the same way every time.</summary>
    public static double Roll(int victim,int tick,int attacker,int hit)
    {
        uint h = unchecked((uint)victim*2654435761u^(uint)tick*2246822519u^(uint)(attacker+1)*3266489917u^(uint)(hit+1)*668265263u);
        h ^= h>>15; h = unchecked(h*2246822519u); h ^= h>>13;
        return h%1_000_000/1_000_000.0;
    }

    public static bool Bleeds(long healthMilli,long maxHealthMilli,int victim,int tick,int attacker,int hit)
        => Roll(victim,tick,attacker,hit) < Chance(healthMilli,maxHealthMilli);
}
