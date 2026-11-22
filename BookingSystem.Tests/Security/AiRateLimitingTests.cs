using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BookingSystem.Ai;
using BookingSystem.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace BookingSystem.Tests.Security;

public class AiRateLimitingTests
{
    private static HttpContext Request(
        string path = AiRateLimiting.ChatPath, string method = "POST", string ip = "10.0.0.1",
        Guid? session = null, string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (session is { } s) context.Request.Headers[AiRateLimiting.SessionHeader] = s.ToString();
        if (userId is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));
        return context;
    }

    private static int Accepted(PartitionedRateLimiter<HttpContext> limiter, int attempts, Func<int, HttpContext> request)
    {
        var accepted = 0;
        for (var i = 0; i < attempts; i++)
        {
            using var lease = limiter.AttemptAcquire(request(i));
            if (lease.IsAcquired) accepted++;
        }
        return accepted;
    }

    [Fact]
    public void PerIpLimit_RejectsRequestsAboveTheLimit()
    {
        var limiter = AiRateLimiting.CreateLimiter(new RateLimitOptions { PerIpPerMinute = 3, PerSessionPerHour = 100 });

        Accepted(limiter, 5, i => Request(session: Guid.NewGuid())).Should().Be(3);
        Accepted(limiter, 1, _ => Request(ip: "10.0.0.2")).Should().Be(1, "another IP has its own window");
    }

    [Fact]
    public void PerSessionLimit_AppliesToOneSession()
    {
        var limiter = AiRateLimiting.CreateLimiter(new RateLimitOptions { PerIpPerMinute = 100, PerSessionPerHour = 2 });
        var session = Guid.NewGuid();

        Accepted(limiter, 3, _ => Request(session: session)).Should().Be(2);
        Accepted(limiter, 1, _ => Request(session: Guid.NewGuid())).Should().Be(1);
    }

    [Fact]
    public void SignedInUser_CannotResetTheHourlyLimitByRotatingSessions()
    {
        var limiter = AiRateLimiting.CreateLimiter(new RateLimitOptions { PerIpPerMinute = 100, PerSessionPerHour = 2 });

        Accepted(limiter, 4, i => Request(ip: $"10.0.1.{i}", session: Guid.NewGuid(), userId: "user-1")).Should().Be(2);
    }

    [Theory]
    [InlineData("/Car", "GET")]
    [InlineData("/Ai/History", "GET")]
    [InlineData("/Ai/Confirm/abc", "POST")]
    public void OtherEndpoints_AreNotLimited(string path, string method)
    {
        var limiter = AiRateLimiting.CreateLimiter(new RateLimitOptions { PerIpPerMinute = 1, PerSessionPerHour = 1 });

        Accepted(limiter, 10, _ => Request(path, method)).Should().Be(10);
    }

    [Fact]
    public void Disabled_AcceptsEverything()
    {
        var limiter = AiRateLimiting.CreateLimiter(new RateLimitOptions { Enabled = false, PerIpPerMinute = 1 });

        Accepted(limiter, 10, _ => Request()).Should().Be(10);
    }

    [Fact]
    public void SessionKey_PrefersUserThenSessionThenIp()
    {
        var session = Guid.NewGuid();

        AiRateLimiting.SessionKey(Request(session: session, userId: "u1")).Should().Be("user:u1");
        AiRateLimiting.SessionKey(Request(session: session)).Should().Be("session:" + session);
        AiRateLimiting.SessionKey(Request(ip: "10.9.9.9")).Should().Be("ip:10.9.9.9");
    }
}
