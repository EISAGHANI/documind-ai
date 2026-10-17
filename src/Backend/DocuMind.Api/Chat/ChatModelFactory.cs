using DocuMind.Api.ApiKeys;
using DocuMind.Api.Entities;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Chat;

// Builds the chat model for a tenant from the provider, key and model chosen in Settings
public class ChatModelFactory(ApiKeyStore keys, AiClientCache clients, IOptions<ClaudeOptions> claudeOptions)
{
    public async Task<IChatModel> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await keys.GetSettingsAsync(tenantId, cancellationToken);
        var provider = settings.ChatProvider
            ?? throw new MissingApiKeyException("Choose which AI provider answers questions in Settings.");

        var entry = await keys.GetEntryAsync(tenantId, provider, cancellationToken)
            ?? throw new MissingApiKeyException($"Add an API key for {ProviderNames.Display(provider)} in Settings.");
        var apiKey = keys.Decrypt(entry);

        var model = entry.Model
            ?? (provider == AiProvider.Anthropic ? claudeOptions.Value.Model : null)
            ?? throw new MissingApiKeyException($"Choose a model for {ProviderNames.Display(provider)} in Settings.");

        return provider switch
        {
            AiProvider.Anthropic => new ClaudeChatModel(clients.GetAnthropicClient(apiKey), model, claudeOptions.Value),
            AiProvider.OpenAI or AiProvider.Gemini => new OpenAICompatibleChatModel(clients.GetChatClient(provider, apiKey, model)),
            _ => throw new MissingApiKeyException($"{ProviderNames.Display(provider)} can't be used for answering questions."),
        };
    }
}
