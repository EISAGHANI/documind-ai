namespace DocuMind.Api.Chat;

// Follow-up questions ("and what about termination?") retrieve poorly on their own, so a short
// question is searched together with the previous user question.
public static class RetrievalQuery
{
    private const int ShortQuestionChars = 60;

    public static string Build(IReadOnlyList<ChatTurn> history, string question)
    {
        if (question.Length > ShortQuestionChars)
        {
            return question;
        }

        var previous = history.LastOrDefault(t => t.Role == "user")?.Content;
        if (string.IsNullOrWhiteSpace(previous))
        {
            return question;
        }

        var context = previous.Length > 300 ? previous[..300] : previous;
        return $"{context} {question}";
    }
}
