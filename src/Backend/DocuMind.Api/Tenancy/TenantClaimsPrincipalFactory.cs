using System.Security.Claims;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Tenancy;

// Adds the user's company and role to their token at sign-in and on every token refresh,
// so a role change takes effect at the next refresh.
public class TenantClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> options,
    ApplicationDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var membership = await db.TenantMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .OrderBy(m => m.JoinedAt)
            .FirstOrDefaultAsync();

        if (membership is not null)
        {
            identity.AddClaim(new Claim(TenantClaims.TenantId, membership.TenantId.ToString()));
            identity.AddClaim(new Claim(TenantClaims.Role, membership.Role.ToString()));
        }

        return identity;
    }
}
