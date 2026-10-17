namespace DocuMind.Api.Embeddings;

public class VoyageOptions
{
    public const string SectionName = "Voyage";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.voyageai.com/v1/";
    public string Model { get; set; } = "voyage-4";

    // Voyage allows 1,000 inputs and 320K tokens per request for voyage-4; 128 chunks stays well under both
    public int BatchSize { get; set; } = 128;
}
