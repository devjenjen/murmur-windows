using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Murmur.Core.Formatting;

/// <summary>
/// Deterministic normalizer for spoken German numbers and decimals.
/// </summary>
public static class GermanNumberParser
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<string, int> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["null"] = 0,
        ["ein"] = 1,
        ["eins"] = 1,
        ["zwei"] = 2,
        ["drei"] = 3,
        ["vier"] = 4,
        ["fünf"] = 5,
        ["fuenf"] = 5,
        ["sechs"] = 6,
        ["sieben"] = 7,
        ["acht"] = 8,
        ["neun"] = 9,
    };

    private static readonly Dictionary<string, int> Teens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zehn"] = 10,
        ["elf"] = 11,
        ["zwölf"] = 12,
        ["zwoelf"] = 12,
        ["dreizehn"] = 13,
        ["vierzehn"] = 14,
        ["fünfzehn"] = 15,
        ["fuenfzehn"] = 15,
        ["sechzehn"] = 16,
        ["siebzehn"] = 17,
        ["achtzehn"] = 18,
        ["neunzehn"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zwanzig"] = 20,
        ["dreißig"] = 30,
        ["dreissig"] = 30,
        ["vierzig"] = 40,
        ["fünfzig"] = 50,
        ["fuenfzig"] = 50,
        ["sechzig"] = 60,
        ["siebzig"] = 70,
        ["achtzig"] = 80,
        ["neunzig"] = 90,
    };

    private static readonly Dictionary<string, int> Hundreds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hundert"] = 100,
        ["einhundert"] = 100,
        ["zweihundert"] = 200,
        ["dreihundert"] = 300,
        ["vierhundert"] = 400,
        ["fünfhundert"] = 500,
        ["fuenfhundert"] = 500,
        ["sechshundert"] = 600,
        ["siebenhundert"] = 700,
        ["achthundert"] = 800,
        ["neunhundert"] = 900,
    };

    private static readonly Dictionary<string, int> StandaloneNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["null"] = 0,
        ["eins"] = 1,
        ["zwei"] = 2,
        ["drei"] = 3,
        ["vier"] = 4,
        ["fünf"] = 5,
        ["fuenf"] = 5,
        ["sechs"] = 6,
        ["sieben"] = 7,
        ["acht"] = 8,
        ["neun"] = 9,
        ["zehn"] = 10,
        ["elf"] = 11,
        ["zwölf"] = 12,
        ["zwoelf"] = 12,
        ["dreizehn"] = 13,
        ["vierzehn"] = 14,
        ["fünfzehn"] = 15,
        ["fuenfzehn"] = 15,
        ["sechzehn"] = 16,
        ["siebzehn"] = 17,
        ["achtzehn"] = 18,
        ["neunzehn"] = 19,
        ["zwanzig"] = 20,
        ["dreißig"] = 30,
        ["dreissig"] = 30,
        ["vierzig"] = 40,
        ["fünfzig"] = 50,
        ["fuenfzig"] = 50,
        ["sechzig"] = 60,
        ["siebzig"] = 70,
        ["achtzig"] = 80,
        ["neunzig"] = 90,
        ["hundert"] = 100,
        ["einhundert"] = 100,
    };

    private static readonly Dictionary<string, string> FractionalDigitMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["null"] = "0",
        ["ein"] = "1",
        ["eins"] = "1",
        ["zwei"] = "2",
        ["drei"] = "3",
        ["vier"] = "4",
        ["fünf"] = "5",
        ["fuenf"] = "5",
        ["sechs"] = "6",
        ["sieben"] = "7",
        ["acht"] = "8",
        ["neun"] = "9",
    };

    private static readonly Regex HundredsCompoundRegex = new(
        @"(?i)(?<![a-zäöüßA-ZÄÖÜ])((?:ein|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun)?hundert)((?:(?:ein|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun)und(?:zwanzig|dreißig|dreissig|vierzig|fünfzig|fuenfzig|sechzig|siebzig|achtzig|neunzig))|(?:elf|zwölf|zwoelf|dreizehn|vierzehn|fünfzehn|fuenfzehn|sechzehn|siebzehn|achtzehn|neunzehn|zehn)|(?:zwanzig|dreißig|dreissig|vierzig|fünfzig|fuenfzig|sechzig|siebzig|achtzig|neunzig)|(?:eins|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun))?(?![a-zäöüßA-ZÄÖÜ])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex CompoundTensRegex = new(
        @"(?i)(?<![a-zäöüßA-ZÄÖÜ])((?:ein|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun)und(?:zwanzig|dreißig|dreissig|vierzig|fünfzig|fuenfzig|sechzig|siebzig|achtzig|neunzig))(?![a-zäöüßA-ZÄÖÜ])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex DecimalSequenceRegex = new(
        @"(?i)(?<![a-zäöüßA-ZÄÖÜ])(-?(?:\d+|null|eins|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun))\s+komma\s+((?:(?:null|eins|ein|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun|\d+)\s*)+)(?![a-zäöüßA-ZÄÖÜ])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    private static readonly Regex StandaloneMinusNumberRegex = new(
        @"(?i)(?<![a-zäöüßA-ZÄÖÜ])minus\s+(-?\d+)(?![a-zäöüßA-ZÄÖÜ])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        MatchTimeout);

    /// <summary>
    /// Normalizes spoken German numbers in the given text.
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

        var result = text;

        // 1. Replace compounds: hundreds + remainder
        result = HundredsCompoundRegex.Replace(result, m =>
        {
            var hStr = m.Groups[1].Value;
            var rStr = m.Groups[2].Value;

            if (!Hundreds.TryGetValue(hStr, out var val))
            {
                return m.Value;
            }

            if (!string.IsNullOrEmpty(rStr))
            {
                if (TryEvaluateCompoundTen(rStr, out var compTen))
                {
                    val += compTen;
                }
                else if (Teens.TryGetValue(rStr, out var teen))
                {
                    val += teen;
                }
                else if (Tens.TryGetValue(rStr, out var ten))
                {
                    val += ten;
                }
                else if (Units.TryGetValue(rStr, out var unit))
                {
                    val += unit;
                }
            }

            return val.ToString(CultureInfo.InvariantCulture);
        });

        // 2. Replace compound tens (e.g. einundzwanzig)
        result = CompoundTensRegex.Replace(result, m =>
        {
            if (TryEvaluateCompoundTen(m.Value, out var val))
            {
                return val.ToString(CultureInfo.InvariantCulture);
            }
            return m.Value;
        });

        // 3. Parse decimal sequences introduced by komma
        result = DecimalSequenceRegex.Replace(result, m =>
        {
            var intPartStr = m.Groups[1].Value;
            var fracPartStr = m.Groups[2].Value;

            string normalizedIntPart;
            if (int.TryParse(intPartStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            {
                normalizedIntPart = intVal.ToString(CultureInfo.InvariantCulture);
            }
            else if (StandaloneNumbers.TryGetValue(intPartStr, out var wordVal))
            {
                normalizedIntPart = wordVal.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                normalizedIntPart = intPartStr;
            }

            var tokens = fracPartStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var fracSb = new StringBuilder();
            foreach (var token in tokens)
            {
                if (FractionalDigitMap.TryGetValue(token, out var digit))
                {
                    fracSb.Append(digit);
                }
                else if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    fracSb.Append(token);
                }
                else
                {
                    // Unrecognized fractional token -> keep original match
                    return m.Value;
                }
            }

            return $"{normalizedIntPart},{fracSb}";
        });

        // 4. Parse safe standalone number words
        foreach (var (word, number) in StandaloneNumbers)
        {
            var pattern = $@"(?i)(?<![a-zäöüßA-ZÄÖÜ]){Regex.Escape(word)}(?![a-zäöüßA-ZÄÖÜ])";
            result = Regex.Replace(result, pattern, number.ToString(CultureInfo.InvariantCulture), RegexOptions.CultureInvariant, MatchTimeout);
        }

        // 5. Replace leading minus phrase before numeric token (e.g. "minus 3,5" -> "-3,5", "minus 10" -> "-10")
        result = StandaloneMinusNumberRegex.Replace(result, "-$1");

        return result;
    }

    private static bool TryEvaluateCompoundTen(string input, out int value)
    {
        value = 0;
        var undIdx = input.IndexOf("und", StringComparison.OrdinalIgnoreCase);
        if (undIdx <= 0) return false;

        var unitStr = input[..undIdx];
        var tenStr = input[(undIdx + 3)..];

        if (Units.TryGetValue(unitStr, out var unitVal) && Tens.TryGetValue(tenStr, out var tenVal))
        {
            value = unitVal + tenVal;
            return true;
        }

        return false;
    }
}
