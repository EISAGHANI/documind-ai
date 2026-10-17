using DocuMind.Api.Entities;

namespace DocuMind.Api.ApiKeys;

public static class ProviderNames
{
    public static readonly AiProvider[] ChatProviders = [AiProvider.Anthropic, AiProvider.OpenAI, AiProvider.Gemini];

    public static bool IsChat(AiProvider provider) => provider != AiProvider.Voyage;

    public static string Display(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic => "Claude",
        AiProvider.OpenAI => "OpenAI",
        AiProvider.Gemini => "Gemini",
        AiProvider.Voyage => "Voyage AI",
        _ => provider.ToString(),
    };

    // Route values are lowercase provider names: anthropic, openai, gemini, voyage
    public static bool TryParse(string value, out AiProvider provider) =>
        Enum.TryParse(value, ignoreCase: true, out provider) && Enum.IsDefined(provider);

    public static string ToRouteValue(AiProvider provider) => provider.ToString().ToLowerInvariant();
}
