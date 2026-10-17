using DocuMind.Api.ApiKeys;
using DocuMind.Api.Audits;
using DocuMind.Api.Chat;
using DocuMind.Api.Data;
using DocuMind.Api.Embeddings;
using DocuMind.Api.Entities;
using DocuMind.Api.Processing;
using DocuMind.Api.Storage;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "Frontend";

// 1. Register DbContext with PostgreSQL + pgvector
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

// 2. Sign-in with ASP.NET Core Identity. Tokens are Identity's bearer tokens (protected with Data
// Protection, see section 5); the company and role are claims added by TenantClaimsPrincipalFactory.
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        // Length over composition rules, in line with current NIST guidance (SP 800-63B)
        options.Password.RequiredLength = 10;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddClaimsPrincipalFactory<TenantClaimsPrincipalFactory>();
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromMinutes(30);
    options.RefreshTokenExpiration = TimeSpan.FromDays(14);
});
builder.Services.AddAuthorizationBuilder()
    // Every endpoint needs a signed-in user unless it opts out with [AllowAnonymous]
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(TenantClaims.TenantId).Build())
    .AddPolicy(TenantClaims.AdminPolicy, policy => policy.RequireClaim(TenantClaims.Role, nameof(TenantRole.Admin)));
builder.Services.AddMemoryCache();

// 3. Tenancy and file storage
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, ClaimsTenantProvider>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

// 4. Document processing: Voyage embeddings + background worker
builder.Services.Configure<VoyageOptions>(builder.Configuration.GetSection(VoyageOptions.SectionName));
builder.Services.AddHttpClient<IEmbeddingService, VoyageEmbeddingService>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<VoyageOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler(resilience =>
    {
        // Retries 429/5xx with exponential backoff and jitter, as Voyage recommends.
        // Large embedding batches can take a while, so allow more than the 10s default per attempt.
        resilience.Retry.MaxRetryAttempts = 5;
        resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
        resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
    });
builder.Services.AddSingleton<IDocumentProcessingQueue, DocumentProcessingQueue>();
builder.Services.AddScoped<DocumentProcessor>();
builder.Services.AddHostedService<DocumentProcessingWorker>();

// 5. Bring-your-own-key: tenants' AI provider keys, encrypted with Data Protection.
// Set DataProtection:KeysPath in production so the encryption keys survive restarts and are shared
// between instances; without it saved API keys become unreadable when the key ring is lost.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("DocuMind");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}
builder.Services.Configure<ProviderEndpointsOptions>(builder.Configuration.GetSection(ProviderEndpointsOptions.SectionName));
builder.Services.AddSingleton<AiClientCache>();
builder.Services.AddScoped<ApiKeyStore>();
builder.Services.AddScoped<ApiKeyValidator>();

// 6. Question answering over documents with the tenant's chosen chat provider
builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection(ClaudeOptions.SectionName));
builder.Services.AddScoped<ChatModelFactory>();
builder.Services.AddScoped<RetrievalService>();
builder.Services.AddScoped<ChatService>();

// Compliance audits: checklists evaluated in the background with the tenant's chosen model
builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));
builder.Services.AddSingleton<AuditQueue>();
builder.Services.AddSingleton<IAuditQueue>(services => services.GetRequiredService<AuditQueue>());
builder.Services.AddScoped<AuditRunner>();
builder.Services.AddHostedService<AuditWorker>();

// 7. Allow the Next.js frontend to call the API
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// 8. Register native OpenAPI services (No Swashbuckle required!)
builder.Services.AddOpenApi();

var app = builder.Build();

// 9. Enable OpenAPI endpoint and Scalar UI in Development
if (app.Environment.IsDevelopment())
{
    // Generates the /openapi/v1.json document
    app.MapOpenApi().AllowAnonymous();

    // Renders the modern API Reference UI at /scalar/v1
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseMiddleware<MembershipValidationMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
