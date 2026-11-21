namespace Reforged.Simulation;

/// <summary>A bot's game plan (owner model, build-49): a point in the Aggression / Expansion / Tech triangle whose
/// three values sum to 100.</summary>
public sealed record AiStyle(int Aggression,int Expansion,int Tech)
{
    public static readonly AiStyle Balanced = new(34,33,33);

    /// <summary>"Balanced" when every value is at least 25, else the axes of 30 or more, heaviest first ("Aggression-Tech").</summary>
    public string Label => string.Join('-',Axes);

    /// <summary>Build-223: the label's parts, ["Balanced"] or the heavy axes heaviest first, for the lobby to name in
    /// the install's language.</summary>
    public string[] Axes => Aggression >= 25 && Expansion >= 25 && Tech >= 25 ? ["Balanced"]
        : [.. new[] {("Aggression",Aggression),("Expansion",Expansion),("Tech",Tech)}
            .Where(a => a.Item2 >= 30).OrderByDescending(a => a.Item2).Select(a => a.Item1)];

    public bool Valid => Aggression >= 0 && Expansion >= 0 && Tech >= 0 && Aggression+Expansion+Tech == 100;
}

/// <summary>Whom a bot's soldiers attack among the enemies in range: the nearest, the nearest spread at most
/// AiAttackersPerEnemy to an enemy, or spread with the wounded and ranged enemies around the nearest one first.</summary>
public enum AiTargeting { Nearest,Spread,Focus }

/// <summary>How well a difficulty plays, the same for every style (owner model, build-49): how often it decides
/// (tactics: defence, guards, assault; strategy: building, training, tech, workers, every Nth tactical think), its base
/// worker count, builders per project, whether parties regroup on the march, its target choice, the share of a
/// party below which it falls back, and whether it pulls badly wounded soldiers out of a fight (build-50). Hard plays
/// fair: no income bonus.</summary>
public sealed record AiSkill(string Name,int ThinkTicks,int StrategicEvery,int BaseWorkers,int Builders,bool Legs,AiTargeting Targeting,int FallBackPercent,
    bool PullsWounded = false)
{
    public static AiSkill For(int difficulty) => difficulty switch {
        <= 0 => new("Easy",3*SimWorld.TicksPerSecond,1,8,1,false,AiTargeting.Nearest,20),
        1 => new("Normal",SimWorld.TicksPerSecond,1,10,2,true,AiTargeting.Spread,33),
        // Hard decides tactics twice a second, but strategy once, so the placement search costs no more than Normal's.
        _ => new("Hard",SimWorld.TicksPerSecond/2,2,12,2,true,AiTargeting.Focus,50,PullsWounded: true) };
}

/// <summary>Which techniques a bot researches: those that help a unit type it has alive, only with the bank holding
/// twice the cost (Tech below 20), from money nothing else has reserved (20-49), or every technique with its cost
/// reserved before training (50 and more).</summary>
public enum AiResearch { Thrifty,Fielded,Eager }

/// <summary>What a party attacks, by kind (build-50): enemy soldiers and heroes, armed buildings (towers), military
/// producers, research buildings, the rest (Citadels, Mills, Foundries, Camps), and workers. Resources (build-53) are
/// the Mills and Foundries alone; an order without them ranks them as Economy.</summary>
public enum AiTargetClass { Army,Defenses,Military,Tech,Economy,Workers,Resources }

/// <summary>A trait the player picks for a bot slot (owner model, build-51), applied after the style, in this order.</summary>
public enum AiTrait { Rusher,Expansionist,Defender,Relentless,Marksman,Demolisher,Raider }

/// <summary>A trait's lobby text and the traits it cannot be combined with.</summary>
public sealed record AiTraitInfo(AiTrait Trait,string Description,AiTrait[] Excludes)
{
    public string Name => Trait.ToString();
}

/// <summary>A bot slot's choice: a personality or difficulty-limited random choice and its traits,
/// written "Gimli", "Gimli+Defender+Marksman" or "Random:1+Defender".</summary>
public sealed record AiBotChoice(string Id,AiTrait[] Traits)
{
    public override string ToString() => string.Join('+',Traits.Select(t => t.ToString()).Prepend(Id));
}

