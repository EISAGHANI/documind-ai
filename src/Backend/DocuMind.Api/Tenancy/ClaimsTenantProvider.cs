namespace DocuMind.Api.Tenancy;

// Reads the tenant from the signed-in user's token. Requests without a valid tenant claim get
// Guid.Empty, which matches no rows, so the query filters return nothing rather than leaking data.
public class ClaimsTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    public Guid TenantId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(TenantClaims.TenantId)?.Value, out var tenantId)
            ? tenantId
            : Guid.Empty;
}
