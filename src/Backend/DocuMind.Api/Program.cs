using DocuMind.Api.Data;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Register DbContext with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));


builder.Services.AddControllers();

// 2. Register native OpenAPI services (No Swashbuckle required!)
builder.Services.AddOpenApi();

var app = builder.Build();

// 3. Enable OpenAPI endpoint and Scalar UI in Development
if (app.Environment.IsDevelopment())
{
    // Generates the /openapi/v1.json document
    app.MapOpenApi();

    // Renders the modern API Reference UI at /scalar/v1
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();