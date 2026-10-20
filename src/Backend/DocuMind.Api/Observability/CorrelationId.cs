namespace DocuMind.Api.Observability;

// Carries the request's correlation id into jobs, so a request can be followed into the worker
public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";
}

public class HttpCorrelationIdAccessor(IHttpContextAccessor httpContext) : ICorrelationIdAccessor
{
    public string? CorrelationId =>
        httpContext.HttpContext?.Items[Observability.CorrelationId.ItemKey] as string;
}

// Accepts the caller's id (from Next.js) or creates one, echoes it on the response and adds it to log scopes
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[CorrelationId.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            id = Guid.NewGuid().ToString("N");
        }

        context.Items[CorrelationId.ItemKey] = id;
        context.Response.Headers[CorrelationId.HeaderName] = id;
        System.Diagnostics.Activity.Current?.SetTag("correlation_id", id);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }
}
