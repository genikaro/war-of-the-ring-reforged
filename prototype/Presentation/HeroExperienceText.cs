using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>A hero's level line and experience tooltip. Build-184: "Уровень 6" with the original's own label
/// (GameConsole 8300), the rest from the catalog.</summary>
public sealed record HeroExperienceText(string Level,string Tooltip,float Progress,string Counter)
{
    public static HeroExperienceText From(SimUnit hero)
    {
        static string Xp(long micro) => GameText.Number(micro/1_000_000m,"0.##");
        string level=$"{GameText.T("tip.level")} {hero.HeroLevel}";
        if (hero.Definition.Experience==null) return new(level,level,0,"");
        string counter=hero.HeroLevel>=hero.MaxHeroLevel ? GameText.T("hero.max")
            : $"{(hero.ExperienceMicro-hero.LevelStartExperienceMicro)/1_000_000}/{(hero.NextLevelExperienceMicro-hero.LevelStartExperienceMicro)/1_000_000}";
        string progress=hero.HeroLevel>=hero.MaxHeroLevel ? GameText.T("hero.max_level")
            : GameText.T("hero.xp_to_level",Xp(hero.ExperienceMicro-hero.LevelStartExperienceMicro),Xp(hero.NextLevelExperienceMicro-hero.LevelStartExperienceMicro),hero.HeroLevel+1);
        string bonuses=hero.HeroLevel<=1 ? "" : "\n"+GameText.T("hero.level_bonuses",hero.ExperienceHitPoints,
            GameText.Number(hero.ExperienceArmorMilli/1000m,"0.###"),GameText.Number(hero.ExperienceDamageBonusPpm/10000m,"0.##"));
        return new(level,$"{level} · {progress}\n"+GameText.T("hero.total_xp",Xp(hero.ExperienceMicro))+bonuses,hero.ExperienceProgressPpm/1_000_000f,counter);
    }
}
