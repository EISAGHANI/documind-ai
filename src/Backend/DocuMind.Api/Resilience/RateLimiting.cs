using System.Security.Claims;
using System.Threading.RateLimiting;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace DocuMind.Api.Resilience;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    // Requests per minute for everything a signed-in user does, counted per user and per company
    public int UserPerMinute { get; set; } = 120;
    public int TenantPerMinute { get; set; } = 600;

    // Expensive endpoints (chat, uploads, audit runs), per company
    public int ExpensivePerMinute { get; set; } = 30;

    // Anonymous endpoints (login, register), per client IP
    public int AnonymousPerMinute { get; set; } = 20;
}

public class QuotaOptions
{
    public const string SectionName = "Quotas";

    // 0 disables a limit
    public int MaxDocumentsPerTenant { get; set; } = 500;
    public int MaxUploadsPerTenantPerDay { get; set; } = 200;
}

public static class RateLimitPolicies
{
    public const string Expensive = "expensive";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddDocuMindRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new();
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.Configure<QuotaOptions>(configuration.GetSection(QuotaOptions.SectionName));
        if (!options.Enabled)
        {
            return services;
        }

        var redis = configuration["Redis:ConnectionString"];

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }
                return ValueTask.CompletedTask;
            };

            // Chained so a request must fit under both its user's and its company's budget
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    Partition(context, redis, "user", UserKey(context), options.UserPerMinute, AnonymousKey(context), options.AnonymousPerMinute)),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    TenantKey(context) is { } tenant
                        ? Window(redis, $"tenant:{tenant}", options.TenantPerMinute)
                        : RateLimitPartition.GetNoLimiter("none")));

            limiter.AddPolicy(RateLimitPolicies.Expensive, context =>
                Window(redis, $"expensive:{TenantKey(context) ?? AnonymousKey(context)}", options.ExpensivePerMinute));
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(
        HttpContext context, string? redis, string prefix, string? userKey, int userLimit, string ipKey, int ipLimit) =>
        userKey is not null
            ? Window(redis, $"{prefix}:{userKey}", userLimit)
            : Window(redis, $"ip:{ipKey}", ipLimit);

    private static RateLimitPartition<string> Window(string? redis, string key, int permits)
    {
        if (permits <= 0)
        {
            return RateLimitPartition.GetNoLimiter(key);
        }

        return string.IsNullOrEmpty(redis)
            ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })
            : RateLimitPartition.Get(key, k => new RedisFixedWindowLimiter(redis, k, permits, TimeSpan.FromMinutes(1)));
    }

    private static string? UserKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    private static string? TenantKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(TenantClaims.TenantId) : null;

    // With UseForwardedHeaders this is the real client address rather than the proxy's
    private static string AnonymousKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

// Fixed-window limiter shared by every API instance through Redis. Fails open: if Redis is down, requests are allowed
// rather than turning a cache outage into an API outage.
internal sealed class RedisFixedWindowLimiter : RateLimiter
{
    private static readonly Lazy<Dictionary<string, ConnectionMultiplexer>> Connections = new(() => []);
    private readonly ConnectionMultiplexer? _connection;
    private readonly string _key;
    private readonly int _permits;
    private readonly TimeSpan _window;

    public RedisFixedWindowLimiter(string connectionString, string key, int permits, TimeSpan window)
    {
        _key = key;
        _permits = permits;
        _window = window;
        lock (Connections.Value)
        {
            if (!Connections.Value.TryGetValue(connectionString, out _connection))
            {
                try
                {
                    var options = ConfigurationOptions.Parse(connectionString);
                    options.AbortOnConnectFail = false;
                    _connection = ConnectionMultiplexer.Connect(options);
                    Connections.Value[connectionString] = _connection;
                }
                catch (RedisException)
                {
                    _connection = null;
                }
            }
        }
    }

    // Stateless, so the framework may drop and recreate it freely
    public override TimeSpan? IdleDuration => TimeSpan.FromMinutes(5);

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
        AcquireAsyncCore(permitCount, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return Allowed;
        }

        try
        {
            var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)_window.TotalSeconds;
            var redisKey = $"documind:rl:{_key}:{bucket}";
            var db = _connection.GetDatabase();
            var count = await db.StringIncrementAsync(redisKey, permitCount);
            if (count == permitCount)
            {
                await db.KeyExpireAsync(redisKey, _window + TimeSpan.FromSeconds(5));
            }

            return count <= _permits ? Allowed : new RejectedLease(_window);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return Allowed;
        }
    }

    private static readonly RateLimitLease Allowed = new AllowedLease();

    private sealed class AllowedLease : RateLimitLease
    {
        public override bool IsAcquired => true;
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }

    private sealed class RejectedLease(TimeSpan retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter;
                return true;
            }
            metadata = null;
            return false;
        }
    }
}
