using DocuMind.Api.Jobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace DocuMind.Api.Observability;

public static class ObservabilityExtensions
{
    // Exporting is off unless an OTLP endpoint is configured (OpenTelemetry:OtlpEndpoint or OTEL_EXPORTER_OTLP_ENDPOINT)
    private static string? OtlpEndpoint(IConfiguration configuration) =>
        configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 } endpoint
            ? endpoint
            : Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

    public static WebApplicationBuilder AddDocuMindObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var otlp = OtlpEndpoint(builder.Configuration);
        var role = builder.Configuration["Jobs:Mode"] ?? "All";

        builder.Host.UseSerilog((context, services, logger) =>
        {
            logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .Enrich.WithProperty("Mode", role);

            // One structured JSON line per event in containers; readable text on a developer machine
            if (context.HostingEnvironment.IsDevelopment())
            {
                logger.WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }

            if (otlp is not null)
            {
                logger.WriteTo.OpenTelemetry(o =>
                {
                    o.Endpoint = otlp;
                    o.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
                });
            }
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o =>
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                 .AddHttpClientInstrumentation()
                 .AddSource(JobTelemetry.SourceName, "Npgsql");
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation()
                 .AddMeter(JobTelemetry.SourceName, "Npgsql", "Microsoft.AspNetCore.RateLimiting");
            });

        if (otlp is not null)
        {
            telemetry.UseOtlpExporter(OpenTelemetry.Exporter.OtlpExportProtocol.Grpc, new Uri(otlp));
        }

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<Data.ApplicationDbContext>("database", tags: ["ready"]);

        return builder;
    }

    // /health/live: the process is up. /health/ready: it can reach what it needs (the database).
    // Both bypass authentication and rate limiting so orchestrators can probe them.
    public static WebApplication MapDocuMindHealth(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false })
            .AllowAnonymous().DisableRateLimiting();
        app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") })
            .AllowAnonymous().DisableRateLimiting();
        return app;
    }
}
