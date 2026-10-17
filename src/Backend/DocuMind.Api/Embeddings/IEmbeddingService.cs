namespace DocuMind.Api.Embeddings;

public enum EmbeddingInputType
{
    // Text being indexed (document chunks)
    Document,

    // Text being searched with (user questions)
    Query
}

public interface IEmbeddingService
{
    // Returns one embedding per input, in the same order as the inputs.
    // The API key is the tenant's own; the model is fixed platform-wide so all vectors stay comparable.
    Task<IReadOnlyList<float[]>> EmbedAsync(
        string apiKey,
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default);
}
