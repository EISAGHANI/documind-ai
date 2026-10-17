using System.ComponentModel.DataAnnotations;

namespace DocuMind.Api.Contracts;

// Purpose is "chat" (answers questions) or "embeddings" (indexes documents and questions)
public record AiProviderStatus(
    string Provider,
    string DisplayName,
    string Purpose,
    bool Configured,
    string? KeyHint,
    string? Model,
    DateTime? UpdatedAt);

public record AiSettingsResponse(string? ChatProvider, IReadOnlyList<AiProviderStatus> Providers);

public record SaveApiKeyRequest([Required, MinLength(8), MaxLength(500)] string ApiKey);

public record SaveApiKeyResponse(AiProviderStatus Status, IReadOnlyList<string> Models);

public record ModelListResponse(IReadOnlyList<string> Models);

public record SetModelRequest([Required, MaxLength(200)] string Model);

public record SetChatProviderRequest(string? Provider);
