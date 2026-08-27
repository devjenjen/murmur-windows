using Murmur.Core.Formatting;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class RuleBasedFormatterTests
{
    private readonly RuleBasedFormatter _formatter = new();

    [Fact]
    public void Strips_Standalone_Fillers()
    {
        var input = "Um, this is uh a test mhm.";
        var result = _formatter.Format(input);
        result.ShouldBe("This is a test.");
    }

    [Fact]
    public void Converts_Spoken_Punctuation()
    {
        var input = "hello world new line next line new paragraph third line";
        var result = _formatter.Format(input);
        result.ShouldBe("Hello world\nnext line\n\nthird line.");
    }

    [Fact]
    public void Capitalizes_Sentences_And_Adds_Terminal_Punctuation()
    {
        var input = "first sentence. second sentence? third sentence";
        var result = _formatter.Format(input);
        result.ShouldBe("First sentence. Second sentence? Third sentence.");
    }

    [Fact]
    public void Strips_German_Fillers_And_Preserves_Repetitions()
    {
        var result = new RuleBasedFormatter().Format(
            "Ähm, ich ich möchte ehm dieses Mikrofon nutzen");

        result.ShouldBe("Ich ich möchte dieses Mikrofon nutzen.");
    }

    [Theory]
    [InlineData("very very good", "Very very good.")]
    [InlineData("no no no", "No no no.")]
    [InlineData("had had", "Had had.")]
    public void Preserves_Intentional_Repetitions_When_Removing_Fillers(string input, string expected)
    {
        new RuleBasedFormatter().Format(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("äh")]
    [InlineData("ähm")]
    [InlineData("um")]
    [InlineData("uh")]
    public void Removes_Standalone_Filler_Tokens(string filler)
    {
        new RuleBasedFormatter().Format($"{filler} hello").ShouldBe("Hello.");
    }

    [Fact]
    public void Can_Preserve_Fillers_And_Repetitions()
    {
        var result = new RuleBasedFormatter(removeFillers: false).Format(
            "ähm ich ich bleibe dabei");

        result.ShouldBe("Ähm ich ich bleibe dabei.");
    }

    [Fact]
    public void Preserves_Protected_Letter_Marker_At_Sentence_Start()
    {
        new RuleBasedFormatter().Format("\u200Cx = 5").ShouldBe("x = 5.");
    }
}
