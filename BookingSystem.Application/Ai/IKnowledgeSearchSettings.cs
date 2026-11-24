namespace BookingSystem.Ai;

/// <summary>
/// The method the agent uses to search the rental-policy knowledge base, selectable by an administrator at runtime.
/// The choice is stored in the database and applies from the next question; until an administrator chooses,
/// <see cref="SearchOptions.Mode"/> (<c>Ai:Search:Mode</c>) is used. Modes that need embeddings fall back to
/// full-text search by themselves when no embedding key is configured.
/// </summary>
public interface IKnowledgeSearchSettings
{
    IReadOnlyList<KnowledgeSearchMode> GetAvailableModes();

    /// <summary>The administrator's choice, or <see cref="SearchOptions.Mode"/> when nothing was chosen.</summary>
    Task<KnowledgeSearchMode> GetActiveModeAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentException"><paramref name="mode"/> is not a defined <see cref="KnowledgeSearchMode"/>.</exception>
    Task SetActiveModeAsync(KnowledgeSearchMode mode, CancellationToken cancellationToken = default);
}
