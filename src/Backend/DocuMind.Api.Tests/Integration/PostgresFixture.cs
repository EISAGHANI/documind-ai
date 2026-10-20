using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using DocuMind.Api.Tests.Unit;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Testcontainers.PostgreSql;
using Xunit;

namespace DocuMind.Api.Tests.Integration;

// A real Postgres with pgvector, started once per test class collection. If Docker isn't available
// (e.g. a laptop without Docker Desktop) the tests are skipped rather than failed; CI always has Docker.
public class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool Available { get; private set; }
    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
            await _container.StartAsync();
            await using var db = CreateContext(Guid.Empty);
            await db.Database.MigrateAsync();
            Available = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && IsDockerProblem(ex)
                                   && Environment.GetEnvironmentVariable("CI") is not "true")
        {
            Available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public ApplicationDbContext CreateContext(Guid tenantId) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
                .Options,
            new FixedTenantProvider(tenantId));

    // Seeds a company with a document and one embedded chunk
    public async Task<(Guid TenantId, Guid DocumentId)> SeedTenantAsync(string name, float embeddingValue)
    {
        var tenant = new Tenant { Name = name };
        await using var db = CreateContext(tenant.Id);
        db.Tenants.Add(tenant);
        var document = new Document { TenantId = tenant.Id, Title = $"{name} contract", FileName = "c.pdf", StoragePath = $"{tenant.Id}/c.pdf" };
        document.Chunks.Add(new DocumentChunk
        {
            TenantId = tenant.Id,
            ChunkIndex = 0,
            Content = $"Confidential terms of {name}",
            PageNumber = 1,
            Embedding = new Vector(Enumerable.Repeat(embeddingValue, DocumentChunk.EmbeddingDimensions).ToArray()),
        });
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return (tenant.Id, document.Id);
    }

    // Docker missing or not running surfaces as various exception types; anything else is a real failure
    private static bool IsDockerProblem(Exception ex) =>
        ex.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("docker", StringComparison.OrdinalIgnoreCase)
        || ex.InnerException is { } inner && IsDockerProblem(inner);
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
