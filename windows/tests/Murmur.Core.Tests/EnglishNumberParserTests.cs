using Murmur.Core.Formatting;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class EnglishNumberParserTests
{
    [Theory]
    [InlineData("zero", "0")]
    [InlineData("nineteen", "19")]
    [InlineData("twenty one", "21")]
    [InlineData("twenty-one", "21")]
    [InlineData("one hundred five", "105")]
    [InlineData("one hundred and five", "105")]
    [InlineData("nine hundred ninety-nine", "999")]
    [InlineData("five point zero five", "5.05")]
    [InlineData("zero point zero five", "0.05")]
    [InlineData("minus five point zero five", "-5.05")]
    [InlineData("5 point 0 five", "5.05")]
    [InlineData("TWENTY   ONE", "21")]
    [InlineData("someone stone a an x and y", "someone stone a an x and y")]
    [InlineData("42, 3.14, 1000 and x1", "42, 3.14, 1000 and x1")]
    public void Normalizes_Only_Supported_Number_Expressions(string input, string expected)
        => EnglishNumberParser.NormalizeSpokenNumbers(input).ShouldBe(expected);

    [Fact]
    public void Null_Throws_And_Whitespace_Is_Preserved()
    {
        Should.Throw<ArgumentNullException>(() => EnglishNumberParser.NormalizeSpokenNumbers(null!));
        EnglishNumberParser.NormalizeSpokenNumbers("").ShouldBe("");
        EnglishNumberParser.NormalizeSpokenNumbers("   ").ShouldBe("   ");
    }
}
