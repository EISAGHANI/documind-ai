namespace DocuMind.Api.Chat;

// A tenant's chosen AI provider and model: streams cited answers and produces structured JSON
public interface IChatModel
{
    string Provider { get; }
    string Model { get; }

    IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        IReadOnlyList<ChatTurn> history,
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken);

    // Returns JSON that conforms to the given JSON Schema (the provider enforces it)
    Task<string> GenerateJsonAsync(StructuredRequest request, CancellationToken cancellationToken);
}

// Schema objects must set additionalProperties: false and list every property as required,
// which both Claude and OpenAI-style strict schemas accept.
public record StructuredRequest(
    string Instructions,
    string Input,
    string SchemaName,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Schema,
    int MaxTokens,
    string Effort);

// Thrown when the model's output can't be used (refusal, cut off, invalid JSON); shown to users
public class ModelOutputException(string message) : InvalidOperationException(message);

public static class ChatPrompts
{
    public const string Instructions = """
        You are DocuMind, an assistant that answers questions about an organization's own documents
        (contracts, compliance records, technical manuals).

        Answer using only the sources provided with the user's latest message. Cite the sources that
        support each claim. If the sources don't contain the answer, say that the documents don't cover
        it rather than answering from general knowledge. When sources conflict, point out the conflict
        and cite both sides.

        Be concise and direct. Use short paragraphs or bullet lists where they help.
        """;

    // Providers without native citations are asked to cite by source number instead
    public const string NumberedCitations = """

        The sources are numbered. Cite them inline with the source number in square brackets right
        after the claim it supports, for example: "Payment is due within 30 days [2]." Only cite
        numbers that appear in the sources.
        """;

    public static string SourceTitle(RetrievedChunk chunk) =>
        chunk.PageNumber is { } page ? $"{chunk.DocumentTitle} (page {page})" : chunk.DocumentTitle;
}
