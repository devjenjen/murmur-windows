using System.Text;
using System.Text.RegularExpressions;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Formatting;

/// <summary>
/// Deterministic text cleanup: strips fillers, formats spoken punctuation,
/// collapses whitespace, and capitalizes sentences.
/// </summary>
public sealed class RuleBasedFormatter : ITextFormatter
{
    internal const char ProtectedLetterMarker = '\u200C';

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly string[] Fillers =
        ["um", "uh", "erm", "uhm", "hmm", "mhm", "äh", "ähm", "ehm", "hm"];

    private static readonly (string Phrase, string Replacement)[] SpokenPunctuation =
    [
        ("new paragraph", "\n\n"),
        ("new line", "\n"),
        ("open paren", " ("),
        ("close paren", ") "),
    ];

    private readonly bool _removeFillers;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleBasedFormatter"/> class.
    /// </summary>
    /// <param name="removeFillers">Whether filler words are removed.</param>
    public RuleBasedFormatter(bool removeFillers = true)
    {
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

        if (_removeFillers)
        {
            text = StripFillers(text);
        }

        text = ApplySpokenPunctuation(text);
        text = CollapseWhitespace(text);
        text = CapitalizeSentences(text);
        text = EnsureTerminalPunctuation(text);

        return text;
    }

    private static string StripFillers(string text)
    {
        var result = text;
        foreach (var filler in Fillers)
        {
            var pattern = $@"(?i)(?<![\w']){Regex.Escape(filler)}\b,?";
            result = Regex.Replace(result, pattern, "", RegexOptions.CultureInvariant, MatchTimeout);
        }
        return result;
    }

    private static string ApplySpokenPunctuation(string text)
    {
        var result = text;
        foreach (var (phrase, replacement) in SpokenPunctuation)
        {
            var pattern = $@"(?i)\b{Regex.Escape(phrase)}\b";
            result = Regex.Replace(result, pattern, replacement, RegexOptions.CultureInvariant, MatchTimeout);
        }
        return result;
    }

    private static string CollapseWhitespace(string text)
    {
        var result = Regex.Replace(text, @"[ \t]+", " ", RegexOptions.CultureInvariant, MatchTimeout);
        result = Regex.Replace(result, @" +([,.!?;:])", "$1", RegexOptions.CultureInvariant, MatchTimeout);
        result = Regex.Replace(result, @"[ \t]*\n[ \t]*", "\n", RegexOptions.CultureInvariant, MatchTimeout);
        result = Regex.Replace(result, @"\n{3,}", "\n\n", RegexOptions.CultureInvariant, MatchTimeout);
        return result.Trim();
    }

    private static string CapitalizeSentences(string text)
    {
        var sb = new StringBuilder(text.Length);
        var capitalizeNext = true;
        var protectNextLetter = false;

        foreach (var c in text)
        {
            if (c == ProtectedLetterMarker)
            {
                protectNextLetter = true;
                continue;
            }

            if (capitalizeNext && char.IsLetter(c))
            {
                if (protectNextLetter)
                {
                    sb.Append(c);
                    protectNextLetter = false;
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
                if (c is '.' or '!' or '?')
                {
                    capitalizeNext = true;
                }
            }
        }
        return sb.ToString();
    }

    private static string EnsureTerminalPunctuation(string text)
    {
        if (text.Length == 0) return text;
        var last = text[^1];
        if (char.IsLetterOrDigit(last))
        {
            return text + ".";
        }
        return text;
    }
}
