using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Murmur.Core.Formatting;

/// <summary>
/// Normalizes supported English number phrases without changing surrounding text.
/// </summary>
public static class EnglishNumberParser
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<string, int> DigitWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0,
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
    };

    private static readonly Dictionary<string, int> SmallNumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0,
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10,
        ["eleven"] = 11,
        ["twelve"] = 12,
        ["thirteen"] = 13,
        ["fourteen"] = 14,
        ["fifteen"] = 15,
        ["sixteen"] = 16,
        ["seventeen"] = 17,
        ["eighteen"] = 18,
        ["nineteen"] = 19,
        ["twenty"] = 20,
        ["thirty"] = 30,
        ["forty"] = 40,
        ["fifty"] = 50,
        ["sixty"] = 60,
        ["seventy"] = 70,
        ["eighty"] = 80,
        ["ninety"] = 90,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twenty"] = 20,
        ["thirty"] = 30,
        ["forty"] = 40,
        ["fifty"] = 50,
        ["sixty"] = 60,
        ["seventy"] = 70,
        ["eighty"] = 80,
        ["ninety"] = 90,
    };

    private static readonly string DigitWordPattern = $"(?:{string.Join('|', DigitWords.Keys)})";
    private static readonly string NonZeroDigitWordPattern = $"(?:{string.Join('|', DigitWords.Keys.Where(word => word != "zero"))})";
    private static readonly string TeenWordPattern = "(?:ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen)";
    private static readonly string TensWordPattern = $"(?:{string.Join('|', Tens.Keys)})";
    private static readonly string UnderHundredPattern = $"(?:{DigitWordPattern}|{TeenWordPattern}|{TensWordPattern}(?:(?:\\s+|-\\s*){NonZeroDigitWordPattern})?)";
    private static readonly string SpokenNumberPattern = $"(?:(?:{NonZeroDigitWordPattern})\\s+hundred(?:\\s+(?:and\\s+)?{UnderHundredPattern})?|{UnderHundredPattern})";
    private static readonly string FractionTokenPattern = $"(?:[0-9]+|{DigitWordPattern})";

    private static readonly Regex NumberExpressionRegex = new(
        $@"(?<![\p{{L}}\p{{Nd}}_])(?:(?<minus>minus)\s+)?(?:(?<digits>[0-9]+)|(?<spoken>{SpokenNumberPattern}))(?:\s+point\s+(?<fraction>{FractionTokenPattern}(?:\s+{FractionTokenPattern})*))?(?!\s+point\b)(?![\p{{L}}\p{{Nd}}_]|\.[0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        MatchTimeout);

    /// <summary>
    /// Normalizes supported English spoken numbers in the given text.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <returns>The normalized text.</returns>
    public static string NormalizeSpokenNumbers(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return NumberExpressionRegex.Replace(text, NormalizeMatch);
    }

    private static string NormalizeMatch(Match match)
    {
        var integerText = match.Groups["digits"].Value;
        if (!match.Groups["digits"].Success)
        {
            if (!TryParseSpokenInteger(match.Groups["spoken"].Value, out var integerValue))
            {
                return match.Value;
            }

            integerText = integerValue.ToString(CultureInfo.InvariantCulture);
        }

        var sign = match.Groups["minus"].Success ? "-" : string.Empty;
        if (!match.Groups["fraction"].Success)
        {
            return sign + integerText;
        }

        var fractionalDigits = new StringBuilder();
        foreach (var token in match.Groups["fraction"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (DigitWords.TryGetValue(token, out var digit))
            {
                fractionalDigits.Append(digit.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                fractionalDigits.Append(token);
            }
        }

        return $"{sign}{integerText}.{fractionalDigits}";
    }

    private static bool TryParseSpokenInteger(string input, out int value)
    {
        var tokens = input.Replace('-', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        value = 0;

        if (tokens.Length >= 2
            && DigitWords.TryGetValue(tokens[0], out var hundredsDigit)
            && hundredsDigit > 0
            && tokens[1].Equals("hundred", StringComparison.OrdinalIgnoreCase))
        {
            value = hundredsDigit * 100;
            if (tokens.Length == 2)
            {
                return true;
            }

            var remainderStart = tokens[2].Equals("and", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            if (remainderStart == tokens.Length)
            {
                return false;
            }

            if (!TryParseUnderHundred(tokens[remainderStart..], out var remainder))
            {
                return false;
            }

            value += remainder;
            return true;
        }

        return TryParseUnderHundred(tokens, out value);
    }

    private static bool TryParseUnderHundred(string[] tokens, out int value)
    {
        value = 0;
        if (tokens.Length == 1)
        {
            return SmallNumberWords.TryGetValue(tokens[0], out value);
        }

        if (tokens.Length == 2
            && Tens.TryGetValue(tokens[0], out var tens)
            && DigitWords.TryGetValue(tokens[1], out var units)
            && units > 0)
        {
            value = tens + units;
            return true;
        }

        return false;
    }
}
