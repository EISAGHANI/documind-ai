namespace DocuMind.Api.Entities;

public enum AiProvider
{
    Anthropic,
    OpenAI,
    Gemini,
    Voyage
}

// An AI provider API key supplied by a tenant ("bring your own key").
// The key is encrypted with ASP.NET Core Data Protection and never returned to clients.
public class TenantApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public AiProvider Provider { get; set; }
    public string EncryptedKey { get; set; } = string.Empty;

    // Last characters of the key so users can tell which key is saved, e.g. "…a1b2"
    public string KeyHint { get; set; } = string.Empty;

    // Chat model to use with this key; null until chosen (embeddings use a fixed platform model)
    public string? Model { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TenantSettings
{
    public Guid TenantId { get; set; }

    // Which provider answers questions; must have a saved key
    public AiProvider? ChatProvider { get; set; }
}
