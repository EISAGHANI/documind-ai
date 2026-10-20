namespace DocuMind.Api.Entities;

// A persisted chat thread. Private to the user who started it (within their tenant).
public class ChatConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = "New conversation";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessageEntity> Messages { get; set; } = [];
}

public class ChatMessageEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;
    public Guid TenantId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    // JSON array of the sources shown with an assistant answer
    public string? SourcesJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Append-only record of security-relevant actions (who did what, when, from where)
public class ActivityLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
