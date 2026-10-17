using System.Security.Cryptography;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.ApiKeys;

// Thrown when a feature needs an API key or setting the tenant hasn't provided yet.
// The message is shown to the user as-is.
public class MissingApiKeyException(string message) : InvalidOperationException(message);

// Stores tenants' API keys encrypted at rest. All methods take an explicit tenant id and bypass
// the tenant query filter, so they also work from the background worker.
public class ApiKeyStore(ApplicationDbContext db, IDataProtectionProvider dataProtection)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("DocuMind.TenantApiKeys.v1");

    public Task<List<TenantApiKey>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.TenantApiKeys.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public Task<TenantApiKey?> GetEntryAsync(Guid tenantId, AiProvider provider, CancellationToken cancellationToken) =>
        db.TenantApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Provider == provider, cancellationToken);

    public async Task<string?> GetKeyAsync(Guid tenantId, AiProvider provider, CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(tenantId, provider, cancellationToken);
        return entry is null ? null : Decrypt(entry);
    }

    public string Decrypt(TenantApiKey entry)
    {
        try
        {
            return _protector.Unprotect(entry.EncryptedKey);
        }
        catch (CryptographicException)
        {
            // The encryption keys were lost or rotated away (e.g. a new server without the key ring)
            throw new MissingApiKeyException(
                $"The saved {ProviderNames.Display(entry.Provider)} key can no longer be read. Please enter it again in Settings.");
        }
    }

    public async Task<TenantApiKey> SaveKeyAsync(
        Guid tenantId, AiProvider provider, string apiKey, string? model, CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(tenantId, provider, cancellationToken);
        if (entry is null)
        {
            entry = new TenantApiKey { TenantId = tenantId, Provider = provider };
            db.TenantApiKeys.Add(entry);
        }

        entry.EncryptedKey = _protector.Protect(apiKey);
        entry.KeyHint = Hint(apiKey);
        entry.Model = model;
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task SetModelAsync(TenantApiKey entry, string model, CancellationToken cancellationToken)
    {
        entry.Model = model;
        entry.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, AiProvider provider, CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(tenantId, provider, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        db.TenantApiKeys.Remove(entry);

        // Stop using a provider for chat once its key is gone
        var settings = await FindSettingsAsync(tenantId, cancellationToken);
        if (settings?.ChatProvider == provider)
        {
            settings.ChatProvider = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TenantSettings> GetSettingsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await FindSettingsAsync(tenantId, cancellationToken) ?? new TenantSettings { TenantId = tenantId };

    public async Task SetChatProviderAsync(Guid tenantId, AiProvider? provider, CancellationToken cancellationToken)
    {
        var settings = await FindSettingsAsync(tenantId, cancellationToken);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = tenantId };
            db.TenantSettings.Add(settings);
        }

        settings.ChatProvider = provider;
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<TenantSettings?> FindSettingsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.TenantSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

    private static string Hint(string apiKey) => "…" + apiKey[^Math.Min(4, apiKey.Length)..];
}
