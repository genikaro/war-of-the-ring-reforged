using Reforged.Simulation;

static class AttackVariantChecks
{
    private static UnitDefinition Profile(params int[] weights) => UnitDefinition.Probe with {
        Attacks=weights.Select((weight,index)=>new MeleeAttack("attack-"+index,weight,30,[new(12,1000000)])).ToArray() };

    public static void Run(Action<bool,string> check)
    {
        var source=Profile(10000,10000,10000);
        for(int id=0;id<32;id++)
        {
            var choices=Enumerable.Range(0,30).Select(serial=>AttackVariants.Choose(source,id,serial)).ToArray();
            check(choices.Chunk(3).All(cycle=>cycle.Distinct().Count()==3),
                "equal source weights use every variant in each three-swing cycle for unit "+id);
            check(Enumerable.Range(2,choices.Length-2).All(i=>choices[i]!=choices[i-1] || choices[i]!=choices[i-2]),
                "equal-weight attacks cannot get stuck repeating one animation for unit "+id);
        }
        check(!Enumerable.Range(0,12).Select(s=>AttackVariants.Choose(source,0,s)).SequenceEqual(
            Enumerable.Range(0,12).Select(s=>AttackVariants.Choose(source,1,s))),"different units have independently shuffled attack orders");
        var weighted=Profile(30000,10000,20000);var reduced=Profile(3,1,2);
        var sequence=Enumerable.Range(0,120).Select(s=>AttackVariants.Choose(weighted,7,s)).ToArray();
        check(sequence.Chunk(6).All(cycle=>new[]{0,1,2}.Select(index=>cycle.Count(c=>c==index)).SequenceEqual([3,1,2])),
            "unequal source weights retain their exact relative frequencies in each shuffled cycle");
        check(sequence.SequenceEqual(Enumerable.Range(0,120).Select(s=>AttackVariants.Choose(reduced,7,s))),
            "multiplying all source weights by 10000 does not change animation variety");
        check(sequence.SequenceEqual(Enumerable.Range(0,120).Select(s=>AttackVariants.Choose(weighted,7,s))),"attack animation choice is deterministic on replay");
        var ranged=source with {ProjectileSpeedMmPerSecond=25000,ReachMm=13000,MinimumRangeMm=1000,MeleeDpsMilli=12000,
            Attacks=[new("bow",10000,40,[new(20,1000000)]),..source.Attacks.Select(a=>a with {Melee=true})]};
        check(Enumerable.Range(0,20).All(s=>AttackVariants.Choose(ranged,0,s)==0),"ranged attacks never select contact animations");
        check(Enumerable.Range(0,3).Select(s=>AttackVariants.Choose(ranged,0,s,melee:true)).Order().SequenceEqual([1,2,3]),
            "ranged units use all eligible contact variants during melee");
        check(Enumerable.Range(0,20).All(s=>AttackVariants.Choose(Profile(10000),9,s)==0),"single-clip units keep their available attack");
        var large=Profile(int.MaxValue,int.MaxValue-1);
        check(new[]{0,1,123456789,int.MaxValue}.All(s=>AttackVariants.Choose(large,int.MaxValue,s) is 0 or 1),
            "large coprime weights and serials stay within eligible variants without overflow");
    }

    public static void Cached(Action<bool,string> check,string folder)
    {
        var rules=CombatRules.Parse(File.ReadAllText(Path.Combine(folder,"combat/rules.json")));
        foreach(var unit in rules.Units.Values)
            foreach(bool melee in unit.HasMelee ? new[]{false,true} : new[]{false})
            {
                var eligible=Enumerable.Range(0,unit.Attacks.Length).Where(i=>!unit.IsRanged || unit.Attacks[i].Melee==melee).ToArray();
                check(Enumerable.Range(0,12).All(s=>eligible.Contains(AttackVariants.Choose(unit,5,s,melee))),unit.DisplayName+" selects eligible source clips");
                if(eligible.Select(i=>unit.Attacks[i].Weight).Distinct().Count()==1)
                    check(Enumerable.Range(0,eligible.Length).Select(s=>AttackVariants.Choose(unit,5,s,melee)).Order().SequenceEqual(eligible),
                        unit.DisplayName+" uses every equal-weight source attack before repeating");
            }
    }
}
