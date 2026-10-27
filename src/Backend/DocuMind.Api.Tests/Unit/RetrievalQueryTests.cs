using DocuMind.Api.Chat;
using Xunit;

namespace DocuMind.Api.Tests.Unit;

public class RetrievalQueryTests
{
    [Fact]
    public void Long_question_is_used_as_is()
    {
        var history = new List<ChatTurn> { new("user", "What is the notice period?") };
        var question = new string('x', 80);

        Assert.Equal(question, RetrievalQuery.Build(history, question));
    }

    [Fact]
    public void Short_follow_up_is_combined_with_previous_user_question()
    {
        var history = new List<ChatTurn>
        {
            new("user", "What is the notice period?"),
            new("assistant", "Thirty days."),
        };

        Assert.Equal("What is the notice period? and for suppliers?", RetrievalQuery.Build(history, "and for suppliers?"));
    }

    [Fact]
    public void Short_question_without_history_is_unchanged()
    {
        Assert.Equal("Hi?", RetrievalQuery.Build([], "Hi?"));
    }
}
