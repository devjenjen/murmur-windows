namespace Murmur.Speech;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Murmur.Core.Abstractions;

/// <summary>
/// Deterministic rule-based speech repair engine that eliminates conversational self-corrections,
/// disfluencies, and hesitation markers.
/// </summary>
public sealed class RuleBasedSpeechRepairer : ISpeechRepairer
{
    private static readonly Regex MarkerRegex = new(
        @"(?:(?:[\.,;\-–—]|\.{3}|…)\s*\b(?<marker>warte\s+(?:mal|kurz|eben|einen\s+moment|(?:eine\s+)?sekunde))\b|(?:[\.,;\-–—]|\.{3}|…)?\s*\b(?<marker>ach\s+nee|ach\s+ne|ach\s+nein|ich\s+meinte|oder\s+vielmehr|oder\s+besser(?:\s+gesagt)?|ach\s+ja|no\s+wait|i\s+mean|or\s+rather|scratch\s+that)\b)[\s,:]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NichtSondernRegex = new(
        @"\bnicht\s+(?!nur\b|bloß\b)(?<rep>.+?)\s*,?\s*sondern\s+(?<alt>[^\.,;!?]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HesitationRegex = new(
        @",?\s*\b(ähm|äh|uh)\b,?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SeparablePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ab", "an", "auf", "aus", "bei", "ein", "fest", "fort", "her", "hin",
        "los", "mit", "nach", "vor", "vorbei", "weg", "weiter", "zu", "zurück", "zusammen"
    };

    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ja", "halt", "ähm", "äh", "eben", "so", "also", "quasi", "sozusagen", "uh", "er", "hm"
    };

    private static readonly string[] Weekdays = new[]
    {
        "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag", "Sonnabend",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    };

    private static readonly string[] RelativeTimeAdverbs = new[]
    {
        "vorgestern", "gestern", "heute", "morgen", "übermorgen",
        "yesterday", "today", "tomorrow", "tonight"
    };

    private static readonly Regex ClockTimeRegex = new(
        @"\b(?:\d{1,2}(?::\d{2})?\s*(?:Uhr|am|pm|a\.m\.|p\.m\.)|\d{1,2}\s+Uhr)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberWordRegex = new(
        @"\b(?:\d+|eins|zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|elf|zwölf|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public Task<string> RepairAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rawTranscript))
        {
            return Task.FromResult(rawTranscript);
        }

        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            return Task.FromResult(rawTranscript);
        }

        string current = rawTranscript;
        for (int i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string next = RepairPass(current);
            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                break;
            }
            current = next;
        }

        return Task.FromResult(current);
    }

    private static string RepairPass(string text)
    {
        // 1. Check "nicht X sondern Y" pattern
        var nichtMatch = NichtSondernRegex.Match(text);
        if (nichtMatch.Success)
        {
            string rep = nichtMatch.Groups["rep"].Value.Trim();
            string alt = nichtMatch.Groups["alt"].Value.Trim();

            // If alt doesn't have preposition but rep does (e.g. rep="am Dienstag", alt="Mittwoch"),
            // keep preposition
            string prep = ExtractPreposition(rep);
            if (!string.IsNullOrEmpty(prep) && !alt.StartsWith(prep, StringComparison.OrdinalIgnoreCase))
            {
                alt = prep + " " + alt;
            }

            string before = text[..nichtMatch.Index];
            string after = text[(nichtMatch.Index + nichtMatch.Length)..];
            return before + alt + after;
        }

        // 2. Check conversational correction markers
        var markerMatch = MarkerRegex.Match(text);
        if (markerMatch.Success)
        {
            return ApplyMarkerRepair(text, markerMatch);
        }

        // 3. Strip isolated hesitation disfluencies if present
        if (HesitationRegex.IsMatch(text))
        {
            string cleaned = HesitationRegex.Replace(text, " ");
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
            cleaned = Regex.Replace(cleaned, @"\s+([,\.!?;:])", "$1");
            return cleaned.Trim();
        }

        return text;
    }

    private static string ApplyMarkerRepair(string text, Match markerMatch)
    {
        int matchIndex = markerMatch.Index;
        int matchLength = markerMatch.Length;

        string textBefore = text[..matchIndex].TrimEnd();
        string textAfterAll = text[(matchIndex + matchLength)..].TrimStart();

        // Parse textAfterAll into alteration, terminal punctuation, and remainder
        var punctMatch = Regex.Match(textAfterAll, @"[\.!?]");
        string alteration;
        string punctuation;
        string remainder;

        if (punctMatch.Success)
        {
            alteration = textAfterAll[..punctMatch.Index].Trim();
            punctuation = punctMatch.Value;
            remainder = textAfterAll[(punctMatch.Index + punctMatch.Length)..].Trim();
        }
        else
        {
            var commaMatch = Regex.Match(textAfterAll, @",");
            if (commaMatch.Success && commaMatch.Index > 0)
            {
                alteration = textAfterAll[..commaMatch.Index].Trim();
                punctuation = "";
                remainder = textAfterAll[commaMatch.Index..].Trim();
            }
            else
            {
                alteration = textAfterAll.Trim();
                punctuation = "";
                remainder = "";
            }
        }

        if (string.IsNullOrWhiteSpace(alteration))
        {
            return textBefore + punctuation + (string.IsNullOrEmpty(remainder) ? "" : " " + remainder);
        }

        // Check for German separable verb prefix at end of textBefore
        var prefixMatch = Regex.Match(textBefore, @"\s+(?<prefix>[a-zA-ZäöüÄÖÜß]+)$");
        string trailingParticle = "";
        string textBeforeTrimmed = textBefore;

        if (prefixMatch.Success && SeparablePrefixes.Contains(prefixMatch.Groups["prefix"].Value))
        {
            string particle = prefixMatch.Groups["prefix"].Value;
            if (!alteration.EndsWith(particle, StringComparison.OrdinalIgnoreCase))
            {
                trailingParticle = particle;
                textBeforeTrimmed = textBefore[..prefixMatch.Index].TrimEnd();
            }
        }

        // Find reparandum in textBeforeTrimmed
        (int repIndex, int repLength) = FindReparandum(textBeforeTrimmed, alteration);

        string prefix;
        if (repIndex >= 0 && repLength > 0)
        {
            prefix = textBeforeTrimmed[..repIndex];
            string matchedRep = textBeforeTrimmed.Substring(repIndex, repLength);

            // If matchedRep had preposition but alteration does not, preserve preposition
            string prep = ExtractPreposition(matchedRep);
            if (!string.IsNullOrEmpty(prep) && !alteration.StartsWith(prep, StringComparison.OrdinalIgnoreCase))
            {
                alteration = prep + " " + alteration;
            }
        }
        else
        {
            // Default: replace last token
            var lastWordMatch = Regex.Match(textBeforeTrimmed, @"\b[a-zA-Z0-9äöüÄÖÜß\-_']+\b(?=[^\w]*$)");
            if (lastWordMatch.Success)
            {
                prefix = textBeforeTrimmed[..lastWordMatch.Index];
            }
            else
            {
                prefix = textBeforeTrimmed;
            }
        }

        // Clean any hesitation/filler word immediately before reparandum
        prefix = StripTrailingFillers(prefix);

        // Prevent duplicate or conflicting prepositions when alteration already includes a preposition
        // (e.g. prefix="Wir treffen uns am ", alteration="am Mittwoch" -> "Wir treffen uns am Mittwoch")
        string altPrep = ExtractPreposition(alteration);
        if (!string.IsNullOrEmpty(altPrep))
        {
            string trimmedPrefix = prefix.TrimEnd();
            string prefixPrep = ExtractTrailingPreposition(trimmedPrefix);
            if (!string.IsNullOrEmpty(prefixPrep))
            {
                prefix = trimmedPrefix[..^prefixPrep.Length].TrimEnd();
                if (!string.IsNullOrEmpty(prefix))
                {
                    prefix += " ";
                }
            }
        }

        // Build result
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(prefix))
        {
            sb.Append(prefix);
            if (!prefix.EndsWith(' '))
            {
                sb.Append(' ');
            }
        }

        sb.Append(alteration);

        if (!string.IsNullOrEmpty(trailingParticle))
        {
            sb.Append(' ');
            sb.Append(trailingParticle);
        }

        if (!string.IsNullOrEmpty(punctuation))
        {
            sb.Append(punctuation);
        }

        if (!string.IsNullOrEmpty(remainder))
        {
            sb.Append(' ');
            sb.Append(remainder);
        }

        return sb.ToString();
    }

    private static (int index, int length) FindReparandum(string textBefore, string alteration)
    {
        // 1. Weekday match
        foreach (var weekday in Weekdays)
        {
            if (alteration.Contains(weekday, StringComparison.OrdinalIgnoreCase))
            {
                // Find last weekday in textBefore
                int bestIndex = -1;
                int bestLen = 0;
                foreach (var w in Weekdays)
                {
                    int idx = textBefore.LastIndexOf(w, StringComparison.OrdinalIgnoreCase);
                    if (idx > bestIndex)
                    {
                        bestIndex = idx;
                        bestLen = w.Length;
                    }
                }
                if (bestIndex >= 0)
                {
                    return (bestIndex, bestLen);
                }
            }
        }

        // 2. Relative time adverb match
        foreach (var adv in RelativeTimeAdverbs)
        {
            if (alteration.Contains(adv, StringComparison.OrdinalIgnoreCase))
            {
                int bestIndex = -1;
                int bestLen = 0;
                foreach (var a in RelativeTimeAdverbs)
                {
                    int idx = textBefore.LastIndexOf(a, StringComparison.OrdinalIgnoreCase);
                    if (idx > bestIndex)
                    {
                        bestIndex = idx;
                        bestLen = a.Length;
                    }
                }
                if (bestIndex >= 0)
                {
                    return (bestIndex, bestLen);
                }
            }
        }

        // 3. Clock time match
        if (ClockTimeRegex.IsMatch(alteration))
        {
            var matches = ClockTimeRegex.Matches(textBefore);
            if (matches.Count > 0)
            {
                var last = matches[^1];
                return (last.Index, last.Length);
            }
        }

        // 4. Number word match
        if (NumberWordRegex.IsMatch(alteration))
        {
            var matches = NumberWordRegex.Matches(textBefore);
            if (matches.Count > 0)
            {
                var last = matches[^1];
                return (last.Index, last.Length);
            }
        }

        // 5. Prepositional phrase match
        string altPrep = ExtractPreposition(alteration);
        if (!string.IsNullOrEmpty(altPrep))
        {
            int idx = textBefore.LastIndexOf(altPrep, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return (idx, textBefore.Length - idx);
            }
        }

        return (-1, 0);
    }

    private static string StripTrailingFillers(string prefix)
    {
        string trimmed = prefix.TrimEnd();
        var fillerMatch = Regex.Match(trimmed, @"\b(?<filler>[a-zA-ZäöüÄÖÜß]+)$");
        if (fillerMatch.Success && Fillers.Contains(fillerMatch.Groups["filler"].Value))
        {
            trimmed = trimmed[..fillerMatch.Index].TrimEnd();
            return trimmed;
        }

        return prefix;
    }

    private static readonly string[] Prepositions = new[]
    {
        "am", "um", "im", "in", "an", "von", "vom", "ab", "bis", "auf", "für", "fuer",
        "zu", "zum", "zur", "nach", "vor", "bei", "beim", "mit",
        "at", "on", "in", "to", "for", "from", "by", "until", "with"
    };

    private static string ExtractPreposition(string phrase)
    {
        foreach (var prep in Prepositions)
        {
            if (phrase.StartsWith(prep + " ", StringComparison.OrdinalIgnoreCase))
            {
                return prep;
            }
        }
        return string.Empty;
    }

    private static string ExtractTrailingPreposition(string text)
    {
        string trimmed = text.TrimEnd();
        foreach (var prep in Prepositions)
        {
            if (trimmed.EndsWith(" " + prep, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, prep, StringComparison.OrdinalIgnoreCase))
            {
                return prep;
            }
        }
        return string.Empty;
    }
}
