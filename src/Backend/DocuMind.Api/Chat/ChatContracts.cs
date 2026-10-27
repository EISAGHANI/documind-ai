using System.ComponentModel.DataAnnotations;

namespace DocuMind.Api.Chat;

public record ChatTurn(
    [Required, RegularExpression("^(user|assistant)$")] string Role,
    [Required, MaxLength(20_000)] string Content);

public record ChatRequest(
    [Required, MinLength(1), MaxLength(50)] List<ChatTurn> Messages,
    List<Guid>? DocumentIds,
    // Continue a persisted conversation; its stored history is then used instead of Messages[..^1]
    Guid? ConversationId = null);

public record ConversationSummary(Guid Id, string Title, DateTime UpdatedAt);

public record ConversationMessage(Guid Id, string Role, string Content, IReadOnlyList<ChatSource>? Sources, DateTime CreatedAt);

public record ConversationDetail(Guid Id, string Title, IReadOnlyList<ConversationMessage> Messages);

public record ChatConversationEvent(Guid ConversationId);

// Payloads of the server-sent events streamed by POST /api/chat
public record ChatSource(int Index, Guid DocumentId, string DocumentTitle, int? PageNumber, string Excerpt);

public record ChatTextEvent(string Text);

public record ChatCitationEvent(int SourceIndex, string CitedText);

public record ChatDoneEvent(string? StopReason);

public record ChatErrorEvent(string Message);
