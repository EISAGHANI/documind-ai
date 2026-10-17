namespace DocuMind.Api.Audits;

public class AuditOptions
{
    public const string SectionName = "Audits";

    // Documents up to this size are reviewed in full; larger ones use the passages most relevant
    // to each check. ~120,000 characters is roughly 30,000 tokens.
    public int FullDocumentCharLimit { get; set; } = 120_000;

    // For large documents: passages retrieved per check, and the overall cap sent to the model
    public int PassagesPerCheck { get; set; } = 6;
    public int MaxPassages { get; set; } = 80;

    public int MaxChecksPerChecklist { get; set; } = 40;

    // Compliance review is accuracy-sensitive, so it runs at a higher effort than chat
    public string Effort { get; set; } = "high";
    public int MaxOutputTokens { get; set; } = 16000;
}
