using Microsoft.Extensions.Options;

namespace BookingSystem.Ai;

/// <summary>
/// Gives the business-local "now" (Kyiv by default). Backed by <see cref="TimeProvider"/>
/// so experiments can pin the date of each scenario.
/// </summary>
public sealed class AgentClock(TimeProvider timeProvider, IOptions<AiOptions> options)
{
    private readonly TimeZoneInfo _zone = ResolveZone(options.Value.TimeZone);

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _zone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    private static TimeZoneInfo ResolveZone(string id)
    {
        // "Europe/Kyiv" only exists in newer tzdata; "Europe/Kiev" is the legacy alias.
        foreach (var candidate in new[] { id, "Europe/Kiev" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out var zone))
                return zone;
        }

        return TimeZoneInfo.Utc;
    }
}
