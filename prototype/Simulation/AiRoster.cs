namespace Reforged.Simulation;

/// <summary>A bot's game plan (owner model, build-49): a point in the Aggression / Expansion / Tech triangle whose
/// three values sum to 100.</summary>
public sealed record AiStyle(int Aggression,int Expansion,int Tech)
{
    public static readonly AiStyle Balanced = new(34,33,33);

    /// <summary>"Balanced" when every value is at least 25, else the axes of 30 or more, heaviest first ("Aggression-Tech").</summary>
    public string Label => Aggression >= 25 && Expansion >= 25 && Tech >= 25 ? "Balanced"
        : string.Join('-',new[] {("Aggression",Aggression),("Expansion",Expansion),("Tech",Tech)}
            .Where(a => a.Item2 >= 30).OrderByDescending(a => a.Item2).Select(a => a.Item1));

    public bool Valid => Aggression >= 0 && Expansion >= 0 && Tech >= 0 && Aggression+Expansion+Tech == 100;
}

/// <summary>Whom a bot's soldiers attack among the enemies in range: the nearest, the nearest spread at most
/// AiAttackersPerEnemy to an enemy, or spread with the wounded and ranged enemies around the nearest one first.</summary>
public enum AiTargeting { Nearest,Spread,Focus }

/// <summary>How well a difficulty plays, the same for every style (owner model, build-49): how often it decides
/// (tactics: defence, guards, assault; strategy: building, training, tech, workers, every Nth tactical think), its base
/// worker count, builders per project, whether parties regroup on the march, its target choice, and the share of a
/// party below which it falls back. Hard plays fair: no income bonus.</summary>
public sealed record AiSkill(string Name,int ThinkTicks,int StrategicEvery,int BaseWorkers,int Builders,bool Legs,AiTargeting Targeting,int FallBackPercent)
{
    public static AiSkill For(int difficulty) => difficulty switch {
        <= 0 => new("Easy",3*SimWorld.TicksPerSecond,1,8,1,false,AiTargeting.Nearest,20),
        1 => new("Normal",SimWorld.TicksPerSecond,1,10,2,true,AiTargeting.Spread,33),
        // Hard decides tactics twice a second, but strategy once, so the placement search costs no more than Normal's.
        _ => new("Hard",SimWorld.TicksPerSecond/2,2,12,3,true,AiTargeting.Focus,50) };
}

/// <summary>Which techniques a bot researches: those that help a unit type it has alive, only with the bank holding
/// twice the cost (Tech below 20), from money nothing else has reserved (20-49), or every technique with its cost
/// reserved before training (50 and more).</summary>
public enum AiResearch { Thrifty,Fielded,Eager }

/// <summary>Every number the bot's decisions read, derived once from its difficulty and style with integer maths.</summary>
public sealed record AiProfile
{
    public required AiSkill Skill { get; init; }
    public required AiStyle Style { get; init; }
    /// <summary>No assault party forms before this tick: (100 - Aggression) x 6 s.</summary>
    public int AssaultFromTicks { get; init; }
    /// <summary>Workers before the 2 per Mill: the skill's base plus Expansion x 6 / 100.</summary>
    public int Workers { get; init; }
    public int ExpansionTicks { get; init; }
    public int ExpansionIntervalTicks { get; init; }
    public int MaxCitadels { get; init; }
    public int UpgradeTicks { get; init; }
    public AiResearch Research { get; init; }
    /// <summary>The Watchtower before the first military building (Aggression 30 or less).</summary>
    public bool TowerFirst { get; init; }
    /// <summary>More military buildings once every producer is training, without waiting for a bank that pays twice over
    /// (Aggression 50 or more).</summary>
    public bool FreeMilitary { get; init; }

    public static AiProfile For(AiPersonality personality,AiStyle style)
    {
        if (!style.Valid) throw new ArgumentException($"A style's values must be 0 or more and sum to 100: {style}");
        int a = style.Aggression,e = style.Expansion,t = style.Tech,second = SimWorld.TicksPerSecond;
        var skill = AiSkill.For(personality.Difficulty);
        return new() { Skill = skill,Style = style,
            AssaultFromTicks = (100-a)*6*second,
            Workers = skill.BaseWorkers+e*6/100,
            ExpansionTicks = (900-6*e)*second,
            ExpansionIntervalTicks = (600-4*e)*second,
            MaxCitadels = Math.Min(4,2+(e+16)/33),
            UpgradeTicks = (600-4*t)*second,
            Research = t >= 50 ? AiResearch.Eager : t >= 20 ? AiResearch.Fielded : AiResearch.Thrifty,
            TowerFirst = a <= 30,
            FreeMilitary = a >= 50 };
    }
}

/// <summary>A named bot: its English name, fixed style and lobby text. Its difficulty stays the shipped .rai's.</summary>
public sealed record AiBotEntry(string Id,string Name,AiStyle Style,string Description);

/// <summary>The named bots (owner model, build-49). The shipped .rai files give each a difficulty and 0-100 knobs, but
/// only Difficulty and Aggressiveness were ever read, so several played the same game; each now has its own style.
/// No two share a style. A personality missing here (a new .rai) plays Balanced.</summary>
public static class AiRoster
{
    public static IReadOnlyList<AiBotEntry> Bots { get; } = [
        new("FarmerMaggot","Farmer Maggot",new(10,70,20),"Tends his fields: many workers and Mills, an early second base, rare attacks."),
        new("Gollum","Gollum",new(60,30,10),"Impatient: small early attacks, little research."),
        new("Saleme","Saleme",new(20,30,50),"Hides behind towers and research, attacks late."),
        new("Default","Default",AiStyle.Balanced,"A bit of everything."),
        new("Frodo","Frodo",new(20,60,20),"Explorer: spreads to new bases quickly and defends them."),
        new("Gimli","Gimli",new(30,20,50),"Stubborn: a tower first, upgrades early, attacks with heavy parties."),
        new("Grishnakh","Grishnakh",new(70,20,10),"Orc raider: early and frequent attacks with cheap units."),
        new("Legolas","Legolas",new(40,0,60),"Precise: researches early and attacks with an upgraded army."),
        new("Shelob","Shelob",new(10,50,40),"Lurker: builds up quietly, defends well, strikes late with a large army."),
        new("Aragorn","Aragorn",new(50,20,30),"Steady pressure from mid-game, army kept upgraded."),
        new("Gandalf","Gandalf",new(30,10,60),"Fast tech, then decisive attacks with a researched army."),
        new("Saruman","Saruman",new(30,50,20),"Industry: several bases, then a flood of soldiers."),
        new("Smaug","Smaug",new(90,0,10),"All-out aggression from the first minutes.")];

    public static AiBotEntry Entry(string id) => Bots.FirstOrDefault(b => string.Equals(b.Id,id,StringComparison.OrdinalIgnoreCase))
        ?? new(id,id,AiStyle.Balanced,"No description yet.");

    public static string Difficulty(int level) => AiSkill.For(level).Name;

    /// <summary>"Normal · Tech-Aggression (30/20/50)": difficulty, style and its Aggression/Expansion/Tech values.</summary>
    public static string Summary(int difficulty,AiStyle style) => $"{Difficulty(difficulty)} · {style.Label} ({style.Aggression}/{style.Expansion}/{style.Tech})";
}
