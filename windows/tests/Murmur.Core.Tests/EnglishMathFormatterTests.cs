using Murmur.Core.Formatting;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class EnglishMathFormatterTests
{
    [Theory]
    [InlineData("five times three equals fifteen", "5 × 3 = 15.")]
    [InlineData("x equals five point zero five", "x = 5.05.")]
    [InlineData("x equals five point zero five times y", "x = 5.05 × y.")]
    [InlineData("x squared", "x².")]
    [InlineData("x to the power of two", "x².")]
    [InlineData("x to the power of 12", "x¹².")]
    [InlineData("x to the power of minus three", "x⁻³.")]
    [InlineData("square root of x", "√x.")]
    [InlineData("square root of open parenthesis x plus one close parenthesis", "√(x + 1)")]
    [InlineData("x is less than or equal to y", "x ≤ y.")]
    [InlineData("a multiplied by b", "a × b.")]
    [InlineData("capital sigma alpha beta", "Σ α β.")]
    [InlineData("x and y", "x and y.")]
    [InlineData("someone has a point of view", "Someone has a point of view.")]
    public void Formats_English_Expressions_In_Unicode(string input, string expected)
        => new MathFormatter().Format(input).ShouldBe(expected);

    [Fact]
    public void Formats_English_Expressions_In_Latex()
    {
        var formatter = new MathFormatter(MathFormatMode.Latex);
        formatter.Format("s squared plus square root of x").ShouldBe(@"s^2 + \sqrt{x}");
        formatter.Format("x to the power of minus three").ShouldBe("x^{-3}");
        formatter.Format("x is less than or equal to y").ShouldBe(@"x \le y.");
        formatter.Format("x equals five point zero five times y").ShouldBe("x = 5.05 \\cdot y.");
        formatter.Format("square root of five point zero five").ShouldBe(@"\sqrt{5.05}");
        formatter.Format("square root of x.").ShouldBe(@"\sqrt{x}.");
    }

    [Fact]
    public void Formats_English_Brackets_Operators_And_Greek_Symbols()
    {
        var formatter = new MathFormatter();
        formatter.Format("open curly brace a close curly brace").ShouldBe("{a}");
        formatter.Format("open square bracket x close square bracket").ShouldBe("[x]");
        formatter.Format("open brace x close brace").ShouldBe("{x}");
        formatter.Format("open bracket x close bracket").ShouldBe("[x]");
        formatter.Format("open paren x close paren").ShouldBe("(x)");
        formatter.Format("x IS LESS   THAN OR EQUAL TO y").ShouldBe("x ≤ y.");
        formatter.Format("minus three times delta").ShouldBe("−3 × Δ.");
        formatter.Format("x is not equal to y").ShouldBe("x ≠ y.");
        formatter.Format("x not equal to y").ShouldBe("x ≠ y.");
        formatter.Format("x equal to y").ShouldBe("x = y.");
        formatter.Format("x is equal to y").ShouldBe("x = y.");
        formatter.Format("x divided by three").ShouldBe("x ÷ 3.");
        formatter.Format("x less than y").ShouldBe("x < y.");
        formatter.Format("x greater than y").ShouldBe("x > y.");
        formatter.Format("x is greater than or equal to y").ShouldBe("x ≥ y.");
        formatter.Format("x is approximately equal to y").ShouldBe("x ≈ y.");
        formatter.Format("x approximately equal to y").ShouldBe("x ≈ y.");
        formatter.Format("uppercase sigma").ShouldBe("Σ.");
        formatter.Format("lowercase sigma").ShouldBe("σ.");
    }

    [Fact]
    public void Honors_Filler_Handling_And_Empty_Input()
    {
        new MathFormatter(removeFillers: false).Format("um x plus y").ShouldBe("Um x + y.");
        new MathFormatter().Format("um x plus y").ShouldBe("x + y.");
        Should.Throw<ArgumentNullException>(() => new MathFormatter().Format(null!));
        new MathFormatter().Format("").ShouldBe("");
        new MathFormatter().Format("   ").ShouldBe("");
    }
}
