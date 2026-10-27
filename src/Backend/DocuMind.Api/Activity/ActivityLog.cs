using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using DocuMind.Api.Observability;
using DocuMind.Api.Tenancy;

namespace DocuMind.Api.Activity;

public interface IActivityLog
{
    Task RecordAsync(string action, string? entityType = null, object? entityId = null, string? detail = null,
        CancellationToken cancellationToken = default);
}

// Writes to the append-only ActivityLog table. Failures are logged, never thrown: an audit write
// must not turn a successful user action into an error.
public class ActivityLogWriter(
    ApplicationDbContext db,
    ITenantProvider tenantProvider,
    IHttpContextAccessor httpContext,
    ICorrelationIdAccessor correlation,
    ILogger<ActivityLogWriter> logger) : IActivityLog
{
    public async Task RecordAsync(string action, string? entityType = null, object? entityId = null, string? detail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpContext.HttpContext;
            var userClaim = http?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            db.ActivityLog.Add(new ActivityLogEntry
            {
                TenantId = tenantProvider.TenantId,
                UserId = Guid.TryParse(userClaim, out var userId) ? userId : null,
                Action = action,
                EntityType = entityType,
                EntityId = entityId?.ToString(),
                Detail = detail is { Length: > 1000 } ? detail[..1000] : detail,
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                CorrelationId = correlation.CorrelationId,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to write activity log entry {Action}", action);
        }
    }
}
