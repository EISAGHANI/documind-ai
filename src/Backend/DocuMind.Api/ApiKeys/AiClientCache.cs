using System.ClientModel;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Anthropic;
using DocuMind.Api.Entities;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;

namespace DocuMind.Api.ApiKeys;

// Reuses SDK clients per API key instead of creating one (and its HTTP connections) per request.
// Clients are keyed by a hash of the API key so the plaintext key isn't used as a dictionary key.
public class AiClientCache(IOptions<ProviderEndpointsOptions> endpoints)
{
    private readonly ConcurrentDictionary<string, object> _clients = new();
    private readonly ProviderEndpointsOptions _endpoints = endpoints.Value;

    public AnthropicClient GetAnthropicClient(string apiKey) =>
        (AnthropicClient)_clients.GetOrAdd($"anthropic:{Hash(apiKey)}", _ => new AnthropicClient { ApiKey = apiKey });

    public ChatClient GetChatClient(AiProvider provider, string apiKey, string model) =>
        (ChatClient)_clients.GetOrAdd(
            $"{provider}:chat:{model}:{Hash(apiKey)}",
            _ => new ChatClient(model, new ApiKeyCredential(apiKey), OpenAIOptions(provider)));

    public OpenAIModelClient GetModelClient(AiProvider provider, string apiKey) =>
        (OpenAIModelClient)_clients.GetOrAdd(
            $"{provider}:models:{Hash(apiKey)}",
            _ => new OpenAIModelClient(new ApiKeyCredential(apiKey), OpenAIOptions(provider)));

    private OpenAIClientOptions OpenAIOptions(AiProvider provider) => new()
    {
        Endpoint = new Uri(provider == AiProvider.Gemini ? _endpoints.GeminiBaseUrl : _endpoints.OpenAIBaseUrl),
    };

    private static string Hash(string apiKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
