namespace Reforged.Input;

/// <summary>Selection-only state. Issued orders still record explicit unit IDs.</summary>
public sealed class ControlGroups
{
    private readonly SortedSet<int>[] _groups = Enumerable.Range(0,10).Select(_ => new SortedSet<int>()).ToArray();

    public int[] Recall(int slot,Func<int,bool> livingFriendly)
    {
        var group = Group(slot); group.RemoveWhere(id => !livingFriendly(id)); return group.ToArray();
    }

    public void Assign(int slot,IEnumerable<int> selection,Func<int,bool> livingFriendly,bool append = false)
    {
        var group = Group(slot);
        if (!append) group.Clear();
        group.UnionWith(selection.Where(livingFriendly)); group.RemoveWhere(id => !livingFriendly(id));
    }

    private SortedSet<int> Group(int slot) => slot is >= 0 and < 10 ? _groups[slot] : throw new ArgumentOutOfRangeException(nameof(slot));
}
