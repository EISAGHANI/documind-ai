using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Chat;

// Answers questions from the tenant's documents: retrieve chunks -> hand them to the tenant's
// chosen chat model -> stream the answer and its citations back. Every exchange is persisted
// as a conversation, and the stored history feeds both retrieval and the model's context.
public class ChatService(
    ChatModelFactory chatModels,
    RetrievalService retrieval,
    ITenantProvider tenantProvider,
    ApplicationDbContext db,
    IHttpContextAccessor httpContext,
    IOptions<ClaudeOptions> options)
{
    private const int HistoryMessageLimit = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string NoResultsAnswer =
        "I couldn't find anything relevant in your processed documents. Try rephrasing the question, " +
        "or check that the documents you expect have finished processing.";

    private Guid CurrentUserId =>
        Guid.TryParse(httpContext.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    public async IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Resolve the model first so a missing key is reported before any work is done
        var model = await chatModels.CreateAsync(tenantProvider.TenantId, cancellationToken);

        var question = request.Messages[^1].Content;
        var (conversation, history) = await LoadOrStartConversationAsync(request, question, cancellationToken);
        yield return new ConversationStreamEvent(conversation.Id);

        db.ChatMessages.Add(new ChatMessageEntity
        {
            ConversationId = conversation.Id, TenantId = conversation.TenantId, Role = "user", Content = question,
        });
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var chunks = await retrieval.RetrieveAsync(
            RetrievalQuery.Build(history, question), request.DocumentIds, options.Value.RetrievalLimit, cancellationToken);

        var sources = chunks.Select((c, i) => new ChatSource(
            i, c.DocumentId, c.DocumentTitle, c.PageNumber, Excerpt(c.Content))).ToList();
        yield return new SourcesStreamEvent(sources);

        var answer = new StringBuilder();

        // Nothing to ground an answer in: don't spend a model call on it
        if (chunks.Count == 0)
        {
            answer.Append(NoResultsAnswer);
            yield return new TextStreamEvent(NoResultsAnswer);
            yield return new DoneStreamEvent("no_results");
        }
        else
        {
            await foreach (var streamEvent in model.StreamAnswerAsync(history, question, chunks, cancellationToken))
            {
                if (streamEvent is TextStreamEvent text)
                {
                    answer.Append(text.Text);
                }

                yield return streamEvent;
            }
        }

        if (answer.Length > 0)
        {
            db.ChatMessages.Add(new ChatMessageEntity
            {
                ConversationId = conversation.Id,
                TenantId = conversation.TenantId,
                Role = "assistant",
                Content = answer.ToString(),
                SourcesJson = JsonSerializer.Serialize(sources, Json),
            });
            conversation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<(ChatConversation Conversation, IReadOnlyList<ChatTurn> History)> LoadOrStartConversationAsync(
        ChatRequest request, string question, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;

        if (request.ConversationId is { } id)
        {
            var existing = await db.ChatConversations
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken)
                ?? throw new InvalidOperationException("That conversation no longer exists.");

            // Server-side history is the source of truth for follow-ups
            var stored = await db.ChatMessages
                .Where(m => m.ConversationId == id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(HistoryMessageLimit)
                .ToListAsync(cancellationToken);
            stored.Reverse();
            return (existing, stored.Select(m => new ChatTurn(m.Role, m.Content)).ToList());
        }

        var conversation = new ChatConversation
        {
            TenantId = tenantProvider.TenantId,
            UserId = userId,
            Title = Title(question),
        };
        db.ChatConversations.Add(conversation);
        return (conversation, request.Messages[..^1]);
    }

    private static string Title(string question)
    {
        var single = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return single.Length <= 80 ? single : single[..80].TrimEnd() + "…";
    }

    private static string Excerpt(string content) =>
        content.Length <= 300 ? content : content[..300].TrimEnd() + "…";
}
