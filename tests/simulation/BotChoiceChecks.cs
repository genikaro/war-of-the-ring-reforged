using Reforged.Simulation;

static class BotChoiceChecks
{
    public static void Run(Action<bool,string> check)
    {
        AiBotOption[] source=[new("Smaug",2),new("Gimli",1),new("Saleme",0),new("Aragorn",2),
            new("Gollum",0),new("Default",1),new("FarmerMaggot",0)];
        var options=AiBotOptions.For(source);
        check(options.Select(p=>p.Difficulty).SequenceEqual(options.Select(p=>p.Difficulty).Order()),
            "the bot list runs from Easy through Normal to Hard");
        check(options.Where(p=>p.IsRandom).Select(p=>p.Difficulty).SequenceEqual([0,1,2]),
            "each available difficulty has exactly one random choice");
        check(options.GroupBy(p=>p.Difficulty).All(g=>g.First().IsRandom
            && g.Skip(1).Select(p=>p.Name).SequenceEqual(g.Skip(1).Select(p=>p.Name).Order(StringComparer.OrdinalIgnoreCase))),
            "random leads each difficulty group and named bots follow alphabetically");
        check(options.Where(p=>p.IsRandom).Select(p=>p.Label).SequenceEqual(["Random (Easy)","Random (Normal)","Random (Hard)"])
            && options.Single(p=>p.Id=="FarmerMaggot").Label=="Bot · Farmer Maggot (Easy)",
            "bot labels show the difficulty and preserve readable personality names");
        foreach(int level in new[] {0,1,2})
        {
            var request=AiRoster.Parse(AiBotOptions.RandomId(level)+"+Defender+Marksman");
            bool valid=true,stable=true; var seen=new HashSet<string>();
            for(int seed=0;seed<128;seed++) for(int slot=0;slot<8;slot++)
            {
                var choice=AiBotOptions.Resolve(request,source,seed,slot); seen.Add(choice.Id);
                valid&=source.Any(p=>p.Id==choice.Id && p.Difficulty==level)
                    && choice.Traits.SequenceEqual([AiTrait.Defender,AiTrait.Marksman]);
                stable&=choice.ToString()==AiBotOptions.Resolve(request,source.Reverse(),seed,slot).ToString()
                    && choice.ToString()==AiBotOptions.Resolve(AiRoster.Parse(request.ToString()),options,seed,slot).ToString();
            }
            check(valid,"random choices retain their requested difficulty and traits, level "+level);
            check(stable,"saved seed and slot repeat the same draw regardless of pool order, level "+level);
            check(seen.Count==source.Count(p=>p.Difficulty==level),"new match seeds draw every eligible personality, level "+level);
        }
        var named=AiRoster.Parse("Gimli+Rusher");
        check(AiBotOptions.Resolve(named,source,7,1)==named,"named personality choices are not rerolled");
        var only=AiBotOptions.For([new("Gimli",1)]);
        check(only.Length==2 && only[0].IsRandom && only[0].Difficulty==1
            && AiBotOptions.Resolve(AiRoster.Parse(only[0].Id),only,7,0).Id=="Gimli",
            "a single available difficulty produces only its own random option");
        try
        {
            AiBotOptions.Resolve(AiRoster.Parse(AiBotOptions.RandomId(2)),only,7,0);
            check(false,"an empty difficulty pool must be rejected");
        }
        catch(InvalidOperationException) { check(true,"an empty random pool never falls back to a different difficulty"); }
    }
}
