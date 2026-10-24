namespace Reforged.Simulation;

/// <summary>Units bucketed by ground position, kept current by SimUnit's position setters. A query returns every
/// unit, dead or alive, whose position lies within a radius, plus some beyond it: callers keep their exact tests,
/// so answers never change, only how many units they look at. Results come in bucket order, not id order.</summary>
public sealed class UnitGrid
{
    public const int BucketMm = 8000;
    private readonly int _columns,_rows;
    private readonly List<SimUnit>[] _buckets;

    public UnitGrid(int widthMm,int heightMm)
    {
        _columns = Math.Max(1,(widthMm+BucketMm-1)/BucketMm); _rows = Math.Max(1,(heightMm+BucketMm-1)/BucketMm);
        _buckets = new List<SimUnit>[_columns*_rows];
        for (int i = 0; i < _buckets.Length; i++) _buckets[i] = [];
    }

    public int Count { get; private set; }
    /// <summary>The largest body radius, attack or melee reach, and attack or melee gap of any unit added so far.
    /// They only grow, which keeps reach bounds built from them conservative.</summary>
    public int MaxBodyRadiusMm { get; private set; }
    public int MaxReachMm { get; private set; }
    public int MaxGapMm { get; private set; }

    // Positions outside the map clamp to the border buckets; queries clamp the same way, so they still find them.
    private int Column(int x) => Math.Clamp(x/BucketMm,0,_columns-1);
    private int Row(int z) => Math.Clamp(z/BucketMm,0,_rows-1);

    public void Add(SimUnit unit)
    {
        unit.Grid = this; unit.GridBucket = Row(unit.Z)*_columns+Column(unit.X);
        _buckets[unit.GridBucket].Add(unit); Count++;
        Measure(unit.Definition); MeasureReach(unit.ReachMm);
    }

    /// <summary>A unit's researched reach (Eagle Fletching) can exceed its definition's.</summary>
    internal void MeasureReach(int reachMm) => MaxReachMm = Math.Max(MaxReachMm,reachMm);

    public void Rebuild(IEnumerable<SimUnit> units)
    {
        foreach (var bucket in _buckets) bucket.Clear();
        Count = MaxBodyRadiusMm = MaxReachMm = MaxGapMm = 0;
        foreach (var unit in units) Add(unit);
    }

    internal void Moved(SimUnit unit)
    {
        int bucket = Row(unit.Z)*_columns+Column(unit.X);
        if (bucket == unit.GridBucket) return;
        _buckets[unit.GridBucket].Remove(unit); _buckets[bucket].Add(unit); unit.GridBucket = bucket;
    }

    internal void Measure(UnitDefinition definition)
    {
        MaxBodyRadiusMm = Math.Max(MaxBodyRadiusMm,definition.BodyRadiusMm);
        MaxReachMm = Math.Max(MaxReachMm,Math.Max(definition.ReachMm,definition.MeleeReachMm));
        MaxGapMm = Math.Max(MaxGapMm,Math.Max(definition.AttackGapMm,definition.MeleeGapMm));
    }

    /// <summary>Fills the list with the units in every bucket that a circle of this radius at (x, z) touches.</summary>
    public List<SimUnit> Near(int x,int z,int radius,List<SimUnit> into)
    {
        into.Clear();
        int columnEnd = Column(x+radius),rowEnd = Row(z+radius);
        for (int row = Row(z-radius); row <= rowEnd; row++)
        for (int column = Column(x-radius); column <= columnEnd; column++)
            into.AddRange(_buckets[row*_columns+column]);
        return into;
    }
}