/// <summary>Every number the bot's decisions read, derived once from its difficulty, style and traits with integer maths.</summary>
public sealed record AiProfile
{
    public static readonly IReadOnlyList<AiTargetClass> DefaultTargetOrder =
        [AiTargetClass.Army,AiTargetClass.Defenses,AiTargetClass.Military,AiTargetClass.Tech,AiTargetClass.Economy,AiTargetClass.Workers];

    public required AiSkill Skill { get; init; }
    public required AiStyle Style { get; init; }
    /// <summary>No assault party forms before this tick: (100 - Aggression) x 6 s.</summary>
    public int AssaultFromTicks { get; init; }
    /// <summary>Desired workers before the 2 per Mill: the skill's base plus Expansion x 6 / 100.
    /// Actual recruitment is capped at five per usable source plus the skill's one or two builders.</summary>
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
    /// <summary>The order a party attacks in (build-50), and whether it first answers the soldiers and towers hurting
    /// it; a later trait may send a party at buildings regardless.</summary>
    public IReadOnlyList<AiTargetClass> TargetOrder { get; init; } = DefaultTargetOrder;
    public bool ThreatsFirst { get; init; } = true;
    // Set by traits (build-51); the defaults leave the style's play unchanged.
    /// <summary>Parties of the war party's smallest size (Rusher).</summary>
    public bool SmallestParties { get; init; }
    /// <summary>The cheapest soldier a producer offers, never saving for a better one (Rusher).</summary>
    public bool CheapSoldiers { get; init; }
    /// <summary>No research before this tick (Rusher: 15:00).</summary>
    public int ResearchFromTicks { get; init; }
    /// <summary>Minimum towers per Citadel (Defender: 2). Zero uses the normal minimum of one;
    /// every four completed local buildings add another, up to three extra towers (build-117).</summary>
    public int TowersPerCitadel { get; init; }
    /// <summary>Expansion guards of a full party instead of half (Defender).</summary>
    public bool FullGuards { get; init; }
    /// <summary>Before AssaultFromTicks an assault may still leave once this many towers stand and the army holds this
    /// many soldiers; 0 for never (Defender: 3 towers, 12 soldiers).</summary>
    public int EarlyAssaultTowers { get; init; }
    public int EarlyAssaultArmy { get; init; }
    /// <summary>The share of a party's size idle at home that reinforces the party out (Relentless: half).</summary>
    public int ReinforcePercent { get; init; } = 100;
    /// <summary>The army's melee share in millionths, or -1 for the war party's (Marksman: 40%).</summary>
    public int MeleeSharePpm { get; init; } = -1;
    /// <summary>Raids on the enemy economy, from this tick (Raider, build-53: half the assault gate, (100 - Aggression) x 3 s).</summary>
    public bool Raids { get; init; }
    public int RaidFromTicks { get; init; }

