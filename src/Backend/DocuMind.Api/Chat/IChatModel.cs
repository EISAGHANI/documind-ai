namespace DocuMind.Api.Chat;

// A chat provider that answers a question from retrieved chunks and streams the answer
public interface IChatModel
{
    IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        IReadOnlyList<ChatTurn> history,
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken);
}

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
