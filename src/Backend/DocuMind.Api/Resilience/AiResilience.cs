using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DocuMind.Api.Chat;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace DocuMind.Api.Resilience;

public class AiLimitsOptions
{
    public const string SectionName = "AiLimits";

    // Concurrent LLM calls per API/worker instance, and per tenant within it
    public int MaxConcurrentCalls { get; set; } = 16;
    public int MaxConcurrentCallsPerTenant { get; set; } = 3;

    // How long a request may wait for a free slot before being rejected with "busy"
    public int QueueTimeoutSeconds { get; set; } = 15;

    // Time allowed until the model produces its first output, and between streamed chunks
    public int FirstTokenTimeoutSeconds { get; set; } = 60;
    public int StreamIdleTimeoutSeconds { get; set; } = 45;

    // Structured (audit) calls can be long
    public int StructuredCallTimeoutSeconds { get; set; } = 300;

    public int BreakerFailureThreshold { get; set; } = 5;
    public int BreakerBreakSeconds { get; set; } = 30;
}

// Thrown when no AI call slot frees up in time; users see the message and can retry
public class AiBusyException() : InvalidOperationException("The AI service is busy right now. Please try again in a moment.");

// Bounds concurrent AI calls overall and per tenant, so one tenant can't starve the rest
public sealed class AiConcurrencyGate(IOptions<AiLimitsOptions> options)
{
    private readonly AiLimitsOptions _options = options.Value;
    private readonly SemaphoreSlim _global = new(options.Value.MaxConcurrentCalls);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _tenants = new();

    public async Task<IDisposable> AcquireAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = _tenants.GetOrAdd(tenantId, _ => new SemaphoreSlim(_options.MaxConcurrentCallsPerTenant));
        var timeout = TimeSpan.FromSeconds(_options.QueueTimeoutSeconds);

        if (!await tenant.WaitAsync(timeout, cancellationToken))
        {
            throw new AiBusyException();
        }

        if (!await _global.WaitAsync(timeout, cancellationToken))
        {
            tenant.Release();
            throw new AiBusyException();
        }

        return new Lease(_global, tenant);
    }

    private sealed class Lease(SemaphoreSlim global, SemaphoreSlim tenant) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                global.Release();
                tenant.Release();
            }
        }
    }
}

// One circuit breaker per AI provider: when a provider is down, calls fail fast instead of piling up.
// Throttling (429) and rejected keys are per-tenant problems, so they don't count against the provider.
public sealed class AiCircuitBreakers(IOptions<AiLimitsOptions> options)
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline> _pipelines = new();

    public ResiliencePipeline For(string provider) => _pipelines.GetOrAdd(provider, _ =>
        new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = Math.Max(2, options.Value.BreakerFailureThreshold),
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(options.Value.BreakerBreakSeconds),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsProviderOutage),
            })
            .Build());

    internal static bool IsProviderOutage(Exception ex) => ex switch
    {
        TimeoutException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: var code } => (int)code! >= 500,
        System.ClientModel.ClientResultException { Status: >= 500 } => true,
        Anthropic.Exceptions.Anthropic5xxException => true,
        _ => false,
    };
}

// Decorates a tenant's chat model with the concurrency gate, timeouts and circuit breaker
public sealed class ResilientChatModel(
    IChatModel inner,
    Guid tenantId,
    AiConcurrencyGate gate,
    AiCircuitBreakers breakers,
    AiLimitsOptions limits) : IChatModel
{
    public string Provider => inner.Provider;
    public string Model => inner.Model;

    public async IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        IReadOnlyList<ChatTurn> history,
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var lease = await gate.AcquireAsync(tenantId, cancellationToken);
        var breaker = breakers.For(inner.Provider);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var events = inner.StreamAnswerAsync(history, question, chunks, cts.Token).GetAsyncEnumerator(cts.Token);

        var first = true;
        while (true)
        {
            var timeout = TimeSpan.FromSeconds(first ? limits.FirstTokenTimeoutSeconds : limits.StreamIdleTimeoutSeconds);
            cts.CancelAfter(timeout);

            var hasNext = first
                ? await breaker.ExecuteAsync(_ => MoveNextAsync(events, timeout, cancellationToken), cancellationToken)
                : await MoveNextAsync(events, timeout, cancellationToken).AsTask();
            cts.CancelAfter(Timeout.InfiniteTimeSpan);
            if (!hasNext)
            {
                yield break;
            }

            first = false;
            yield return events.Current;
        }
    }

    // Only the first read goes through the breaker: that's where an unavailable provider shows up
    private async ValueTask<bool> MoveNextAsync(IAsyncEnumerator<ChatStreamEvent> events, TimeSpan timeout, CancellationToken callerToken)
    {
        try
        {
            return await events.MoveNextAsync();
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{inner.Provider} did not respond within {timeout.TotalSeconds:0}s.");
        }
    }

    public async Task<string> GenerateJsonAsync(StructuredRequest request, CancellationToken cancellationToken)
    {
        using var lease = await gate.AcquireAsync(tenantId, cancellationToken);
        var timeout = TimeSpan.FromSeconds(limits.StructuredCallTimeoutSeconds);

        return await breakers.For(inner.Provider).ExecuteAsync(async ct =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await inner.GenerateJsonAsync(request, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{inner.Provider} did not respond within {timeout.TotalSeconds:0}s.");
            }
        }, cancellationToken);
    }
}
