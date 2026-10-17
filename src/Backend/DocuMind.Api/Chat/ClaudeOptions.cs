namespace DocuMind.Api.Chat;

// Platform-wide Claude settings. The API key itself comes from each tenant's Settings.
public class ClaudeOptions
{
    public const string SectionName = "Anthropic";

    // Used when a tenant saves a Claude key without picking a model
    public string Model { get; set; } = "claude-opus-5-5";

    // Claude Opus 5.5 defaults to "medium"; set explicitly so behaviour doesn't drift with model defaults
    public string Effort { get; set; } = "medium";
    public int MaxTokens { get; set; } = 64000;

    // How many chunks to retrieve per question (applies to every provider)
    public int RetrievalLimit { get; set; } = 8;
}
