namespace TodoApp.Models;

/// <summary>
/// A user's game state. Exactly one per <see cref="User"/>, enforced by using
/// <see cref="UserId"/> as the primary key rather than a surrogate.
/// </summary>
public class Character
{
    public Guid UserId { get; set; }

    public string Name { get; set; } = "Adventurer";

    /// <summary>Key into the avatar set defined by the client.</summary>
    public string AvatarKey { get; set; } = "fox";

    /// <summary>
    /// Source of truth for progression. Level is never stored, it is always derived from
    /// this via <see cref="Progression.LevelCurve"/> so the two can never disagree.
    /// </summary>
    public int TotalXp { get; set; }

    public int TasksCompleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ---------------------------------------------------------------- RPG layer

    /// <summary>
    /// Key into <see cref="Rpg.ClassCatalog"/>. Null means the character predates class
    /// selection, or has not chosen yet; the UI prompts rather than picking for them.
    /// </summary>
    public string? ClassKey { get; set; }

    public int Strength { get; set; } = 10;
    public int Dexterity { get; set; } = 10;
    public int Constitution { get; set; } = 10;
    public int Intelligence { get; set; } = 10;
    public int Wisdom { get; set; } = 10;
    public int Charisma { get; set; } = 10;

    /// <summary>
    /// Current hit points. Max is deliberately not stored: it is derived from class, level
    /// and Constitution for the same reason level is derived from XP (DEC-002).
    /// </summary>
    public int CurrentHitPoints { get; set; }

    /// <summary>
    /// The anti-inflation gate. Earned only by completing real tasks and spent on fights,
    /// so it is a balance rather than a computation (DEC-003).
    /// </summary>
    public int Stamina { get; set; }

    public int Gold { get; set; }

    /// <summary>
    /// Salvage material, spent at the forge. A balance rather than a computation: its credits
    /// come from items that salvage destroys, so there is no surviving state to recompute it
    /// from, which is the same argument that already justifies <see cref="Gold"/> and
    /// <see cref="Stamina"/> on this row (DEC-002).
    /// </summary>
    public int Essence { get; set; }

    /// <summary>Anchor for passive hit point regeneration, avoiding a background job.</summary>
    public DateTimeOffset? HitPointsUpdatedAt { get; set; }

    /// <summary>
    /// How many times this character has begun again. Zero for everyone who never has.
    /// </summary>
    /// <remarks>
    /// Stored rather than counted from the chronicle's ascension entries (DEC-002 argues the
    /// other way, and loses here): the chronicle is deletable from the account danger zone, and
    /// a counter that a tidy-up can silently reset is worse than one that is written down. It is
    /// also stamped onto every entry as its era, which a count over those same entries could not
    /// do without reading them all back on every write.
    /// </remarks>
    public int Ascensions { get; set; }

    /// <summary>When the last ascension happened. Null until the first one.</summary>
    public DateTimeOffset? AscendedAt { get; set; }

    public Rpg.AbilityScores AbilityScores
    {
        get => new(Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma);
        set
        {
            Strength = value.Strength;
            Dexterity = value.Dexterity;
            Constitution = value.Constitution;
            Intelligence = value.Intelligence;
            Wisdom = value.Wisdom;
            Charisma = value.Charisma;
        }
    }
}
