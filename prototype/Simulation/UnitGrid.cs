namespace Reforged.Simulation;

/// <summary>Units bucketed by ground position, kept current by SimUnit's position setters. A query returns every
/// unit, dead or alive, whose position lies within a radius, plus some beyond it: callers keep their exact tests,
/// so answers never change, only how many units they look at. Results come in bucket order, not id order.</summary>
public sealed class UnitGrid
{
    public const int BucketMm = 4000;
    private readonly GridNavigation _navigation;
    private readonly int _columns,_rows;
    private readonly List<SimUnit>[] _buckets;
    private readonly List<SimUnit>[] _attackBuckets;
    private readonly Dictionary<int,List<SimUnit>> _targeters = [];
    private readonly HashSet<SimUnit> _transits = [];

    public UnitGrid(GridNavigation navigation)
    {
        _navigation=navigation;
        int widthMm=navigation.Width*navigation.CellSize,heightMm=navigation.Height*navigation.CellSize;
        _columns = Math.Max(1,(widthMm+BucketMm-1)/BucketMm); _rows = Math.Max(1,(heightMm+BucketMm-1)/BucketMm);
        _buckets = new List<SimUnit>[_columns*_rows];
        _attackBuckets = new List<SimUnit>[_buckets.Length];
        for (int i = 0; i < _buckets.Length; i++) { _buckets[i] = []; _attackBuckets[i]=[]; }
    }

    public int Count { get; private set; }
    /// <summary>The largest body radius, attack or melee reach, and attack or melee gap of any unit added so far.
    /// They only grow, which keeps reach bounds built from them conservative.</summary>
    public int MaxBodyRadiusMm { get; private set; }
    public int MaxReachMm { get; private set; }
    public int MaxGapMm { get; private set; }
    public int MaxMovementMm { get; private set; }

    // Positions outside the map clamp to the border buckets; queries clamp the same way, so they still find them.
    private int Column(int x) => Math.Clamp(x/BucketMm,0,_columns-1);
    private int Row(int z) => Math.Clamp(z/BucketMm,0,_rows-1);

    public void Add(SimUnit unit)
    {
        unit.Grid = this; unit.GridBucket = Row(unit.Z)*_columns+Column(unit.X);
        _buckets[unit.GridBucket].Add(unit); Count++;
        unit.AttackBucket=-1; AttackPositionChanged(unit); TransitChanged(unit);
        if (unit.Target>=0) TargetersList(unit.Target).Add(unit);
        Measure(unit.Definition); MeasureReach(unit.ReachMm);
    }

    /// <summary>A unit's researched reach (Eagle Fletching) can exceed its definition's.</summary>
    internal void MeasureReach(int reachMm) => MaxReachMm = Math.Max(MaxReachMm,reachMm);

    public void Rebuild(IEnumerable<SimUnit> units)
    {
        foreach (var bucket in _buckets) bucket.Clear();
        foreach (var bucket in _attackBuckets) bucket.Clear();
        _targeters.Clear(); _transits.Clear();
        Count = MaxBodyRadiusMm = MaxReachMm = MaxGapMm = MaxMovementMm = 0;
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
        MaxMovementMm=Math.Max(MaxMovementMm,(int)((SimWorld.MovementTickSpan*definition.RunScale/1_000_000+999)/1000));
    }

    private List<SimUnit> TargetersList(int target)
    {
        if (!_targeters.TryGetValue(target,out var list)) _targeters[target]=list=[];
        return list;
    }

    internal void Retargeted(SimUnit unit,int previous)
    {
        if (previous==unit.Target) return;
        if (_targeters.TryGetValue(previous,out var old)) old.Remove(unit);
        if (unit.Target>=0) TargetersList(unit.Target).Add(unit);
    }

    public IEnumerable<SimUnit> Targeters(int target) => _targeters.TryGetValue(target,out var list) ? list : [];

    internal void TransitChanged(SimUnit unit)
    {
        if (unit.TransitNode>=0 && !unit.GroundSegment) _transits.Add(unit); else _transits.Remove(unit);
    }

    internal void AttackPositionChanged(SimUnit unit)
    {
        int bucket=-1;
        if (unit.AttackNode>=0)
        {
            var (x,z)=_navigation.Center(unit.AttackNode); bucket=Row(z)*_columns+Column(x);
        }
        if (bucket==unit.AttackBucket) return;
        if (unit.AttackBucket>=0) _attackBuckets[unit.AttackBucket].Remove(unit);
        if (bucket>=0) _attackBuckets[bucket].Add(unit);
        unit.AttackBucket=bucket;
    }

    public List<SimUnit> NearAttackPositions(int x,int z,int radius,List<SimUnit> into)
    {
        into.Clear();
        for (int row=Row(z-radius);row<=Row(z+radius);row++)
        for (int column=Column(x-radius);column<=Column(x+radius);column++)
            into.AddRange(_attackBuckets[row*_columns+column]);
        return into;
    }

    /// <summary>Broad phase for a swept step, including movement earlier in the tick (bounded by the fastest
    /// unit's run budget) and entire reserved deck links, which can extend beyond point buckets.</summary>
    public List<SimUnit> AlongMovement(int ax,int az,int bx,int bz,int radius,List<SimUnit> into)
    {
        int half=Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az))/2+1;
        Near(ax+(bx-ax)/2,az+(bz-az)/2,radius+half+MaxMovementMm,into);
        foreach (var u in _transits)
            if (!u.GroundSegment && !into.Contains(u)) into.Add(u);
        return into;
    }

    public List<SimUnit> InRectangle(int x0,int z0,int x1,int z1,List<SimUnit> into)
    {
        into.Clear();
        for (int row=Row(z0);row<=Row(z1);row++)
        for (int column=Column(x0);column<=Column(x1);column++)
            into.AddRange(_buckets[row*_columns+column]);
        foreach (var u in _transits)
            if (!u.GroundSegment && !into.Contains(u)) into.Add(u);
        return into;
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