    public static AiProfile For(AiPersonality personality,AiStyle style,IReadOnlyList<AiTrait>? traits = null)
    {
        if (!style.Valid) throw new ArgumentException($"A style's values must be 0 or more and sum to 100: {style}");
        int a = style.Aggression,e = style.Expansion,t = style.Tech,second = SimWorld.TicksPerSecond;
        var skill = AiSkill.For(personality.Difficulty);
        var profile = new AiProfile { Skill = skill,Style = style,
            AssaultFromTicks = (100-a)*6*second,
            Workers = skill.BaseWorkers+e*6/100,
            ExpansionTicks = (900-6*e)*second,
            ExpansionIntervalTicks = (600-4*e)*second,
            MaxCitadels = Math.Min(4,2+(e+16)/33),
            UpgradeTicks = (600-4*t)*second,
            Research = t >= 50 ? AiResearch.Eager : t >= 20 ? AiResearch.Fielded : AiResearch.Thrifty,
            TowerFirst = a <= 30,
            FreeMilitary = a >= 50 };
        foreach (var trait in AiRoster.Validate(traits ?? []))
            profile = trait switch {
                AiTrait.Rusher => profile with { SmallestParties = true,AssaultFromTicks = 0,CheapSoldiers = true,ResearchFromTicks = 900*second,
                    ExpansionTicks = Math.Max(profile.ExpansionTicks,900*second),Workers = profile.Workers-2,FreeMilitary = true,TowerFirst = false },
                AiTrait.Expansionist => profile with { ExpansionTicks = 240*second,ExpansionIntervalTicks = 180*second,MaxCitadels = 5,Workers = profile.Workers+4 },
                AiTrait.Defender => profile with { TowerFirst = true,TowersPerCitadel = 2,FullGuards = true,AssaultFromTicks = 1200*second,
                    EarlyAssaultTowers = 3,EarlyAssaultArmy = 12 },
                AiTrait.Relentless => profile with { Skill = profile.Skill with { FallBackPercent = 0,PullsWounded = false },ReinforcePercent = 50 },
                AiTrait.Marksman => profile with { MeleeSharePpm = 400_000 },
                // Its assaults go for the economy too: Mills and Foundries first, food being every army's bottleneck.
                AiTrait.Raider => profile with { Raids = true,RaidFromTicks = (100-a)*3*second,
                    TargetOrder = [AiTargetClass.Army,AiTargetClass.Resources,AiTargetClass.Defenses,AiTargetClass.Military,AiTargetClass.Tech,AiTargetClass.Economy,AiTargetClass.Workers] },
                AiTrait.Demolisher => profile with { ThreatsFirst = false,
                    TargetOrder = [AiTargetClass.Military,AiTargetClass.Tech,AiTargetClass.Economy,AiTargetClass.Defenses,AiTargetClass.Army,AiTargetClass.Workers] },
                _ => profile };
        return profile;
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

    /// <summary>The traits a player may give a bot (build-51). Raider, Scout and hero use need new behaviour and come later.</summary>
    public static IReadOnlyList<AiTraitInfo> Traits { get; } = [
        new(AiTrait.Rusher,"Small parties from the first soldiers, the cheapest units, more Barracks; no research or expansion before 15:00, fewer workers.",
            [AiTrait.Expansionist,AiTrait.Defender]),
        new(AiTrait.Expansionist,"A new Citadel from 4:00 and every 3:00 after, up to five, and more workers.",[AiTrait.Rusher]),
        new(AiTrait.Defender,"A tower first and at least two by every Citadel, with more as the base grows; guards of a full party. Attacks with three towers and twelve soldiers, or from 20:00.",
            [AiTrait.Rusher]),
        new(AiTrait.Relentless,"Parties never fall back and the wounded never retreat; half a party at home joins the one out.",[]),
        new(AiTrait.Marksman,"An army of 60% ranged units instead of 20%.",[]),
        new(AiTrait.Demolisher,"Parties raze buildings, military first, and ignore the soldiers and towers hitting them.",[]),
        new(AiTrait.Raider,"Assaults go for Mills and Foundries first, and small raids of three to six (by the bot's Harassment) hit workers elsewhere while they fight.",[])];

    public static AiTraitInfo Trait(AiTrait trait) => Traits.First(t => t.Trait == trait);

    /// <summary>Distinct traits in their application order; throws on a pair that excludes each other.</summary>
    public static AiTrait[] Validate(IEnumerable<AiTrait> traits)
    {
        var chosen = traits.Distinct().Order().ToArray();
        foreach (var trait in chosen)
            foreach (var other in Trait(trait).Excludes.Where(chosen.Contains))
                throw new ArgumentException($"{trait} and {other} cannot be combined");
        return chosen;
    }

    /// <summary>Reads a bot slot, "Gimli" or "Gimli+Defender+Marksman": the only parser of trait names. Throws on an
    /// unknown trait or a pair that excludes each other; the personality id is checked by whoever loads it.</summary>
    public static AiBotChoice Parse(string text)
    {
        var parts = text.Split('+',StringSplitOptions.TrimEntries);
        if (parts[0].Length == 0) throw new ArgumentException("A bot needs a personality: "+text);
        var traits = parts.Skip(1).Select(name => Traits.FirstOrDefault(t => string.Equals(t.Name,name,StringComparison.OrdinalIgnoreCase))?.Trait
            ?? throw new ArgumentException($"Unknown bot trait \"{name}\" in \"{text}\" (known: {string.Join(", ",Traits.Select(t => t.Name))})"));
        return new(parts[0],Validate(traits));
    }

    /// <summary>"Defender, Marksman", or "" without traits.</summary>
    public static string TraitList(IEnumerable<AiTrait> traits) => string.Join(", ",traits.Select(t => t.ToString()));

    public static string Difficulty(int level) => AiSkill.For(level).Name;

    /// <summary>"Normal · Tech-Aggression (30/20/50)": difficulty, style and its Aggression/Expansion/Tech values.</summary>
    public static string Summary(int difficulty,AiStyle style) => $"{Difficulty(difficulty)} · {style.Label} ({style.Aggression}/{style.Expansion}/{style.Tech})";
}
