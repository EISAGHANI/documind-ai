namespace DocuMind.Api.Tenancy;

// Temporary tenant resolution until authentication exists: reads the X-Tenant-Id header,
// falling back to the configured development tenant. Replace with a claim-based provider later.
public class HeaderTenantProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    : ITenantProvider
{
    public const string HeaderName = "X-Tenant-Id";

    public Guid TenantId
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();
            if (Guid.TryParse(header, out var tenantId))
            {
                return tenantId;
            }

            return configuration.GetValue<Guid>("Tenancy:DevelopmentTenantId");
        }
    }
}
