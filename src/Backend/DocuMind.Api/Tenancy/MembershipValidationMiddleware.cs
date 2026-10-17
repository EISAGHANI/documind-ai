using System.Security.Claims;
using DocuMind.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Tenancy;

// Access tokens stay valid until they expire, even if the person was removed or demoted meanwhile.
// This checks the token's company and role against the database (cached briefly) on every request:
// removed members are rejected and role changes apply within a minute instead of at the next refresh.
public class MembershipValidationMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, IOptions<IdentityOptions> identityOptions)
    {
        var user = context.User;
        var userIdClaim = user.FindFirst(identityOptions.Value.ClaimsIdentity.UserIdClaimType)?.Value;
        var tenantIdClaim = user.FindFirst(TenantClaims.TenantId)?.Value;

        if (user.Identity?.IsAuthenticated == true
            && Guid.TryParse(userIdClaim, out var userId)
            && Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            var role = await cache.GetOrCreateAsync(TenantClaims.MembershipCacheKey(userId, tenantId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await db.TenantMemberships
                    .IgnoreQueryFilters()
                    .Where(m => m.UserId == userId && m.TenantId == tenantId)
                    .Select(m => (string?)m.Role.ToString())
                    .FirstOrDefaultAsync(context.RequestAborted);
            });

            if (role is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Keep the role claim in line with the database for authorization checks in this request
            if (user.Identity is ClaimsIdentity identity && identity.FindFirst(TenantClaims.Role)?.Value != role)
            {
                if (identity.FindFirst(TenantClaims.Role) is { } stale)
                {
                    identity.RemoveClaim(stale);
                }
                identity.AddClaim(new Claim(TenantClaims.Role, role));
            }
        }

        await next(context);
    }
}
