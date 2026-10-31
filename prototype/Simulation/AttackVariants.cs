namespace Reforged.Simulation;

/// <summary>Deterministic shuffled weighted cycles. Scaling all source weights equally must not
/// change the sequence or trap a unit in a long run of one animation.</summary>
public static class AttackVariants
{
    private static long Gcd(long a,long b)
    {
        while (b != 0) (a,b) = (b,a%b);
        return a;
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value^(value>>30))*0xbf58476d1ce4e5b9UL;
            value = (value^(value>>27))*0x94d049bb133111ebUL;
            return value^(value>>31);
        }
    }

    public static int Choose(UnitDefinition unit,int unitId,int serial,bool melee = false)
    {
        var eligible = Enumerable.Range(0,unit.Attacks.Length).Where(i => !unit.IsRanged || unit.Attacks[i].Melee == melee).ToArray();
        if (eligible.Length == 0) throw new ArgumentException("No eligible attack animation");
        if (eligible.Length == 1) return eligible[0];
        long divisor = eligible.Select(i => (long)unit.Attacks[i].Weight).Aggregate(Gcd);
        long total = eligible.Sum(i => unit.Attacks[i].Weight/divisor);
        long position = Math.Max(0,serial)%total,cycle = Math.Max(0,serial)/total;
        ulong seed = Mix(unchecked((ulong)(uint)unitId*0x9e3779b97f4a7c15UL+(ulong)cycle+0x632be59bd9b4e019UL));
        long offset = (long)(seed%(ulong)total);
        long step = 1+(long)(Mix(seed+0x9e3779b97f4a7c15UL)%(ulong)(total-1));
        while (Gcd(step,total) != 1) step = step == total-1 ? 1 : step+1;
        // A coprime stride visits every weighted slot exactly once in this cycle.
        long pick = (long)(((UInt128)(ulong)position*(ulong)step+(ulong)offset)%(ulong)total);
        foreach (int index in eligible)
        {
            long weight = unit.Attacks[index].Weight/divisor;
            if (pick < weight) return index;
            pick -= weight;
        }
        throw new InvalidOperationException("Attack animation weights did not cover the cycle");
    }
}
