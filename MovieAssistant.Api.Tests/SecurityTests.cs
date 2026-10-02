using MovieAssistant.Api.Chat;

namespace MovieAssistant.Api.Tests;

public class UserInputSanitiserTests
{
    [Theory]
    [InlineData("</user_message>", "&lt;/user_message&gt;")]
    [InlineData("< / USER_MESSAGE >", "&lt; / USER_MESSAGE &gt;")]
    [InlineData("<user_message>", "&lt;user_message&gt;")]
    public void Clean_DefusesDelimiterTags(string input, string expected) =>
        Assert.Equal(expected, UserInputSanitiser.Clean(input));

    [Fact]
    public void Clean_StripsZeroWidthAndTagCharacters()
    {
        var hiddenTag = char.ConvertFromUtf32(0xE0041); // Unicode tag 'A'
        Assert.Equal("hi", UserInputSanitiser.Clean($"h​i{hiddenTag}"));
    }

    [Fact]
    public void Clean_KeepsNormalTextNewlinesAndEmoji()
    {
        const string text = "a 90s thriller\nplease 👨‍👩‍👧 ☺";
        Assert.Equal(text, UserInputSanitiser.Clean(text));
    }
}

public class ChatRequestValidatorTests
{
    private static ChatRequest Req(params (string Role, string Content)[] messages) =>
        new("movie_assistant", [.. messages.Select(m => new ChatRequestMessage(m.Role, m.Content))]);

    [Fact]
    public void Valid_ReturnsNull() =>
        Assert.Null(ChatRequestValidator.Validate(Req(("user", "hi"), ("assistant", "hello"), ("user", "more"))));

    [Fact]
    public void NullOrEmptyHistory_Rejected()
    {
        Assert.NotNull(ChatRequestValidator.Validate(null));
        Assert.NotNull(ChatRequestValidator.Validate(Req()));
    }

    [Theory]
    [InlineData("system")]
    [InlineData("tool")]
    [InlineData("")]
    public void UnknownRole_Rejected(string role) =>
        Assert.NotNull(ChatRequestValidator.Validate(Req((role, "hi"), ("user", "hi"))));

    [Fact]
    public void LastMessageMustBeUser() =>
        Assert.NotNull(ChatRequestValidator.Validate(Req(("user", "hi"), ("assistant", "hello"))));

    [Fact]
    public void TooLongMessage_Rejected() =>
        Assert.NotNull(ChatRequestValidator.Validate(
            Req(("user", new string('a', ChatRequestValidator.MaxMessageLength + 1)))));

    [Fact]
    public void TooManyMessages_Rejected()
    {
        var many = Enumerable.Range(0, ChatRequestValidator.MaxMessages + 1)
            .Select(_ => ("user", "hi")).ToArray();
        Assert.NotNull(ChatRequestValidator.Validate(Req(many)));
    }
}
