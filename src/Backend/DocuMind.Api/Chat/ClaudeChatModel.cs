using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace DocuMind.Api.Chat;

// Claude with native citations: chunks are sent as search_result blocks and Claude's
// response carries citations that point at the exact chunk it used.
public class ClaudeChatModel(AnthropicClient client, string model, ClaudeOptions options) : IChatModel
{
    public string Provider => "anthropic";
    public string Model => model;

    // Structured outputs: the response is constrained to the JSON schema
    public async Task<string> GenerateJsonAsync(StructuredRequest request, CancellationToken cancellationToken)
    {
        var response = await client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = request.MaxTokens,
            System = request.Instructions,
            OutputConfig = new BetaOutputConfig
            {
                Effort = request.Effort,
                Format = new BetaJsonOutputFormat { Schema = request.Schema.ToDictionary(p => p.Key, p => p.Value) },
            },
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            Messages = [new BetaMessageParam { Role = Role.User, Content = request.Input }],
        }, cancellationToken);

        var stopReason = response.StopReason?.Raw();
        if (stopReason == "refusal")
        {
            throw new ModelOutputException("Claude declined to review this document.");
        }
        if (stopReason == "max_tokens")
        {
            throw new ModelOutputException("The review was cut off because it was too long. Try a checklist with fewer checks.");
        }

        return string.Concat(response.Content.Select(b => b.TryPickText(out var text) ? text.Text : ""));
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        IReadOnlyList<ChatTurn> history,
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? stopReason = null;
        await foreach (var streamEvent in client.Beta.Messages.CreateStreaming(
            BuildParams(history, question, chunks), cancellationToken))
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

    private MessageCreateParams BuildParams(
        IReadOnlyList<ChatTurn> history, string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        // Earlier turns are sent as plain text; only the latest question carries search results
        var messages = history
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
                Title = ChatPrompts.SourceTitle(chunk),
                Content = [new BetaTextBlockParam { Text = chunk.Content }],
                Citations = new BetaCitationsConfigParam { Enabled = true },
            });
        }
        latest.Add(new BetaTextBlockParam { Text = question });
        messages.Add(new BetaMessageParam { Role = Role.User, Content = latest });

        return new MessageCreateParams
        {
            Model = model,
            MaxTokens = options.MaxTokens,
            System = ChatPrompts.Instructions,
            OutputConfig = new BetaOutputConfig { Effort = options.Effort },
            // If a safety classifier declines the request, re-run it on Anthropic's recommended fallback model
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            Messages = messages,
        };
    }
}
