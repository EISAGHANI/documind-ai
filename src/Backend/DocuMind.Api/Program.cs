using DocuMind.Api.ApiKeys;
using DocuMind.Api.Chat;
using DocuMind.Api.Data;
using DocuMind.Api.Embeddings;
using DocuMind.Api.Processing;
using DocuMind.Api.Storage;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "Frontend";

// 1. Register DbContext with PostgreSQL + pgvector
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

// 2. Tenancy and file storage
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HeaderTenantProvider>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

// 3. Document processing: Voyage embeddings + background worker
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

// 4. Bring-your-own-key: tenants' AI provider keys, encrypted with Data Protection.
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

// 5. Question answering over documents with the tenant's chosen chat provider
builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection(ClaudeOptions.SectionName));
builder.Services.AddScoped<ChatModelFactory>();
builder.Services.AddScoped<RetrievalService>();
builder.Services.AddScoped<ChatService>();

// 6. Allow the Next.js frontend to call the API
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// 7. Register native OpenAPI services (No Swashbuckle required!)
builder.Services.AddOpenApi();

var app = builder.Build();

// 8. Enable OpenAPI endpoint and Scalar UI in Development
if (app.Environment.IsDevelopment())
{
    // Generates the /openapi/v1.json document
    app.MapOpenApi();

    // Renders the modern API Reference UI at /scalar/v1
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();
