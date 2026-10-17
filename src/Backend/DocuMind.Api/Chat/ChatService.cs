using System.Runtime.CompilerServices;
using DocuMind.Api.Tenancy;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Chat;

// Answers questions from the tenant's documents: retrieve chunks -> hand them to the tenant's
// chosen chat model -> stream the answer and its citations back.
public class ChatService(
    ChatModelFactory chatModels,
    RetrievalService retrieval,
    ITenantProvider tenantProvider,
    IOptions<ClaudeOptions> options)
{
    private const string NoResultsAnswer =
        "I couldn't find anything relevant in your processed documents. Try rephrasing the question, " +
        "or check that the documents you expect have finished processing.";

    public async IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Resolve the model first so a missing key is reported before any work is done
        var model = await chatModels.CreateAsync(tenantProvider.TenantId, cancellationToken);

        var question = request.Messages[^1].Content;
        var chunks = await retrieval.RetrieveAsync(
            question, request.DocumentIds, options.Value.RetrievalLimit, cancellationToken);

        var sources = chunks.Select((c, i) => new ChatSource(
            i, c.DocumentId, c.DocumentTitle, c.PageNumber, Excerpt(c.Content))).ToList();
        yield return new SourcesStreamEvent(sources);

        // Nothing to ground an answer in: don't spend a model call on it
        if (chunks.Count == 0)
        {
            yield return new TextStreamEvent(NoResultsAnswer);
            yield return new DoneStreamEvent("no_results");
            yield break;
        }

        await foreach (var streamEvent in model.StreamAnswerAsync(request.Messages[..^1], question, chunks, cancellationToken))
        {
            yield return streamEvent;
        }
    }

    private static string Excerpt(string content) =>
        content.Length <= 300 ? content : content[..300].TrimEnd() + "…";
}
