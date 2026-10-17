using DocuMind.Api.Data;
using DocuMind.Api.Storage;
using DocuMind.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
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

// 3. Allow the Next.js frontend to call the API
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// 4. Register native OpenAPI services (No Swashbuckle required!)
builder.Services.AddOpenApi();

var app = builder.Build();

// 5. Enable OpenAPI endpoint and Scalar UI in Development
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
