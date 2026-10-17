namespace DocuMind.Api.Tenancy;

public interface ITenantProvider
{
    Guid TenantId { get; }
}
