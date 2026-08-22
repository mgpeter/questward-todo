namespace TodoApp.Models.Rpg;

/// <summary>
/// What beginning again costs and what it pays.
/// </summary>
/// <remarks>
/// Code-held per DEC-004, and one file so retuning the exchange is one thing to read.
/// <para>
/// The payout is essence and only essence, which is the whole reason ascending is allowed to
/// exist at all: essence buys affixes at the forge and can buy nothing else. It cannot become
/// XP (DEC-012), it cannot become stamina (DEC-003), and it cannot become a completion. What a
/// player carries out of an era is magnitude on the gear of the next one, never progress.
/// </para>
/// <para>
/// The level gate is here rather than in the service for the same reason the rates are: a
/// number a designer wants to move should not be somewhere a reviewer has to read a transaction
/// to find.
/// </para>
/// </remarks>
public static class AscendRules
{
    /// <summary>
    /// The level at which the option appears.
    /// </summary>
    /// <remarks>
    /// Ten, which on the standing curve is 2,250 experience: enough real work that ascending
    /// reads as a decision about an era rather than a button pressed on the way past. Below it
    /// the payout would also be near nothing, so the gate is mostly saving someone from spending
    /// a character to be told so.
    /// </remarks>
    public const int MinimumLevel = 10;

    public const int GoldPerEssence = 10;

    public const int StaminaPerEssence = 5;

    /// <summary>Paid per level reached, so the work done in the era is what most of it comes from.</summary>
    public const int EssencePerLevel = 5;

    public static bool MayAscend(int level) => level >= MinimumLevel;

    /// <summary>
    /// What a character of this level, holding this gold and stamina, renders down to.
    /// </summary>
    /// <remarks>
    /// Integer division throughout, and deliberately not rounded up: nine gold is worth nothing,
    /// which is the same arithmetic the shop and the forge already use on their own balances.
    /// </remarks>
    public static int EssenceFor(int gold, int stamina, int level) =>
        Math.Max(0, gold) / GoldPerEssence
        + Math.Max(0, stamina) / StaminaPerEssence
        + Math.Max(0, level) * EssencePerLevel;
}
