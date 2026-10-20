using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using DocuMind.Api.Entities;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Embeddings;

// Calls the Voyage AI embeddings API (https://docs.voyageai.com/reference/embeddings-api).
// Retries on 429/5xx are handled by the resilience handler registered with this HttpClient.
public class VoyageEmbeddingService(HttpClient httpClient, IOptions<VoyageOptions> options) : IEmbeddingService
{
    private readonly VoyageOptions _options = options.Value;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        string apiKey,
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        var batches = inputs.Chunk(_options.BatchSize).ToArray();
        var results = new float[batches.Length][][];

        // Batches are independent, so embed a few at a time instead of strictly one after another
        await Parallel.ForEachAsync(
            Enumerable.Range(0, batches.Length),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.MaxParallelBatches), CancellationToken = cancellationToken },
            async (i, ct) => results[i] = await EmbedBatchAsync(apiKey, batches[i], inputType, ct));

        return results.SelectMany(r => r).ToList();
    }

    private async Task<float[][]> EmbedBatchAsync(
        string apiKey,
        string[] batch,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken)
    {
        var request = new VoyageRequest(
            batch,
            _options.Model,
            inputType == EmbeddingInputType.Query ? "query" : "document",
            DocumentChunk.EmbeddingDimensions);

        using var message = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Voyage embeddings request failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<VoyageResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Voyage returned an empty response.");

        if (result.Data.Count != batch.Length)
        {
            throw new InvalidOperationException(
                $"Voyage returned {result.Data.Count} embeddings for {batch.Length} inputs.");
        }

        return result.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToArray();
    }

    private record VoyageRequest(
        [property: JsonPropertyName("input")] string[] Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("output_dimension")] int OutputDimension);

    private record VoyageResponse(
        [property: JsonPropertyName("data")] List<VoyageEmbedding> Data);

    private record VoyageEmbedding(
        [property: JsonPropertyName("embedding")] float[] Embedding,
        [property: JsonPropertyName("index")] int Index);
}
