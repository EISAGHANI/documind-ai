namespace DocuMind.Api.Tenancy;

// Claims added to every signed-in user's token, and the authorization policy built on them
public static class TenantClaims
{
    // Cache key for the membership check in MembershipValidationMiddleware
    public static string MembershipCacheKey(Guid userId, Guid tenantId) => $"membership:{userId}:{tenantId}";

    public const string TenantId = "documind:tenant_id";
    public const string Role = "documind:tenant_role";

    public const string AdminPolicy = "TenantAdmin";
}
