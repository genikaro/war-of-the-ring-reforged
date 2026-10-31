namespace Reforged.Simulation;

/// <summary>A named personality or a difficulty-limited random choice in the lobby.</summary>
public sealed record AiBotOption(string Id,int Difficulty,bool IsRandom = false)
{
    public string Name=>IsRandom ? "Random" : AiRoster.Entry(Id).Name;
    public string Label=>IsRandom ? $"Random ({AiRoster.Difficulty(Difficulty)})" : $"Bot · {Name} ({AiRoster.Difficulty(Difficulty)})";
}

public static class AiBotOptions
{
    public static string RandomId(int difficulty)=>difficulty is >=0 and <=2 ? $"Random:{difficulty}"
        : throw new ArgumentOutOfRangeException(nameof(difficulty));

    public static bool TryRandomDifficulty(string id,out int difficulty)
    {
        for(int level=0;level<3;level++)
            if(string.Equals(id,RandomId(level),StringComparison.OrdinalIgnoreCase)) { difficulty=level; return true; }
        difficulty=-1; return false;
    }

    /// <summary>Easy, Normal, Hard; random first in each available group, then names alphabetically.</summary>
    public static AiBotOption[] For(IEnumerable<AiBotOption> personalities)
        =>[.. personalities.Where(p=>!p.IsRandom).Select(p=>p with {Difficulty=Math.Clamp(p.Difficulty,0,2)})
            .GroupBy(p=>p.Difficulty).OrderBy(g=>g.Key).SelectMany(g=>
                new[] {new AiBotOption(RandomId(g.Key),g.Key,true)}
                    .Concat(g.OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.Id,StringComparer.Ordinal)))];

    /// <summary>Resolve at match load from its saved seed and slot. Restart repeats the draw;
    /// a new Start supplies a fresh seed. Pool ordering and other slots cannot alter this slot's draw.</summary>
    public static AiBotChoice Resolve(AiBotChoice choice,IEnumerable<AiBotOption> personalities,int seed,int slot)
    {
        if(!TryRandomDifficulty(choice.Id,out int level)) return choice;
        if(slot<0) throw new ArgumentOutOfRangeException(nameof(slot));
        var pool=personalities.Where(p=>!p.IsRandom && Math.Clamp(p.Difficulty,0,2)==level)
            .OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        if(pool.Length==0) throw new InvalidOperationException($"No {AiRoster.Difficulty(level)} bot is available.");
        uint draw;
        unchecked
        {
            draw=(uint)seed^((uint)slot+1)*0x9e3779b9u^((uint)level+1)*0x85ebca6bu;
            draw=(draw^(draw>>16))*0x7feb352du;
            draw=(draw^(draw>>15))*0x846ca68bu;
            draw^=draw>>16;
        }
        return new(pool[draw%(uint)pool.Length].Id,choice.Traits);
    }
}
