using Murmur.Core.Formatting;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class GermanNumberParserTests
{
    [Fact]
    public void German_Units_And_Teens_Are_Normalized()
    {
        GermanNumberParser.NormalizeSpokenNumbers("fünf Äpfel").ShouldBe("5 Äpfel");
        GermanNumberParser.NormalizeSpokenNumbers("zwölf").ShouldBe("12");
        GermanNumberParser.NormalizeSpokenNumbers("zwei").ShouldBe("2");
        GermanNumberParser.NormalizeSpokenNumbers("siebzehn").ShouldBe("17");
    }

    [Fact]
    public void Tens_With_Sharp_S_Are_Normalized()
    {
        GermanNumberParser.NormalizeSpokenNumbers("dreißig Minuten").ShouldBe("30 Minuten");
    }

    [Fact]
    public void Alternate_Spelling_Tens_Are_Normalized()
    {
        GermanNumberParser.NormalizeSpokenNumbers("dreissig Minuten").ShouldBe("30 Minuten");
    }

    [Fact]
    public void Compound_Tens_Are_Normalized()
    {
        GermanNumberParser.NormalizeSpokenNumbers("einundzwanzig").ShouldBe("21");
        GermanNumberParser.NormalizeSpokenNumbers("dreiundfünfzig").ShouldBe("53");
    }

    [Fact]
    public void Hundreds_Are_Normalized()
    {
        GermanNumberParser.NormalizeSpokenNumbers("zweihundertfünf").ShouldBe("205");
        GermanNumberParser.NormalizeSpokenNumbers("einhundert").ShouldBe("100");
        GermanNumberParser.NormalizeSpokenNumbers("dreihundertzweiundvierzig").ShouldBe("342");
    }

    [Fact]
    public void Decimal_Zeros_Are_Normalized()
    {
        GermanNumberParser.NormalizeSpokenNumbers("null komma null fünf").ShouldBe("0,05");
        GermanNumberParser.NormalizeSpokenNumbers("drei komma eins vier").ShouldBe("3,14");
    }

    [Fact]
    public void Negative_Values_Are_Normalized()
    {
        GermanNumberParser.NormalizeSpokenNumbers("minus drei komma fünf").ShouldBe("-3,5");
        GermanNumberParser.NormalizeSpokenNumbers("minus zehn").ShouldBe("-10");
    }

    [Fact]
    public void Ordinary_Prose_Remains_Prose()
    {
        GermanNumberParser.NormalizeSpokenNumbers("eine Idee bleibt eine Idee").ShouldBe("eine Idee bleibt eine Idee");
    }

    [Fact]
    public void Standalone_Article_Remains_Prose()
    {
        GermanNumberParser.NormalizeSpokenNumbers("ein Test ist möglich").ShouldBe("ein Test ist möglich");
        GermanNumberParser.NormalizeSpokenNumbers("einer von vielen").ShouldBe("einer von vielen");
        GermanNumberParser.NormalizeSpokenNumbers("mit einem Freund").ShouldBe("mit einem Freund");
        GermanNumberParser.NormalizeSpokenNumbers("für einen Moment").ShouldBe("für einen Moment");
    }

    [Fact]
    public void Existing_Digits_And_Punctuation_Remain_Stable()
    {
        GermanNumberParser.NormalizeSpokenNumbers("42 ist die Antwort, 3.14 und 100%").ShouldBe("42 ist die Antwort, 3.14 und 100%");
    }

    [Fact]
    public void Null_And_Whitespace_Convention()
    {
        Should.Throw<ArgumentNullException>(() => GermanNumberParser.NormalizeSpokenNumbers(null!));
        GermanNumberParser.NormalizeSpokenNumbers("").ShouldBe("");
        GermanNumberParser.NormalizeSpokenNumbers("   ").ShouldBe("   ");
    }
}
