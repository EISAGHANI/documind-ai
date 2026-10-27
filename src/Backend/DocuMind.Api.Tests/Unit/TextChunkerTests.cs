using DocuMind.Api.Processing;
using Xunit;

namespace DocuMind.Api.Tests.Unit;

public class TextChunkerTests
{
    [Fact]
    public void Empty_and_whitespace_pages_produce_no_chunks()
    {
        var chunks = TextChunker.Split([new PageText(1, ""), new PageText(2, "   \n\t ")]);

        Assert.Empty(chunks);
    }

    [Fact]
    public void Short_page_becomes_a_single_chunk_with_its_page_number()
    {
        var chunks = TextChunker.Split([new PageText(7, "The tenant shall pay rent monthly.")]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(7, chunk.PageNumber);
        Assert.Contains("pay rent monthly", chunk.Content);
    }

    [Fact]
    public void Chunks_never_exceed_the_maximum_length()
    {
        var text = string.Join(" ", Enumerable.Range(0, 2000).Select(i => $"word{i}"));

        var chunks = TextChunker.Split([new PageText(1, text)]);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= TextChunker.MaxChunkLength));
    }

    [Fact]
    public void Chunks_never_span_pages()
    {
        var pageOne = string.Join(" ", Enumerable.Range(0, 600).Select(i => $"alpha{i}"));
        var pageTwo = string.Join(" ", Enumerable.Range(0, 600).Select(i => $"beta{i}"));

        var chunks = TextChunker.Split([new PageText(1, pageOne), new PageText(2, pageTwo)]);

        Assert.All(chunks.Where(c => c.PageNumber == 1), c => Assert.DoesNotContain("beta", c.Content));
        Assert.All(chunks.Where(c => c.PageNumber == 2), c => Assert.DoesNotContain("alpha", c.Content));
        Assert.Contains(chunks, c => c.PageNumber == 1);
        Assert.Contains(chunks, c => c.PageNumber == 2);
    }

    [Fact]
    public void Every_word_of_a_long_page_is_covered_by_some_chunk()
    {
        var words = Enumerable.Range(0, 1500).Select(i => $"w{i}").ToList();

        var chunks = TextChunker.Split([new PageText(1, string.Join(" ", words))]);

        var covered = chunks.SelectMany(c => c.Content.Split(' ')).ToHashSet();
        Assert.All(words, w => Assert.Contains(w, covered));
    }

    [Fact]
    public void Consecutive_chunks_overlap_so_sentences_at_the_boundary_are_not_lost()
    {
        var text = string.Join(" ", Enumerable.Range(0, 1500).Select(i => $"w{i}"));

        var chunks = TextChunker.Split([new PageText(1, text)]);

        for (var i = 1; i < chunks.Count; i++)
        {
            var previousWords = chunks[i - 1].Content.Split(' ').ToHashSet();
            Assert.Contains(chunks[i].Content.Split(' ')[0], previousWords);
        }
    }

    [Fact]
    public void Splitting_always_terminates_on_text_without_spaces()
    {
        var chunks = TextChunker.Split([new PageText(1, new string('x', 10_000))]);

        Assert.True(chunks.Count >= 7);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= TextChunker.MaxChunkLength));
    }
}
