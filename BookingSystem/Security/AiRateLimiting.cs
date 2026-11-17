using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BookingSystem.Ai;
using BookingSystem.Resources;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BookingSystem.Security;

/// <summary>
/// Rate limiting of the AI chat (each request costs a paid / quota-limited LLM call):
/// <list type="bullet">
/// <item>per client IP: <see cref="RateLimitOptions.PerIpPerMinute"/> requests per minute;</item>
/// <item>per signed-in user, otherwise per chat session, otherwise per IP:
///       <see cref="RateLimitOptions.PerSessionPerHour"/> requests per hour. Users are keyed by
///       account so that rotating the session id does not reset the limit.</item>
/// </list>
/// Other endpoints are not limited.
/// </summary>
public static class AiRateLimiting
{
    public const string ChatPath = "/Ai/Chat";

    /// <summary>Header in which the widget sends its chat session id.</summary>
    public const string SessionHeader = "X-Chat-Session";

    public static IServiceCollection AddAiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<AiOptions>>((limiter, ai) =>
            {
                limiter.GlobalLimiter = CreateLimiter(ai.Value.RateLimit);
                limiter.OnRejected = OnRejectedAsync;
            });
        return services;
    }

    public static PartitionedRateLimiter<HttpContext> CreateLimiter(RateLimitOptions options)
    {
        if (!options.Enabled)
            return PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetNoLimiter("off"));

        var perIp = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsChat(context)
                ? RateLimitPartition.GetFixedWindowLimiter("ip:" + ClientIp(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PerIpPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })
                : RateLimitPartition.GetNoLimiter("none"));

        var perSession = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsChat(context)
                ? RateLimitPartition.GetFixedWindowLimiter(SessionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PerSessionPerHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0
                })
                : RateLimitPartition.GetNoLimiter("none"));

        return PartitionedRateLimiter.CreateChained(perIp, perSession);
    }

    public static bool IsChat(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals(ChatPath, StringComparison.OrdinalIgnoreCase);

    public static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>user:{id} → session:{guid} → ip:{address}.</summary>
    public static string SessionKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
            return "user:" + userId;

        if (Guid.TryParse(context.Request.Headers[SessionHeader], out var sessionId))
            return "session:" + sessionId;

        return "ip:" + ClientIp(context);
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AiRateLimiting).FullName!)
            .LogWarning("AI chat rate limit exceeded for {Ip} / {Key}",
                ClientIp(context.HttpContext), SessionKey(context.HttpContext));

        var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResources>>();
        await response.WriteAsJsonAsync(new { reply = localizer["Chat_RateLimited"].Value }, ct);
    }
}
