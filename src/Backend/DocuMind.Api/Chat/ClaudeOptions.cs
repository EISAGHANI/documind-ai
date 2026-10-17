namespace DocuMind.Api.Chat;

public class ClaudeOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-opus-5-5";

    // Claude Opus 5.5 defaults to "medium"; set explicitly so behaviour doesn't drift with model defaults
    public string Effort { get; set; } = "medium";
    public int MaxTokens { get; set; } = 64000;

    // How many chunks to retrieve and send to Claude per question
    public int RetrievalLimit { get; set; } = 8;
}
