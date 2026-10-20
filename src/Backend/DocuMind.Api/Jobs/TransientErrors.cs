using System.Net;

namespace DocuMind.Api.Jobs;

public static class TransientErrors
{
    // Network blips, timeouts, throttling and provider outages are worth retrying; bad keys, bad files and bad requests aren't
    public static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: var code } =>
            code is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)code! >= 500,
        TimeoutException or TaskCanceledException => true,
        System.ClientModel.ClientResultException { Status: 429 or >= 500 } => true,
        Polly.CircuitBreaker.BrokenCircuitException => true,
        Polly.Timeout.TimeoutRejectedException => true,
        Resilience.AiBusyException => true,
        _ => ex.InnerException is not null && IsTransient(ex.InnerException),
    };
}
