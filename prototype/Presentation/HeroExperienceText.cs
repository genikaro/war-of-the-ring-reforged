using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

public sealed record HeroExperienceText(string Level,string Tooltip,float Progress,string Counter)
{
    public static HeroExperienceText From(SimUnit hero)
    {
        static string Xp(long micro) => (micro/1_000_000m).ToString("0.##",CultureInfo.InvariantCulture);
        string level=$"Level {hero.HeroLevel}";
        if (hero.Definition.Experience==null) return new(level,level,0,"");
        string counter=hero.HeroLevel>=hero.MaxHeroLevel ? "MAX" : string.Create(CultureInfo.InvariantCulture,
            $"{(hero.ExperienceMicro-hero.LevelStartExperienceMicro)/1_000_000}/{(hero.NextLevelExperienceMicro-hero.LevelStartExperienceMicro)/1_000_000}");
        string progress=hero.HeroLevel>=hero.MaxHeroLevel ? "Maximum level"
            : $"{Xp(hero.ExperienceMicro-hero.LevelStartExperienceMicro)} / {Xp(hero.NextLevelExperienceMicro-hero.LevelStartExperienceMicro)} XP to level {hero.HeroLevel+1}";
        string bonuses=hero.HeroLevel<=1 ? "" : string.Create(CultureInfo.InvariantCulture,
            $"\nLevel bonuses: +{hero.ExperienceHitPoints} maximum health, +{hero.ExperienceArmorMilli/1000m:0.###} armor, +{hero.ExperienceDamageBonusPpm/10000m:0.##}% attack.");
        return new(level,$"{level} · {progress}\nTotal experience: {Xp(hero.ExperienceMicro)} XP"+bonuses,hero.ExperienceProgressPpm/1_000_000f,counter);
    }
}
