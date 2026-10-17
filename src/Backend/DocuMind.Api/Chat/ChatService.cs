using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Chat;

public abstract record ChatStreamEvent;
public record SourcesStreamEvent(IReadOnlyList<ChatSource> Sources) : ChatStreamEvent;
public record BlockStartStreamEvent : ChatStreamEvent;
public record TextStreamEvent(string Text) : ChatStreamEvent;
public record CitationStreamEvent(int SourceIndex, string CitedText) : ChatStreamEvent;
public record DoneStreamEvent(string? StopReason) : ChatStreamEvent;

// Answers questions from the tenant's documents: retrieve chunks -> send them to Claude as
// search results with citations enabled -> stream the answer and its citations back.
public class ChatService(
    AnthropicClient client,
    RetrievalService retrieval,
    IOptions<ClaudeOptions> options)
{
    private const string SystemPrompt = """
        You are DocuMind, an assistant that answers questions about an organization's own documents
        (contracts, compliance records, technical manuals).

        Answer using only the search results provided with the user's latest message. Cite the
        search results that support each claim. If the search results don't contain the answer, say
        that the documents don't cover it rather than answering from general knowledge. When the
        results conflict, point out the conflict and cite both sides.

        Be concise and direct. Use short paragraphs or bullet lists where they help.
        """;

    private const string NoResultsAnswer =
        "I couldn't find anything relevant in your processed documents. Try rephrasing the question, " +
        "or check that the documents you expect have finished processing.";

    private readonly ClaudeOptions _options = options.Value;

    public async IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var question = request.Messages[^1].Content;
        var chunks = await retrieval.RetrieveAsync(
            question, request.DocumentIds, _options.RetrievalLimit, cancellationToken);

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

        string? stopReason = null;
        await foreach (var streamEvent in client.Beta.Messages.CreateStreaming(BuildParams(request, chunks), cancellationToken))
        {
            // Each text block is one span of the answer; citations in it support that span
            if (streamEvent.TryPickContentBlockStart(out var blockStart) && blockStart.ContentBlock.TryPickBetaText(out _))
            {
                yield return new BlockStartStreamEvent();
            }
            else if (streamEvent.TryPickContentBlockDelta(out var blockDelta))
            {
                if (blockDelta.Delta.TryPickText(out var text))
                {
                    yield return new TextStreamEvent(text.Text);
                }
                else if (blockDelta.Delta.TryPickCitations(out var citationDelta)
                    && citationDelta.Citation.TryPickBetaCitationSearchResultLocation(out var location))
                {
                    yield return new CitationStreamEvent((int)location.SearchResultIndex, location.CitedText);
                }
            }
            else if (streamEvent.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } reason)
            {
                stopReason = reason.Raw();
            }
        }

        yield return new DoneStreamEvent(stopReason);
    }

    private MessageCreateParams BuildParams(ChatRequest request, IReadOnlyList<RetrievedChunk> chunks)
    {
        // Earlier turns are sent as plain text; only the latest question carries search results
        var messages = request.Messages[..^1]
            .Select(turn => new BetaMessageParam
            {
                Role = turn.Role == "assistant" ? Role.Assistant : Role.User,
                Content = turn.Content,
            })
            .ToList();

        List<BetaContentBlockParam> latest = [];
        foreach (var chunk in chunks)
        {
            latest.Add(new BetaSearchResultBlockParam
            {
                Source = $"documind://documents/{chunk.DocumentId}" + (chunk.PageNumber is { } p ? $"#page={p}" : ""),
                Title = chunk.PageNumber is { } page ? $"{chunk.DocumentTitle} (page {page})" : chunk.DocumentTitle,
                Content = [new BetaTextBlockParam { Text = chunk.Content }],
                Citations = new BetaCitationsConfigParam { Enabled = true },
            });
        }
        latest.Add(new BetaTextBlockParam { Text = request.Messages[^1].Content });
        messages.Add(new BetaMessageParam { Role = Role.User, Content = latest });

        return new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = SystemPrompt,
            OutputConfig = new BetaOutputConfig { Effort = _options.Effort },
            // If a safety classifier declines the request, re-run it on Anthropic's recommended fallback model
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            Messages = messages,
        };
    }

    private static string Excerpt(string content) =>
        content.Length <= 300 ? content : content[..300].TrimEnd() + "…";
}
