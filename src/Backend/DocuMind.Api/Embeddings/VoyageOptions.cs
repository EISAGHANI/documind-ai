namespace DocuMind.Api.Embeddings;

// Platform-wide Voyage settings. The API key itself comes from each tenant's Settings.
public class VoyageOptions
{
    public const string SectionName = "Voyage";

    public string BaseUrl { get; set; } = "https://api.voyageai.com/v1/";

    // Fixed for every tenant: changing it requires a migration (vector size) and reprocessing all documents
    public string Model { get; set; } = "voyage-4";

    // Voyage allows 1,000 inputs and 320K tokens per request for voyage-4; 128 chunks stays well under both
    public int BatchSize { get; set; } = 128;
}
