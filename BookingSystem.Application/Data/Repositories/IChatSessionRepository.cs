using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession?> GetAsync(Guid sessionId, CancellationToken ct = default);

    Task AddAsync(ChatSession session, CancellationToken ct = default);

    /// <summary>Returns the last <paramref name="count"/> messages of a session in chronological order.</summary>
    Task<IReadOnlyList<ChatSessionMessage>> GetRecentMessagesAsync(Guid sessionId, int count, CancellationToken ct = default);

    Task AddMessagesAsync(IEnumerable<ChatSessionMessage> messages, CancellationToken ct = default);
}
