using Reforged.Simulation;

namespace Reforged.Input;

public readonly record struct PlayerOrder(OrderKind Kind, int Cell = -1, int Target = -1);

/// <summary>Shared targeting rules for the panel, hotkeys and contextual clicks.</summary>
public sealed class OrderInput
{
    public OrderKind? Pending { get; private set; }

    public bool Begin(OrderKind kind)
    {
        Pending = kind is OrderKind.Move or OrderKind.Attack or OrderKind.AttackMove or OrderKind.Guard or OrderKind.Patrol or OrderKind.ForceAttack ? kind : null;
        return Pending != null;
    }

    public void Cancel() => Pending = null;

    public bool Resolve(int cell, int enemy, out PlayerOrder order,int nonHostile = -1,bool keepTargeting = false)
    {
        var kind = Pending ?? (enemy >= 0 ? OrderKind.Attack : OrderKind.Move);
        // Attack is one player action. Record the precise simulation order
        // only after targeting, so panel and A share every target category.
        if (Pending == OrderKind.Attack)
            kind = enemy >= 0 ? OrderKind.Attack : nonHostile >= 0 ? OrderKind.ForceAttack : OrderKind.AttackMove;
        int target = kind == OrderKind.ForceAttack ? (enemy >= 0 ? enemy : nonHostile)
            : kind == OrderKind.Guard ? nonHostile : kind == OrderKind.Attack ? enemy : -1;
        order = new(kind,cell,target);
        if (kind == OrderKind.Guard && enemy >= 0) return false;
        if (kind == OrderKind.Attack ? enemy < 0 : cell < 0) return false;
        if (!keepTargeting) Pending = null; return true;
    }
}
