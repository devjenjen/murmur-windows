using System.Text.RegularExpressions;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Formatting;

/// <summary>
/// Output format mode for mathematical formulas.
/// </summary>
public enum MathFormatMode
{
    /// <summary>Unicode symbols (e.g. ×, ≤, α, 5,05).</summary>
    Unicode,

    /// <summary>LaTeX syntax (e.g. \cdot, \le, \alpha, 5.05).</summary>
    Latex
}

/// <summary>
/// Formats spoken German mathematical and scientific dictations into Unicode or LaTeX notation.
/// </summary>
public sealed class GermanMathFormatter : ITextFormatter
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly string[] Fillers =
        ["um", "uh", "erm", "uhm", "hmm", "mhm", "äh", "ähm", "ehm", "hm"];

    private readonly MathFormatMode _mode;
    private readonly bool _removeFillers;

    private static readonly (string Phrase, string Replacement)[] SpokenBrackets =
    [
        ("geschweifte klammer auf", "{"),
        ("geschweifte klammer zu", "}"),
        ("eckige klammer auf", "["),
        ("eckige klammer zu", "]"),
        ("runde klammer auf", "("),
        ("runde klammer zu", ")"),
        ("klammer auf", "("),
        ("klammer zu", ")"),
    ];

    private static readonly (string Phrase, string Replacement)[] SpokenOperators =
    [
        ("approximately gleich", " ≈ "),
        ("ungefähr gleich", " ≈ "),
        ("ungefaehr gleich", " ≈ "),
        ("kleiner gleich", " ≤ "),
        ("größer gleich", " ≥ "),
        ("groesser gleich", " ≥ "),
        ("ist gleich", " = "),
        ("ungleich", " ≠ "),
        ("geteilt durch", " ÷ "),
        ("gleich", " = "),
        ("plus", " + "),
        ("mal", " × "),
    ];

    private static readonly (string Phrase, string Replacement)[] GreekLetters =
    [
        ("alpha", "α"),
        ("beta", "β"),
        ("gamma", "γ"),
        ("delta", "Δ"),
        ("theta", "θ"),
        ("lambda", "λ"),
        ("omega", "ω"),
        ("pi", "π"),
    ];

    private static readonly Regex LeadingSingleVariableRegex = new(
        @"^(\s*[\{\[\(]?\s*)([a-z])\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex DecimalCommaRegex = new(
        @"(\d+),(\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex BracketTrimOpenRegex = new(
        @"([\{\[\(])\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex BracketTrimCloseRegex = new(
        @"\s+([\}\]\)])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    /// <summary>
    /// Initializes a new instance of the <see cref="GermanMathFormatter"/> class.
    /// </summary>
    /// <param name="mode">The target output mode (Unicode or LaTeX).</param>
    /// <param name="removeFillers">Whether filler words should be removed.</param>
    public GermanMathFormatter(MathFormatMode mode = MathFormatMode.Unicode, bool removeFillers = true)
    {
        _mode = mode;
        _removeFillers = removeFillers;
    }

    /// <inheritdoc />
    public string Format(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var text = raw.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // 1. Strip fillers if enabled so leading variables and compound math phrases are clean
        if (_removeFillers)
        {
            foreach (var filler in Fillers)
            {
                var pattern = $@"(?i)(?<![\w']){Regex.Escape(filler)}\b,?";
                text = Regex.Replace(text, pattern, "", RegexOptions.CultureInvariant, MatchTimeout);
            }
            text = text.Trim();
        }

        // 2. Normalize spoken German numbers and decimals
        text = GermanNumberParser.NormalizeSpokenNumbers(text);

        // 3. Replace longest spoken brackets first
        foreach (var (phrase, replacement) in SpokenBrackets)
        {
            var pattern = $@"(?i)\b{Regex.Escape(phrase)}\b";
            text = Regex.Replace(text, pattern, replacement, RegexOptions.CultureInvariant, MatchTimeout);
        }

        // 4. Replace longest spoken operators
        foreach (var (phrase, replacement) in SpokenOperators)
        {
            var pattern = $@"(?i)\b{Regex.Escape(phrase)}\b";
            text = Regex.Replace(text, pattern, replacement, RegexOptions.CultureInvariant, MatchTimeout);
        }

        // 5. Handle minus (as operator and before Greek/variables/numbers)
        text = Regex.Replace(text, @"(?i)\bminus\b", "−", RegexOptions.CultureInvariant, MatchTimeout);

        // 6. Replace Greek letters
        foreach (var (phrase, replacement) in GreekLetters)
        {
            var pattern = $@"(?i)\b{Regex.Escape(phrase)}\b";
            text = Regex.Replace(text, pattern, replacement, RegexOptions.CultureInvariant, MatchTimeout);
        }

        // 7. Apply mode-specific output substitutions
        if (_mode == MathFormatMode.Unicode)
        {
            // In Unicode mode, ensure minus sign is Unicode minus − (U+2212)
            text = text.Replace("-", "−", StringComparison.Ordinal);
        }
        else if (_mode == MathFormatMode.Latex)
        {
            // LaTeX substitutions
            text = text.Replace("−", "-", StringComparison.Ordinal);
            text = text.Replace("×", @"\cdot", StringComparison.Ordinal);
            text = text.Replace("÷", @"\div", StringComparison.Ordinal);
            text = text.Replace("≤", @"\le", StringComparison.Ordinal);
            text = text.Replace("≥", @"\ge", StringComparison.Ordinal);
            text = text.Replace("≠", @"\ne", StringComparison.Ordinal);
            text = text.Replace("≈", @"\approx", StringComparison.Ordinal);

            // Greek Unicode symbols -> LaTeX commands
            text = text.Replace("α", @"\alpha", StringComparison.Ordinal);
            text = text.Replace("β", @"\beta", StringComparison.Ordinal);
            text = text.Replace("γ", @"\gamma", StringComparison.Ordinal);
            text = text.Replace("Δ", @"\Delta", StringComparison.Ordinal);
            text = text.Replace("π", @"\pi", StringComparison.Ordinal);
            text = text.Replace("θ", @"\theta", StringComparison.Ordinal);
            text = text.Replace("λ", @"\lambda", StringComparison.Ordinal);
            text = text.Replace("ω", @"\omega", StringComparison.Ordinal);

            // Decimal comma to decimal point in LaTeX mode (5,05 -> 5.05)
            text = DecimalCommaRegex.Replace(text, "$1.$2");
        }

        // 8. Clean up whitespace around brackets
        text = BracketTrimOpenRegex.Replace(text, "$1");
        text = BracketTrimCloseRegex.Replace(text, "$1");

        // 9. Protect a leading one-letter math variable with RuleBasedFormatter.ProtectedLetterMarker
        text = LeadingSingleVariableRegex.Replace(text, $"$1{RuleBasedFormatter.ProtectedLetterMarker}$2");

        // 10. Run through RuleBasedFormatter
        var formatter = new RuleBasedFormatter(_removeFillers);
        text = formatter.Format(text);

        // 11. Clean up whitespace inside brackets again if RuleBasedFormatter added space
        text = BracketTrimOpenRegex.Replace(text, "$1");
        text = BracketTrimCloseRegex.Replace(text, "$1");

        // 12. Remove any remaining marker
        text = text.Replace(RuleBasedFormatter.ProtectedLetterMarker.ToString(), "", StringComparison.Ordinal);

        return text;
    }
}
