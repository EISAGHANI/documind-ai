using System.Net;
using DocuMind.Api.Chat;
using DocuMind.Api.Jobs;
using Xunit;

namespace DocuMind.Api.Tests.Unit;

public class HybridRankerTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact]
    public void Item_ranked_well_by_both_lists_beats_items_that_top_only_one()
    {
        IReadOnlyList<Guid> vector = [B, A, C];
        IReadOnlyList<Guid> text = [D, B, A];

        var fused = HybridRanker.Fuse([vector, text], id => id, limit: 4);

        Assert.Equal(B, fused[0]);
        Assert.Equal(4, fused.Count);
    }

    [Fact]
    public void Duplicates_are_returned_once_and_the_limit_is_respected()
    {
        IReadOnlyList<Guid> first = [A, B, C];
        IReadOnlyList<Guid> second = [A, B, C];

        var fused = HybridRanker.Fuse([first, second], id => id, limit: 2);

        Assert.Equal([A, B], fused);
    }

    [Fact]
    public void Empty_rankings_give_an_empty_result()
    {
        Assert.Empty(HybridRanker.Fuse<Guid>([[], []], id => id, limit: 5));
    }
}

public class JobBackoffTests
{
    [Fact]
    public void Backoff_grows_exponentially_and_is_capped()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), JobWorker.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(10), JobWorker.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(20), JobWorker.Backoff(3));
        Assert.Equal(TimeSpan.FromSeconds(300), JobWorker.Backoff(50));
    }

    [Fact]
    public void Final_attempt_is_detected()
    {
        Assert.False(new JobContext(Guid.NewGuid(), 4, 5).IsFinalAttempt);
        Assert.True(new JobContext(Guid.NewGuid(), 5, 5).IsFinalAttempt);
    }
}

public class TransientErrorTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public void Throttling_and_server_errors_are_transient(HttpStatusCode code) =>
        Assert.True(TransientErrors.IsTransient(new HttpRequestException("x", null, code)));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public void Client_errors_are_permanent(HttpStatusCode code) =>
        Assert.False(TransientErrors.IsTransient(new HttpRequestException("x", null, code)));

    [Fact]
    public void Network_failures_and_timeouts_are_transient()
    {
        Assert.True(TransientErrors.IsTransient(new HttpRequestException("connection reset")));
        Assert.True(TransientErrors.IsTransient(new TimeoutException()));
        Assert.True(TransientErrors.IsTransient(new Polly.CircuitBreaker.BrokenCircuitException()));
    }

    [Fact]
    public void Inner_exceptions_are_inspected()
    {
        var wrapped = new InvalidOperationException("outer", new TimeoutException());

        Assert.True(TransientErrors.IsTransient(wrapped));
        Assert.False(TransientErrors.IsTransient(new InvalidOperationException("plain")));
    }
}
