using DocuMind.Api.Entities;
using DocuMind.Api.Jobs;
using DocuMind.Api.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace DocuMind.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class TenantIsolationTests(PostgresFixture postgres)
{
    [SkippableFact]
    public async Task Each_tenant_only_sees_its_own_documents_and_chunks()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        var (tenantA, docA) = await postgres.SeedTenantAsync("Acme", 0.1f);
        var (tenantB, docB) = await postgres.SeedTenantAsync("Globex", 0.1f);

        await using var asA = postgres.CreateContext(tenantA);
        await using var asB = postgres.CreateContext(tenantB);

        Assert.Equal([docA], await asA.Documents.Select(d => d.Id).Where(id => id == docA || id == docB).ToListAsync());
        Assert.Equal([docB], await asB.Documents.Select(d => d.Id).Where(id => id == docA || id == docB).ToListAsync());
        Assert.All(await asA.DocumentChunks.ToListAsync(), c => Assert.Equal(tenantA, c.TenantId));
    }

    [SkippableFact]
    public async Task Requests_without_a_tenant_see_nothing()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        await postgres.SeedTenantAsync("Initech", 0.2f);

        await using var anonymous = postgres.CreateContext(Guid.Empty);

        Assert.Empty(await anonymous.Documents.ToListAsync());
        Assert.Empty(await anonymous.DocumentChunks.ToListAsync());
    }

    [SkippableFact]
    public async Task Vector_search_never_returns_another_tenants_chunks()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        // Identical embeddings: only the tenant filter can keep the other company's chunk out
        var (tenantA, _) = await postgres.SeedTenantAsync("Umbrella", 0.3f);
        var (tenantB, _) = await postgres.SeedTenantAsync("Hooli", 0.3f);
        var query = new Vector(Enumerable.Repeat(0.3f, DocumentChunk.EmbeddingDimensions).ToArray());

        await using var asA = postgres.CreateContext(tenantA);
        var hits = await asA.DocumentChunks
            .OrderBy(c => c.Embedding!.CosineDistance(query))
            .Take(50)
            .ToListAsync();

        Assert.NotEmpty(hits);
        Assert.DoesNotContain(hits, c => c.TenantId == tenantB);
    }

    [SkippableFact]
    public async Task Full_text_search_is_tenant_scoped_too()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        var (tenantA, _) = await postgres.SeedTenantAsync("Wayne", 0.4f);
        var (tenantB, _) = await postgres.SeedTenantAsync("Stark", 0.4f);

        await using var asA = postgres.CreateContext(tenantA);
        var hits = await asA.DocumentChunks
            .Where(c => c.SearchVector.Matches(EF.Functions.PlainToTsQuery(DocumentChunk.TextSearchConfig, "confidential")))
            .ToListAsync();

        Assert.NotEmpty(hits);
        Assert.DoesNotContain(hits, c => c.TenantId == tenantB);
    }

    [SkippableFact]
    public async Task Ignoring_filters_is_the_only_way_to_cross_tenants()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        var (tenantA, _) = await postgres.SeedTenantAsync("Cyberdyne", 0.5f);
        var (tenantB, _) = await postgres.SeedTenantAsync("Tyrell", 0.5f);

        await using var asA = postgres.CreateContext(tenantA);
        var tenants = await asA.Documents.IgnoreQueryFilters().Select(d => d.TenantId).Distinct().ToListAsync();

        Assert.Contains(tenantA, tenants);
        Assert.Contains(tenantB, tenants);
    }
}

[Collection(PostgresCollection.Name)]
public class JobQueueTests(PostgresFixture postgres)
{
    private sealed class NoCorrelation : ICorrelationIdAccessor
    {
        public string? CorrelationId => "test-correlation";
    }

    [SkippableFact]
    public async Task Enqueueing_the_same_entity_twice_keeps_a_single_active_job()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        var entityId = Guid.NewGuid();

        await using (var db = postgres.CreateContext(Guid.Empty))
        {
            var queue = new JobQueue(db, new NoCorrelation(), Options.Create(new JobOptions()));
            await queue.EnqueueAsync(JobTypes.ProcessDocument, entityId);
        }

        await using (var db = postgres.CreateContext(Guid.Empty))
        {
            var queue = new JobQueue(db, new NoCorrelation(), Options.Create(new JobOptions()));
            await queue.EnqueueAsync(JobTypes.ProcessDocument, entityId);
        }

        await using var check = postgres.CreateContext(Guid.Empty);
        var jobs = await check.Jobs.Where(j => j.EntityId == entityId).ToListAsync();
        var job = Assert.Single(jobs);
        Assert.Equal("test-correlation", job.CorrelationId);
        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [SkippableFact]
    public async Task Concurrent_claims_never_hand_the_same_job_to_two_workers()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        var entityIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();
        await using (var seed = postgres.CreateContext(Guid.Empty))
        {
            seed.Jobs.AddRange(entityIds.Select(id => new Job { Type = "claim.test", EntityId = id }));
            await seed.SaveChangesAsync();
        }

        var claimed = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            var mine = new List<Guid>();
            while (true)
            {
                await using var db = postgres.CreateContext(Guid.Empty);
                var rows = await db.Jobs.FromSql($"""
                    UPDATE "Jobs" SET "Status" = 'Running', "Attempts" = "Attempts" + 1,
                        "LockedUntil" = now() + interval '5 minutes', "LockedBy" = 'test'
                    WHERE "Id" = (
                        SELECT "Id" FROM "Jobs"
                        WHERE "Type" = 'claim.test' AND "Status" = 'Pending' AND "RunAfter" <= now()
                        ORDER BY "RunAfter" FOR UPDATE SKIP LOCKED LIMIT 1)
                    RETURNING *
                    """).AsNoTracking().ToListAsync();
                if (rows.Count == 0)
                {
                    return mine;
                }
                mine.Add(rows[0].Id);
            }
        })));

        var all = claimed.SelectMany(c => c).ToList();
        Assert.Equal(entityIds.Count, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }
}
