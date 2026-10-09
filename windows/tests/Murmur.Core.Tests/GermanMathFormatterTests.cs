using Murmur.Core.Formatting;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class GermanMathFormatterTests
{
    [Fact]
    public void Basic_Math_Expressions_Are_Formatted()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("x plus y gleich z").ShouldBe("x + y = z.");
        formatter.Format("fünf mal drei gleich fünfzehn").ShouldBe("5 × 3 = 15.");
    }

    [Fact]
    public void Bracket_Expressions_Are_Formatted_With_Bracket_As_Terminal_Punctuation()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("geschweifte klammer auf a geschweifte klammer zu").ShouldBe("{a}");
        formatter.Format("eckige klammer auf x eckige klammer zu").ShouldBe("[x]");
        formatter.Format("klammer auf x plus eins klammer zu").ShouldBe("(x + 1)");
    }

    [Fact]
    public void Longest_Bracket_Phrases_Win_Over_Generic_Klammer()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("geschweifte klammer auf x klammer zu").ShouldBe("{x)");
        formatter.Format("eckige klammer auf x geschweifte klammer zu").ShouldBe("[x}");
    }

    [Fact]
    public void Negative_Values_And_Greek_Letters_Are_Formatted()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("minus drei mal delta").ShouldBe("−3 × Δ.");
    }

    [Fact]
    public void Spoken_Powers_Become_Superscripts()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("s quadrat").ShouldBe("s².");
        formatter.Format("x hoch zwei").ShouldBe("x².");
        formatter.Format("x hoch minus drei").ShouldBe("x⁻³.");
    }

    [Fact]
    public void Compact_Letter_Two_Is_Formatted_As_Square()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("S2").ShouldBe("S².");
    }

    [Fact]
    public void Spoken_Roots_Format_Atoms_And_Explicit_Groups()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("wurzel aus x").ShouldBe("√x.");
        formatter.Format("wurzel aus klammer auf x plus eins klammer zu").ShouldBe("√(x + 1)");
    }

    [Theory]
    [InlineData(MathFormatMode.Unicode, "wurzel aus fünf komma null fünf", "√5,05.")]
    [InlineData(MathFormatMode.Latex, "wurzel aus fünf komma null fünf", @"\sqrt{5.05}")]
    [InlineData(MathFormatMode.Latex, "square root of five point zero five", @"\sqrt{5.05}")]
    [InlineData(MathFormatMode.Unicode, "wurzel aus fünf quadrat", "√5².")]
    [InlineData(MathFormatMode.Latex, "wurzel aus fünf quadrat", @"\sqrt{5^2}")]
    [InlineData(MathFormatMode.Unicode, "square root of five squared", "√5².")]
    [InlineData(MathFormatMode.Latex, "square root of five squared", @"\sqrt{5^2}")]
    [InlineData(MathFormatMode.Latex, "wurzel aus fünf.", @"\sqrt{5}.")]
    public void Spoken_Roots_Consume_Complete_Numeric_Operands(
        MathFormatMode mode,
        string input,
        string expected)
    {
        new MathFormatter(mode).Format(input).ShouldBe(expected);
    }

    [Fact]
    public void Spoken_Sigma_Uses_Math_Symbols()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("sigma alpha beta").ShouldBe("σ α β.");
        formatter.Format("großes sigma alpha beta").ShouldBe("Σ α β.");
    }

    [Fact]
    public void Powers_And_Roots_Use_Latex_Syntax()
    {
        var formatter = new MathFormatter(MathFormatMode.Latex);
        formatter.Format("s quadrat plus wurzel aus x").ShouldBe(@"s^2 + \sqrt{x}");
        formatter.Format("sigma").ShouldBe(@"\sigma.");
    }

    [Fact]
    public void Decimal_Numbers_In_Unicode_And_Latex_Mode()
    {
        var unicodeFormatter = new MathFormatter(MathFormatMode.Unicode);
        unicodeFormatter.Format("x gleich fünf komma null fünf").ShouldBe("x = 5,05.");

        var latexFormatter = new MathFormatter(MathFormatMode.Latex);
        latexFormatter.Format("x gleich fünf komma null fünf").ShouldBe("x = 5.05.");
    }

    [Fact]
    public void Comparison_Operators_In_Unicode_And_Latex_Mode()
    {
        var unicodeFormatter = new MathFormatter(MathFormatMode.Unicode);
        unicodeFormatter.Format("x kleiner gleich y").ShouldBe("x ≤ y.");

        var latexFormatter = new MathFormatter(MathFormatMode.Latex);
        latexFormatter.Format("x kleiner gleich y").ShouldBe(@"x \le y.");
    }

    [Fact]
    public void Ordinary_Prose_Preserves_Capitalization_In_Math_Profile()
    {
        var formatter = new MathFormatter(MathFormatMode.Unicode);
        formatter.Format("eine Idee bleibt eine Idee").ShouldBe("Eine Idee bleibt eine Idee.");
    }

    [Fact]
    public void Filler_Handling_Honors_Constructor_Flag()
    {
        var strippingFormatter = new MathFormatter(MathFormatMode.Unicode, removeFillers: true);
        strippingFormatter.Format("ähm x plus y").ShouldBe("x + y.");

        var preservingFormatter = new MathFormatter(MathFormatMode.Unicode, removeFillers: false);
        preservingFormatter.Format("ähm x plus y").ShouldBe("Ähm x + y.");
    }
}
