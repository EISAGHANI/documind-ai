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
    // Returns one embedding per input, in the same order as the inputs
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default);
}
