using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using OpenAI.Chat;

namespace DocuMind.Api.Chat;

// OpenAI and Gemini (through Google's OpenAI-compatible endpoint). These have no native document
// citations, so sources are numbered in the prompt and the model cites them as [n] in its text;
// the frontend turns those markers into links.
public class OpenAICompatibleChatModel(ChatClient client) : IChatModel
{
    public async IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        IReadOnlyList<ChatTurn> history,
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = [new SystemChatMessage(ChatPrompts.Instructions + ChatPrompts.NumberedCitations)];
        foreach (var turn in history)
        {
            messages.Add(turn.Role == "assistant" ? new AssistantChatMessage(turn.Content) : new UserChatMessage(turn.Content));
        }
        messages.Add(new UserChatMessage(BuildQuestion(question, chunks)));

        yield return new BlockStartStreamEvent();

        string? stopReason = null;
        await foreach (var update in client.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return new TextStreamEvent(part.Text);
                }
            }

            if (!string.IsNullOrEmpty(update.RefusalUpdate))
            {
                stopReason = "refusal";
            }

            if (update.FinishReason is { } finish)
            {
                stopReason ??= finish switch
                {
                    ChatFinishReason.Length => "max_tokens",
                    ChatFinishReason.ContentFilter => "refusal",
                    _ => "end_turn",
                };
            }
        }

        yield return new DoneStreamEvent(stopReason);
    }

    private static string BuildQuestion(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        var prompt = new StringBuilder("<sources>\n");
        for (var i = 0; i < chunks.Count; i++)
        {
            prompt.Append($"<source number=\"{i + 1}\" title=\"{SecurityElement.Escape(ChatPrompts.SourceTitle(chunks[i]))}\">\n")
                .Append(chunks[i].Content)
                .Append("\n</source>\n");
        }
        prompt.Append("</sources>\n\n").Append(question);
        return prompt.ToString();
    }
}
