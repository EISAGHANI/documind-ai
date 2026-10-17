using System.Text.RegularExpressions;

namespace DocuMind.Api.Processing;

public record TextChunk(int PageNumber, string Content);

// Splits page text into overlapping chunks. Chunks never span pages, so every chunk
// keeps an exact page number for citations.
public static partial class TextChunker
{
    // ~1,500 characters is roughly 350-400 tokens: small enough for precise retrieval,
    // large enough to keep a clause or paragraph together
    public const int MaxChunkLength = 1500;
    public const int ChunkOverlap = 200;

    public static IReadOnlyList<TextChunk> Split(IEnumerable<PageText> pages)
    {
        var chunks = new List<TextChunk>();
        foreach (var page in pages)
        {
            var text = Normalize(page.Text);
            if (text.Length == 0)
            {
                continue;
            }

            foreach (var content in SplitText(text))
            {
                chunks.Add(new TextChunk(page.PageNumber, content));
            }
        }

        return chunks;
    }

    private static IEnumerable<string> SplitText(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + MaxChunkLength, text.Length);
            if (end < text.Length)
            {
                end = FindBreak(text, start, end);
            }

            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
            {
                yield return chunk;
            }

            if (end >= text.Length)
            {
                yield break;
            }

            // Step back by the overlap, but always move forward, and start on a word boundary
            var next = Math.Max(end - ChunkOverlap, start + 1);
            while (next < end && !char.IsWhiteSpace(text[next - 1]))
            {
                next++;
            }

            start = next;
        }
    }

    // Prefer breaking at a paragraph, then a sentence, then a word, in the last half of the window
    private static int FindBreak(string text, int start, int end)
    {
        var minimum = start + MaxChunkLength / 2;

        var paragraph = text.LastIndexOf("\n\n", end - 1, end - minimum, StringComparison.Ordinal);
        if (paragraph >= minimum)
        {
            return paragraph + 2;
        }

        for (var i = end - 1; i >= minimum; i--)
        {
            if (text[i] is '.' or '!' or '?' or ';' && char.IsWhiteSpace(text[i + 1]))
            {
                return i + 1;
            }
        }

        var space = text.LastIndexOf(' ', end - 1, end - minimum);
        return space >= minimum ? space + 1 : end;
    }

    // Collapses runs of spaces and blank lines left over from PDF layout
    private static string Normalize(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\0', ' ');
        text = HorizontalWhitespace().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@"\n[ \n]*\n")]
    private static partial Regex BlankLines();
}
