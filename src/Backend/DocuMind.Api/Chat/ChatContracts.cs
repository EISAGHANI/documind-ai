using System.ComponentModel.DataAnnotations;

namespace DocuMind.Api.Chat;

public record ChatTurn(
    [Required, RegularExpression("^(user|assistant)$")] string Role,
    [Required, MaxLength(20_000)] string Content);

public record ChatRequest(
    [Required, MinLength(1), MaxLength(50)] List<ChatTurn> Messages,
    List<Guid>? DocumentIds);

// Payloads of the server-sent events streamed by POST /api/chat
public record ChatSource(int Index, Guid DocumentId, string DocumentTitle, int? PageNumber, string Excerpt);

public record ChatTextEvent(string Text);

public record ChatCitationEvent(int SourceIndex, string CitedText);

public record ChatDoneEvent(string? StopReason);

public record ChatErrorEvent(string Message);
