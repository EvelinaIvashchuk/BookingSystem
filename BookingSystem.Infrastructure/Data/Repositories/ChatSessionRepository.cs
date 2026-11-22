using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class ChatSessionRepository(ApplicationDbContext db) : IChatSessionRepository
{
    public Task<ChatSession?> GetAsync(Guid sessionId, CancellationToken ct = default) =>
        db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task AddAsync(ChatSession session, CancellationToken ct = default) =>
        await db.ChatSessions.AddAsync(session, ct);

    public async Task<IReadOnlyList<ChatSessionMessage>> GetRecentMessagesAsync(
        Guid sessionId, int count, CancellationToken ct = default)
    {
        if (count <= 0) return [];

        var latest = await db.ChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Id)
            .Take(count)
            .ToListAsync(ct);

        latest.Reverse();
        return latest;
    }

    public Task AddMessagesAsync(IEnumerable<ChatSessionMessage> messages, CancellationToken ct = default) =>
        db.ChatMessages.AddRangeAsync(messages, ct);
}
