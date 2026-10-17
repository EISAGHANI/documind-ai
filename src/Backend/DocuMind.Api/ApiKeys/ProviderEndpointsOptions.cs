namespace DocuMind.Api.ApiKeys;

// Base URLs for the OpenAI-compatible chat providers. Overridable for proxies, Azure-style gateways or tests.
public class ProviderEndpointsOptions
{
    public const string SectionName = "AiProviders";

    public string OpenAIBaseUrl { get; set; } = "https://api.openai.com/v1/";

    // Google's OpenAI-compatible endpoint for Gemini
    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
}
