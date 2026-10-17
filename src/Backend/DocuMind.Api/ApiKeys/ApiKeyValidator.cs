using System.ClientModel;
using System.Net;
using Anthropic.Exceptions;
using DocuMind.Api.Embeddings;
using DocuMind.Api.Entities;

namespace DocuMind.Api.ApiKeys;

public record KeyCheckResult(bool Valid, string? Error, IReadOnlyList<string> Models)
{
    public static KeyCheckResult Invalid(string error) => new(false, error, []);
}

// Checks a key against its provider with a cheap call before it is saved, and lists the chat models it can use
public class ApiKeyValidator(AiClientCache clients, IEmbeddingService embeddings, ILogger<ApiKeyValidator> logger)
{
    // Model families that can't be used for chat completions
    private static readonly string[] NonChatMarkers =
        ["embedding", "tts", "whisper", "dall-e", "image", "audio", "realtime", "moderation", "transcribe", "search", "aqa", "imagen", "veo"];

    public async Task<KeyCheckResult> CheckAsync(AiProvider provider, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            return provider switch
            {
                AiProvider.Anthropic => await CheckAnthropicAsync(apiKey, cancellationToken),
                AiProvider.OpenAI or AiProvider.Gemini => await CheckOpenAICompatibleAsync(provider, apiKey, cancellationToken),
                AiProvider.Voyage => await CheckVoyageAsync(apiKey, cancellationToken),
                _ => KeyCheckResult.Invalid("Unknown provider."),
            };
        }
        catch (Exception ex) when (IsAuthFailure(ex))
        {
            return KeyCheckResult.Invalid($"{ProviderNames.Display(provider)} rejected this API key.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not verify {Provider} API key", provider);
            return KeyCheckResult.Invalid(
                $"Could not verify the key with {ProviderNames.Display(provider)}. Check the key and try again.");
        }
    }

    private async Task<KeyCheckResult> CheckAnthropicAsync(string apiKey, CancellationToken cancellationToken)
    {
        var page = await clients.GetAnthropicClient(apiKey).Models.List(cancellationToken: cancellationToken);
        return new KeyCheckResult(true, null, page.Items.Select(m => m.ID).ToList());
    }

    private async Task<KeyCheckResult> CheckOpenAICompatibleAsync(
        AiProvider provider, string apiKey, CancellationToken cancellationToken)
    {
        var models = await clients.GetModelClient(provider, apiKey).GetModelsAsync(cancellationToken);

        var chatModels = models.Value
            // Gemini's compatible endpoint returns ids like "models/gemini-…"; chat accepts the bare id
            .Select(m => m.Id.StartsWith("models/", StringComparison.Ordinal) ? m.Id["models/".Length..] : m.Id)
            .Where(id => !NonChatMarkers.Any(marker => id.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Where(id => provider != AiProvider.Gemini || id.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderByDescending(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new KeyCheckResult(true, null, chatModels);
    }

    private async Task<KeyCheckResult> CheckVoyageAsync(string apiKey, CancellationToken cancellationToken)
    {
        // Embedding one word costs a fraction of a cent and proves the key works with the platform model
        await embeddings.EmbedAsync(apiKey, ["ping"], EmbeddingInputType.Query, cancellationToken);
        return new KeyCheckResult(true, null, []);
    }

    private static bool IsAuthFailure(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException or AnthropicForbiddenException => true,
        ClientResultException { Status: 401 or 403 } => true,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => true,
        _ => false,
    };
}
