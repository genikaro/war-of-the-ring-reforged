using Reforged.Presentation;

/// <summary>Build-131: the owner's blood rule (harder hits bleed more, wounded units bleed more often).</summary>
static class BloodIntensityChecks
{
    public static void Run(Action<bool,string> check)
    {
        check(BloodIntensity.AmountScale(0) == .5 && BloodIntensity.AmountScale(25_000) == 1 && BloodIntensity.AmountScale(50_000) == 1.5
            && BloodIntensity.AmountScale(125_000) == 1.5 && BloodIntensity.AmountScale(12_500) == .75,
            "blood scales from half at no damage through the source count at 25 to half again from 50 damage");
        check(BloodIntensity.Chance(800_000,1_000_000) == 0 && BloodIntensity.Chance(950_000,1_000_000) == 0
            && BloodIntensity.Chance(200_000,1_000_000) == 1 && BloodIntensity.Chance(0,1_000_000) == 1
            && Math.Abs(BloodIntensity.Chance(500_000,1_000_000)-.5) < 1e-9,
            "a unit never bleeds at 80% health or more, always at 20% or less, linearly in between");
        var rolls = Enumerable.Range(0,4000).Select(i => BloodIntensity.Roll(i%97,i,i%13-1,i%3)).ToArray();
        check(rolls.All(r => r is >= 0 and < 1) && Math.Abs(rolls.Average()-.5) < .03
            && BloodIntensity.Roll(5,100,2,0) == BloodIntensity.Roll(5,100,2,0) && BloodIntensity.Roll(5,100,2,0) != BloodIntensity.Roll(5,101,2,0),
            "hit draws are fixed per hit and spread evenly");
        int bled = Enumerable.Range(0,4000).Count(i => BloodIntensity.Bleeds(500_000,1_000_000,i%97,i,i%13-1,i%3));
        check(bled is > 1800 and < 2200,"half-health units bleed about half the time");
    }
}
