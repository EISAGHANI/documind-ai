using DocuMind.Api.ApiKeys;
using DocuMind.Api.Chat;
using DocuMind.Api.Contracts;
using DocuMind.Api.Embeddings;
using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Controllers;

// Bring-your-own-key settings for the current tenant. Keys are write-only: responses only ever
// include a short hint (last 4 characters). Any member can see the status; only Admins can change it.
[ApiController]
[Route("api/settings/ai")]
public class AiSettingsController(
    ApiKeyStore keys,
    ApiKeyValidator validator,
    ITenantProvider tenantProvider,
    IOptions<ClaudeOptions> claudeOptions,
    IOptions<VoyageOptions> voyageOptions) : ControllerBase
{
    private Guid TenantId => tenantProvider.TenantId;

    [HttpGet]
    public async Task<ActionResult<AiSettingsResponse>> Get(CancellationToken cancellationToken)
    {
        var saved = (await keys.ListAsync(TenantId, cancellationToken)).ToDictionary(k => k.Provider);
        var settings = await keys.GetSettingsAsync(TenantId, cancellationToken);

        var providers = Enum.GetValues<AiProvider>()
            .Select(p => ToStatus(p, saved.GetValueOrDefault(p)))
            .ToList();

        return new AiSettingsResponse(
            settings.ChatProvider is { } chat ? ProviderNames.ToRouteValue(chat) : null,
            providers);
    }

    // Verifies the key with the provider, then stores it encrypted
    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPut("keys/{provider}")]
    public async Task<ActionResult<SaveApiKeyResponse>> SaveKey(
        string provider, SaveApiKeyRequest request, CancellationToken cancellationToken)
    {
        if (!ProviderNames.TryParse(provider, out var aiProvider))
        {
            return NotFound();
        }

        var apiKey = request.ApiKey.Trim();
        var check = await validator.CheckAsync(aiProvider, apiKey, cancellationToken);
        if (!check.Valid)
        {
            return Problem(check.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        // Keep the previously chosen model if the new key can still use it
        var existing = await keys.GetEntryAsync(TenantId, aiProvider, cancellationToken);
        var model = existing?.Model is { } previous && check.Models.Contains(previous) ? previous : DefaultModel(aiProvider, check.Models);

        var entry = await keys.SaveKeyAsync(TenantId, aiProvider, apiKey, model, cancellationToken);

        // The first chat key saved becomes the provider that answers questions
        var settings = await keys.GetSettingsAsync(TenantId, cancellationToken);
        if (ProviderNames.IsChat(aiProvider) && settings.ChatProvider is null && model is not null)
        {
            await keys.SetChatProviderAsync(TenantId, aiProvider, cancellationToken);
        }

        return new SaveApiKeyResponse(ToStatus(aiProvider, entry), check.Models);
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpGet("keys/{provider}/models")]
    public async Task<ActionResult<ModelListResponse>> ListModels(string provider, CancellationToken cancellationToken)
    {
        if (!ProviderNames.TryParse(provider, out var aiProvider) || !ProviderNames.IsChat(aiProvider))
        {
            return NotFound();
        }

        var apiKey = await keys.GetKeyAsync(TenantId, aiProvider, cancellationToken);
        if (apiKey is null)
        {
            return Problem("Save an API key for this provider first.", statusCode: StatusCodes.Status409Conflict);
        }

        var check = await validator.CheckAsync(aiProvider, apiKey, cancellationToken);
        return check.Valid
            ? new ModelListResponse(check.Models)
            : Problem(check.Error, statusCode: StatusCodes.Status400BadRequest);
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPut("keys/{provider}/model")]
    public async Task<ActionResult<AiProviderStatus>> SetModel(
        string provider, SetModelRequest request, CancellationToken cancellationToken)
    {
        if (!ProviderNames.TryParse(provider, out var aiProvider))
        {
            return NotFound();
        }
        if (!ProviderNames.IsChat(aiProvider))
        {
            return Problem("The embeddings model is fixed for all workspaces.", statusCode: StatusCodes.Status400BadRequest);
        }

        var entry = await keys.GetEntryAsync(TenantId, aiProvider, cancellationToken);
        if (entry is null)
        {
            return Problem("Save an API key for this provider first.", statusCode: StatusCodes.Status409Conflict);
        }

        // Only accept models the key can actually use, which also catches typos
        var check = await validator.CheckAsync(aiProvider, keys.Decrypt(entry), cancellationToken);
        if (!check.Valid)
        {
            return Problem(check.Error, statusCode: StatusCodes.Status400BadRequest);
        }
        if (!check.Models.Contains(request.Model))
        {
            return Problem($"This key can't use the model \"{request.Model}\".", statusCode: StatusCodes.Status400BadRequest);
        }

        await keys.SetModelAsync(entry, request.Model, cancellationToken);

        var settings = await keys.GetSettingsAsync(TenantId, cancellationToken);
        if (settings.ChatProvider is null)
        {
            await keys.SetChatProviderAsync(TenantId, aiProvider, cancellationToken);
        }

        return ToStatus(aiProvider, entry);
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpPut("chat-provider")]
    public async Task<IActionResult> SetChatProvider(SetChatProviderRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider is null)
        {
            await keys.SetChatProviderAsync(TenantId, null, cancellationToken);
            return NoContent();
        }

        if (!ProviderNames.TryParse(request.Provider, out var aiProvider) || !ProviderNames.IsChat(aiProvider))
        {
            return Problem("Choose Claude, OpenAI or Gemini.", statusCode: StatusCodes.Status400BadRequest);
        }

        var entry = await keys.GetEntryAsync(TenantId, aiProvider, cancellationToken);
        if (entry is null)
        {
            return Problem($"Save an API key for {ProviderNames.Display(aiProvider)} first.", statusCode: StatusCodes.Status409Conflict);
        }
        if (entry.Model is null && aiProvider != AiProvider.Anthropic)
        {
            return Problem($"Choose a model for {ProviderNames.Display(aiProvider)} first.", statusCode: StatusCodes.Status409Conflict);
        }

        await keys.SetChatProviderAsync(TenantId, aiProvider, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = TenantClaims.AdminPolicy)]
    [HttpDelete("keys/{provider}")]
    public async Task<IActionResult> DeleteKey(string provider, CancellationToken cancellationToken)
    {
        if (!ProviderNames.TryParse(provider, out var aiProvider))
        {
            return NotFound();
        }

        await keys.DeleteAsync(TenantId, aiProvider, cancellationToken);
        return NoContent();
    }

    private string? DefaultModel(AiProvider provider, IReadOnlyList<string> models) => provider switch
    {
        // Prefer the platform's recommended Claude model when the key has access to it
        AiProvider.Anthropic => models.Contains(claudeOptions.Value.Model) || models.Count == 0
            ? claudeOptions.Value.Model
            : models[0],
        // Model naming changes often for other providers, so the user picks one explicitly
        _ => null,
    };

    private AiProviderStatus ToStatus(AiProvider provider, TenantApiKey? entry) => new(
        ProviderNames.ToRouteValue(provider),
        ProviderNames.Display(provider),
        ProviderNames.IsChat(provider) ? "chat" : "embeddings",
        entry is not null,
        entry?.KeyHint,
        provider == AiProvider.Voyage ? voyageOptions.Value.Model : entry?.Model,
        entry?.UpdatedAt);
}
