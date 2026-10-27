using DocuMind.Api.Data;
using DocuMind.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocuMind.Api.Tests.Unit;

public class FixedTenantProvider(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; set; } = tenantId;
}

// Guards the multi-tenancy invariant without needing a database: any entity that carries a
// TenantId must be filtered by it, or a new table could silently leak across companies.
public class TenantFilterModelTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=unused", npgsql => npgsql.UseVector())
                .Options,
            new FixedTenantProvider(Guid.NewGuid()));

    [Fact]
    public void Every_entity_with_a_TenantId_has_a_query_filter()
    {
        using var db = CreateContext();

        var unfiltered = db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") is not null)
            .Where(e => e.GetQueryFilter() is null)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.Empty(unfiltered);
    }

    [Fact]
    public void Tenant_owned_entities_exist_in_the_model()
    {
        using var db = CreateContext();

        var tenantOwned = db.Model.GetEntityTypes().Count(e => e.FindProperty("TenantId") is not null);

        Assert.True(tenantOwned >= 4, "Expected documents, chunks, API keys and audits to be tenant-owned");
    }

    [Fact]
    public void Jobs_are_infrastructure_and_are_not_tenant_filtered()
    {
        using var db = CreateContext();

        Assert.Null(db.Model.FindEntityType(typeof(Jobs.Job))!.GetQueryFilter());
    }
}
