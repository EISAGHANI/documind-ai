using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    // Read on every query, so each request only sees rows for its own tenant
    private Guid CurrentTenantId => _tenantProvider.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StoragePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ProcessingError).HasMaxLength(1000);
            entity.HasIndex(e => e.TenantId);
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Embedding).HasColumnType($"vector({DocumentChunk.EmbeddingDimensions})");
            entity.HasOne(e => e.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex }).IsUnique();
            entity.HasIndex(e => e.TenantId);

            // HNSW index for fast cosine-similarity search
            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");

            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TenantApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.EncryptedKey).IsRequired();
            entity.Property(e => e.KeyHint).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Model).HasMaxLength(200);
            entity.HasIndex(e => new { e.TenantId, e.Provider }).IsUnique();
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TenantSettings>(entity =>
        {
            entity.HasKey(e => e.TenantId);
            entity.Property(e => e.ChatProvider).HasConversion<string>().HasMaxLength(20);
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });
    }
}
