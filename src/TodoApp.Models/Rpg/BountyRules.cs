namespace TodoApp.Models.Rpg;

/// <summary>
/// What an overdue task is worth. The whole of DEC-013 in one function.
/// </summary>
/// <remarks>
/// Overdue tasks are bounties, never debuffs. Nothing here, and nothing anywhere on the hunt
/// path, subtracts from the player for having a backlog: the multiplier is at or above 100 for
/// every input, so the worst an on-time task can do is pay a monster's ordinary gold.
/// <para>
/// A stick applied to somebody who is already behind compounds the problem it claims to solve.
/// A bounty makes the backlog the interesting part of the board instead, which is the amendment
/// the product owner made to DEC-013 before any of it was written.
/// </para>
/// </remarks>
public static class BountyRules
{
    /// <summary>Days overdue at which the multiplier reaches its ceiling and stops.</summary>
    public const int CapDays = 30;

    /// <summary>The multiplier at zero days overdue, as a percentage. The floor, too.</summary>
    public const int BasePercent = 100;

    /// <summary>The most a contract's purse can ever be multiplied by, as a percentage.</summary>
    public const int MaxPercent = 200;

    /// <summary>The contract's gold multiplier, as a percentage. Never below 100 (DEC-013).</summary>
    /// <remarks>
    /// 0 days pays 100, 15 pays 150, 29 pays 196, and 30 pays 200. So does 90, and so does 365.
    /// <para>
    /// The cap is what stops stalling from being a strategy. Without it, the multiplier is a
    /// reward for elapsed time, which is a resource the player gets for free and in unlimited
    /// quantity: leaving one Epic task to rot for a year would out-earn clearing the list, and
    /// the app would be paying people to not use it. Capped at a month, waiting past thirty days
    /// is worth exactly zero additional gold forever, while the archetype promotion at the same
    /// threshold makes the fight harder for the same stamina and the same capped purse. Past the
    /// cap, patience only costs.
    /// </para>
    /// <para>
    /// It multiplies gold and nothing else. It cannot reach XP (DEC-012), stamina (DEC-003), the
    /// rung the contract is written at, or a completion, so there is no route from a backlog to
    /// the only number that compounds.
    /// </para>
    /// </remarks>
    public static int BountyPercent(int daysOverdue) =>
        BasePercent + Math.Min(100, Math.Max(0, daysOverdue) * 100 / CapDays);
}
