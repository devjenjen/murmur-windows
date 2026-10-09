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
/// Formats spoken German and English mathematical and scientific dictations into Unicode or LaTeX notation.
/// </summary>
public sealed class MathFormatter : ITextFormatter
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
        ("open curly brace", "{"),
        ("close curly brace", "}"),
        ("open square bracket", "["),
        ("close square bracket", "]"),
        ("open parenthesis", "("),
        ("close parenthesis", ")"),
        ("open brace", "{"),
        ("close brace", "}"),
        ("open bracket", "["),
        ("close bracket", "]"),
        ("open paren", "("),
        ("close paren", ")"),
        ("klammer auf", "("),
        ("klammer zu", ")"),
    ];

    private static readonly (string Phrase, string Replacement)[] SpokenOperators =
    [
        ("is approximately equal to", " ≈ "),
        ("approximately equal to", " ≈ "),
        ("is less than or equal to", " ≤ "),
        ("less than or equal to", " ≤ "),
        ("is greater than or equal to", " ≥ "),
        ("greater than or equal to", " ≥ "),
        ("is not equal to", " ≠ "),
        ("not equal to", " ≠ "),
        ("is equal to", " = "),
        ("equal to", " = "),
        ("multiplied by", " × "),
        ("divided by", " ÷ "),
        ("times", " × "),
        ("is less than", " < "),
        ("less than", " < "),
        ("is greater than", " > "),
        ("greater than", " > "),
        ("equals", " = "),
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
        ("uppercase sigma", "Σ"),
        ("capital sigma", "Σ"),
        ("lowercase sigma", "σ"),
        ("großes sigma", "Σ"),
        ("grosses sigma", "Σ"),
        ("kleines sigma", "σ"),
        ("sigma", "σ"),
        ("alpha", "α"),
        ("beta", "β"),
        ("gamma", "γ"),
        ("delta", "Δ"),
        ("theta", "θ"),
        ("lambda", "λ"),
        ("omega", "ω"),
        ("pi", "π"),
    ];

    private static readonly Regex SpokenPowerRegex = new(
        @"(?i)(?<![\p{L}\p{N}])(?<base>[A-Za-zα-ωΑ-Ω0-9]+|[\)\]\}])\s+(?:hoch|to\s+the\s+power\s+of)\s+(?<sign>minus\s+|[-−]\s*)?(?<exponent>\d+)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    // ponytail: single-letter `2` is treated as square; replace with context parsing if indexed identifiers matter.
    private static readonly Regex CompactSquareRegex = new(
        @"(?i)(?<![\p{L}\p{N}])(?<base>[A-Za-zα-ωΑ-Ω])2\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex SpokenSquareRegex = new(
        @"(?i)(?<![\p{L}\p{N}])(?<base>[A-Za-zα-ωΑ-Ω0-9]+|[\)\]\}])\s+(?:(?:zum|im)\s+)?(?:quadrat|squared)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    // ponytail: root binds to one atom or one explicit bracket group; full spoken operand parsing can come later.
    private static readonly Regex SpokenRootRegex = new(
        @"(?i)\b(?:(?:quadratische\s+wurzel|quadratwurzel|wurzel)\s+aus|square\s+root\s+of)\s+(?<operand>\([^()]*\)|\[[^\[\]]*\]|\{[^{}]*\}|[-−]?[0-9]+(?:[.,][0-9]+)?(?:\^\{?[-−]?\d+\}?|[⁰¹²³⁴⁵⁶⁷⁸⁹⁻]+)?(?![\p{L}\p{N}_])|[A-Za-zα-ωΑ-Ω0-9,⁰¹²³⁴⁵⁶⁷⁸⁹⁻^{}-]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex LeadingMathLetterRegex = new(
        @"^(\s*(?:(?:[−+√]\s*)?[\{\[\(]?\s*))(?<letter>[a-zα-ω])(?=\b|[⁰¹²³⁴⁵⁶⁷⁸⁹⁻])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex LatexCommandRegex = new(
        @"\\[a-z]+",
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
    /// Initializes a new instance of the <see cref="MathFormatter"/> class.
    /// </summary>
    /// <param name="mode">The target output mode (Unicode or LaTeX).</param>
    /// <param name="removeFillers">Whether filler words should be removed.</param>
    public MathFormatter(MathFormatMode mode = MathFormatMode.Unicode, bool removeFillers = true)
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

        // 2. Normalize spoken German and English numbers and decimals
        text = GermanNumberParser.NormalizeSpokenNumbers(text);
        text = EnglishNumberParser.NormalizeSpokenNumbers(text);

        // 3. Replace longest spoken brackets first
        text = ReplacePhrases(text, SpokenBrackets);

        // 4. Normalize spoken powers before operators alter their context
        text = CompactSquareRegex.Replace(text, FormatSquare);
        text = SpokenPowerRegex.Replace(text, FormatPower);
        text = SpokenSquareRegex.Replace(text, FormatSquare);

        // 5. Normalize spoken roots before operators alter their context
        text = SpokenRootRegex.Replace(text, FormatRoot);

        // 6. Replace longest spoken operators
        text = ReplacePhrases(text, SpokenOperators);

        // 7. Handle minus (as operator and before Greek/variables/numbers)
        text = Regex.Replace(text, @"(?i)\bminus\b", "−", RegexOptions.CultureInvariant, MatchTimeout);

        // 8. Replace Greek letters
        text = ReplacePhrases(text, GreekLetters);

        // 9. Apply mode-specific output substitutions
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
            text = text.Replace("σ", @"\sigma", StringComparison.Ordinal);
            text = text.Replace("Σ", @"\Sigma", StringComparison.Ordinal);

            // Decimal comma to decimal point in LaTeX mode (5,05 -> 5.05)
            text = DecimalCommaRegex.Replace(text, "$1.$2");

            // Keep LaTeX commands lowercase when RuleBasedFormatter capitalizes sentences.
            text = LatexCommandRegex.Replace(text, match =>
                $"{RuleBasedFormatter.ProtectedLetterMarker}{match.Value}");
        }

        // 10. Clean up whitespace around brackets
        text = BracketTrimOpenRegex.Replace(text, "$1");
        text = BracketTrimCloseRegex.Replace(text, "$1");

        // 11. Protect leading math letters from sentence capitalization
        text = LeadingMathLetterRegex.Replace(text, match =>
        {
            var letter = match.Groups["letter"].Value;
            return $"{match.Groups[1].Value}{RuleBasedFormatter.ProtectedLetterMarker}{letter}";
        });

        // 12. Run through RuleBasedFormatter
        var formatter = new RuleBasedFormatter(_removeFillers);
        text = formatter.Format(text);

        // 13. Clean up whitespace inside brackets again if RuleBasedFormatter added space
        text = BracketTrimOpenRegex.Replace(text, "$1");
        text = BracketTrimCloseRegex.Replace(text, "$1");

        // 14. Remove any remaining marker
        text = text.Replace(RuleBasedFormatter.ProtectedLetterMarker.ToString(), "", StringComparison.Ordinal);

        return text;
    }

    private string FormatPower(Match match)
    {
        var baseText = match.Groups["base"].Value;
        var exponent = match.Groups["exponent"].Value;
        var negative = match.Groups["sign"].Success;

        if (_mode == MathFormatMode.Unicode)
        {
            var superscript = new char[exponent.Length + (negative ? 1 : 0)];
            var index = 0;
            if (negative)
            {
                superscript[index++] = '⁻';
            }

            const string superscriptDigits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
            foreach (var digit in exponent)
            {
                superscript[index++] = superscriptDigits[digit - '0'];
            }

            return baseText + new string(superscript);
        }

        var sign = negative ? "-" : string.Empty;
        return !negative && exponent.Length == 1
            ? $"{baseText}^{exponent}"
            : $"{baseText}^{{{sign}{exponent}}}";
    }

    private string FormatSquare(Match match)
    {
        var baseText = match.Groups["base"].Value;
        return _mode == MathFormatMode.Unicode ? baseText + "²" : baseText + "^2";
    }

    private string FormatRoot(Match match)
    {
        var operand = match.Groups["operand"].Value.Trim();
        return _mode == MathFormatMode.Unicode
            ? $"√{operand}"
            : $"\\sqrt{{{operand}}}";
    }

    private static string ReplacePhrases(string text, (string Phrase, string Replacement)[] phrases)
    {
        foreach (var (phrase, replacement) in phrases)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                words[i] = Regex.Escape(words[i]);
            }

            var pattern = $@"(?i)\b{string.Join(@"\s+", words)}\b";
            text = Regex.Replace(text, pattern, replacement, RegexOptions.CultureInvariant, MatchTimeout);
        }

        return text;
    }
}
