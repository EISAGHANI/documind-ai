using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.Data;

// Holds the ASP.NET Core Identity tables (users, logins, tokens) alongside the application data
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
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
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<AuditChecklist> AuditChecklists => Set<AuditChecklist>();
    public DbSet<AuditCheck> AuditChecks => Set<AuditCheck>();
    public DbSet<AuditRun> AuditRuns => Set<AuditRun>();
    public DbSet<AuditFinding> AuditFindings => Set<AuditFinding>();

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

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.HasKey(e => new { e.TenantId, e.UserId });
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

            // One company per user for now
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TenantInvitation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Email });
            entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AuditChecklist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasIndex(e => e.TenantId);
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AuditCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Requirement).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(10);
            entity.HasOne(e => e.Checklist).WithMany(c => c.Checks).HasForeignKey(e => e.ChecklistId).OnDelete(DeleteBehavior.Cascade);
            // Scoped through the parent checklist's tenant
            entity.HasQueryFilter(e => e.Checklist.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AuditRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ChecklistName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Error).HasMaxLength(1000);
            entity.Property(e => e.Provider).HasMaxLength(20);
            entity.Property(e => e.Model).HasMaxLength(200);
            entity.OwnsMany(e => e.Checks, checks =>
            {
                checks.ToJson();
                checks.Property(c => c.Severity).HasConversion<string>();
            });
            entity.HasOne(e => e.Document).WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AuditChecklist>().WithMany().HasForeignKey(e => e.ChecklistId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            entity.HasIndex(e => e.DocumentId);
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AuditFinding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CheckTitle).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Requirement).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.Result).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Explanation).IsRequired().HasMaxLength(4000);
            entity.OwnsMany(e => e.Evidence, evidence => evidence.ToJson());
            entity.HasOne(e => e.Run).WithMany(r => r.Findings).HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => e.Run.TenantId == CurrentTenantId);
        });
    }
}
