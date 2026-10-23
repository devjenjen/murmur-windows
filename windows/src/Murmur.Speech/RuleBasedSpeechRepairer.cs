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
    private static readonly string[] Prepositions = new[]
    {
        "am", "um", "im", "in", "an", "von", "vom", "ab", "bis", "auf", "für", "fuer",
        "zu", "zum", "zur", "nach", "vor", "bei", "beim", "mit",
        "at", "on", "in", "to", "for", "from", "by", "until", "with"
    };

    private static readonly Regex WordRegex = new(
        @"\b[a-zA-Z0-9äöüÄÖÜß\-_']+\b",
        RegexOptions.Compiled);

    private static readonly Regex ContinuationBoundaryRegex = new(
        $@"[,.!?;:\r\n…]|\b(?:und|oder|and|or)\b|\b(?<preposition>{string.Join("|", Array.ConvertAll(Prepositions, prep => Regex.Escape(prep)))})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MarkerRegex = new(
        @"(?:(?:\s*(?:\.{2,}|…|[\.,;\-–—]))\s*\b(?<marker>warte\s+(?:mal|kurz|eben|einen\s+moment|(?:eine\s+)?sekunde))\b|(?:\s*(?:\.{2,}|…|[\.,;\-–—]))?\s*\b(?<marker>ach\s+nee|ach\s+ne|ach\s+nein|ich\s+meinte|oder\s+vielmehr|oder\s+besser(?:\s+gesagt)?|no\s+wait|i\s+mean|or\s+rather|scratch\s+that)\b)[\s,:]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NichtSondernRegex = new(
        @"\bnicht\s+(?!nur\b|bloß\b)(?<rep>[^\.\!\?;\r\n]+?)\s*,?\s*sondern\s+(?<alt>[^\.,;!?\r\n]+)",
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

    private static readonly Regex WeekdayRegex = new(
        @"\b(?:Montag|Dienstag|Mittwoch|Donnerstag|Freitag|Samstag|Sonntag|Sonnabend|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RelativeTimeAdverbRegex = new(
        @"\b(?:vorgestern|übermorgen|gestern|heute|morgen|yesterday|today|tomorrow|tonight)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClockTimeRegex = new(
        @"\b(?:\d{1,2}(?::\d{2})?\s*(?:Uhr|am|pm|a\.m\.|p\.m\.)|\d{1,2}\s+Uhr)(?!\w)",
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

            // Only repair when both reparandum and alteration belong to a recognized category (e.g. date/time/number/prep phrase)
            (_, _, bool knownCategory) = FindReparandum(rep, alt);
            if (knownCategory)
            {
                // If alt doesn't have preposition but rep does (e.g. rep="am Dienstag", alt="Mittwoch"),
                // keep preposition
                string prep = ExtractPreposition(rep);
                if (!string.IsNullOrEmpty(prep) && string.IsNullOrEmpty(ExtractPreposition(alt)))
                {
                    alt = prep + " " + alt;
                }

                string before = text[..nichtMatch.Index];
                string after = text[(nichtMatch.Index + nichtMatch.Length)..];
                return before + alt + after;
            }
        }

        // 2. Check conversational correction markers
        foreach (Match markerMatch in MarkerRegex.Matches(text))
        {
            string repaired = ApplyMarkerRepair(text, markerMatch);
            if (!string.Equals(repaired, text, StringComparison.Ordinal))
            {
                return repaired;
            }
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

        string fullBefore = text[..matchIndex].TrimEnd();
        if (string.IsNullOrWhiteSpace(fullBefore))
        {
            return text;
        }

        // Constrain reparandum search to the immediately preceding sentence fragment
        var sentenceBreakMatches = Regex.Matches(fullBefore, @"[.!?;\r\n…]+\s+");
        string priorSentences;
        string textBefore;
        if (sentenceBreakMatches.Count > 0)
        {
            var lastBreak = sentenceBreakMatches[^1];
            int fragmentStart = lastBreak.Index + lastBreak.Length;
            priorSentences = fullBefore[..fragmentStart];
            textBefore = fullBefore[fragmentStart..].TrimEnd();
        }
        else
        {
            priorSentences = string.Empty;
            textBefore = fullBefore;
        }

        if (string.IsNullOrWhiteSpace(textBefore))
        {
            return text;
        }

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

        var nextMarkerMatch = MarkerRegex.Match(textAfterAll);
        if (nextMarkerMatch.Success && nextMarkerMatch.Index > 0)
        {
            string beforeNextMarker = textAfterAll[..nextMarkerMatch.Index];
            int commaIndex = beforeNextMarker.LastIndexOf(',');
            if (commaIndex >= 0)
            {
                alteration = beforeNextMarker[..commaIndex].Trim();
                punctuation = ",";
                remainder = textAfterAll[(commaIndex + 1)..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(alteration))
        {
            return text;
        }

        // Check for German separable verb prefix at end of textBefore
        var prefixMatch = Regex.Match(textBefore, @"\s+(?<prefix>[a-zA-ZäöüÄÖÜß]+)$");
        string trailingParticle = "";
        string textBeforeTrimmed = textBefore;

        if (prefixMatch.Success && SeparablePrefixes.Contains(prefixMatch.Groups["prefix"].Value))
        {
            string particle = prefixMatch.Groups["prefix"].Value;
            if (!Regex.IsMatch(alteration, $@"\b{Regex.Escape(particle)}$", RegexOptions.IgnoreCase))
            {
                trailingParticle = particle;
                textBeforeTrimmed = textBefore[..prefixMatch.Index].TrimEnd();
            }
        }

        // Find reparandum in textBeforeTrimmed
        (int repIndex, int repLength, bool knownCategory) = FindReparandum(textBeforeTrimmed, alteration);

        if (repIndex < 0 || repLength <= 0)
        {
            bool separatorHasSentencePeriod = Regex.IsMatch(markerMatch.Value, @"(?<!\.)\.(?!\.)");
            if (knownCategory || separatorHasSentencePeriod)
            {
                return text;
            }

            var lastWordMatch = Regex.Match(textBeforeTrimmed, @"\b[a-zA-Z0-9äöüÄÖÜß\-_']+\b(?=[^\w]*$)");
            if (!lastWordMatch.Success)
            {
                return text;
            }

            string altWord = alteration.Trim();
            string lastWord = lastWordMatch.Value;
            // Only fall back to single-word replacement if both are capitalized proper nouns (e.g. Alice -> Bob, Jack -> Jill)
            // and neither is a common lower-case word or pronoun (e.g. "I love you, I mean it")
            if (WordRegex.Count(altWord) == 1 &&
                char.IsUpper(altWord[0]) &&
                char.IsUpper(lastWord[0]))
            {
                repIndex = lastWordMatch.Index;
                repLength = lastWordMatch.Length;
            }
            else
            {
                return text;
            }
        }

        string before = textBeforeTrimmed[..repIndex];
        string matchedRep = textBeforeTrimmed.Substring(repIndex, repLength);
        string after = textBeforeTrimmed[(repIndex + repLength)..];

        // If matchedRep had preposition but alteration does not, preserve preposition
        string prep = ExtractPreposition(matchedRep);
        if (!string.IsNullOrEmpty(prep) && string.IsNullOrEmpty(ExtractPreposition(alteration)))
        {
            alteration = prep + " " + alteration;
        }

        // Clean any hesitation/filler word immediately before reparandum
        before = StripTrailingFillers(before);

        // Prevent duplicate or conflicting prepositions when alteration already includes a preposition
        string altPrep = ExtractPreposition(alteration);
        if (!string.IsNullOrEmpty(altPrep))
        {
            string trimmedBefore = before.TrimEnd();
            string beforePrep = ExtractTrailingPreposition(trimmedBefore);
            if (!string.IsNullOrEmpty(beforePrep))
            {
                before = trimmedBefore[..^beforePrep.Length].TrimEnd();
                if (!string.IsNullOrEmpty(before))
                {
                    before += " ";
                }
            }
        }

        // Consume only a validated phrase at the start of the original suffix.
        if (!TryGetConsumedSuffixLength(alteration, after, knownCategory, out int consumedSuffixLength))
        {
            return text;
        }
        after = after[consumedSuffixLength..];

        // Build result
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(priorSentences))
        {
            sb.Append(priorSentences);
        }

        if (!string.IsNullOrEmpty(before))
        {
            sb.Append(before);
            if (!before.EndsWith(' '))
            {
                sb.Append(' ');
            }
        }

        sb.Append(alteration);

        if (!string.IsNullOrWhiteSpace(after))
        {
            if (!after.StartsWith(' '))
            {
                sb.Append(' ');
            }
            sb.Append(after.TrimEnd());
        }

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

    private static bool TryGetConsumedSuffixLength(
        string alteration,
        string after,
        bool knownCategory,
        out int consumedLength)
    {
        consumedLength = 0;
        if (string.IsNullOrWhiteSpace(after))
        {
            return true;
        }

        Match valueMatch = WeekdayRegex.Match(alteration);
        bool isNumber = false;
        if (valueMatch.Success)
        {
            if (RelativeTimeAdverbRegex.IsMatch(alteration) ||
                ClockTimeRegex.IsMatch(alteration) ||
                NumberWordRegex.IsMatch(alteration))
            {
                return false;
            }
        }
        else
        {
            valueMatch = RelativeTimeAdverbRegex.Match(alteration);
            if (valueMatch.Success)
            {
                if (ClockTimeRegex.IsMatch(alteration) || NumberWordRegex.IsMatch(alteration))
                {
                    return false;
                }
            }
            else
            {
                valueMatch = ClockTimeRegex.Match(alteration);
                if (!valueMatch.Success)
                {
                    var numberMatches = NumberWordRegex.Matches(alteration);
                    if (numberMatches.Count != 1)
                    {
                        return !knownCategory && WordRegex.Count(alteration) == 1;
                    }

                    valueMatch = numberMatches[0];
                    isNumber = true;
                }
            }
        }

        string repeatedContinuation = alteration[(valueMatch.Index + valueMatch.Length)..].Trim();
        if (string.IsNullOrEmpty(repeatedContinuation))
        {
            return true;
        }

        int afterOffset = after.Length - after.TrimStart().Length;
        string trimmedAfter = after.TrimStart();
        int leadingPhraseLength = FindLeadingPhraseLength(trimmedAfter);
        string leadingPhrase = trimmedAfter[..leadingPhraseLength].TrimEnd();
        if (string.Equals(leadingPhrase, repeatedContinuation, StringComparison.OrdinalIgnoreCase))
        {
            consumedLength = afterOffset + leadingPhrase.Length;
            return true;
        }

        return isNumber && TryConsumeNominalContinuation(alteration, after, valueMatch, out consumedLength);
    }

    private static bool TryConsumeNominalContinuation(
        string alteration,
        string after,
        Match numberMatch,
        out int consumedLength)
    {
        consumedLength = 0;
        string trimmedAlteration = alteration.Trim();
        var numberMatches = NumberWordRegex.Matches(trimmedAlteration);
        if (numberMatches.Count != 1 || numberMatch.Index != 0)
        {
            return false;
        }

        string replacementPhrase = trimmedAlteration[(numberMatch.Index + numberMatch.Length)..].Trim();
        if (string.IsNullOrEmpty(replacementPhrase) || ContinuationBoundaryRegex.IsMatch(replacementPhrase))
        {
            return false;
        }

        var replacementWords = WordRegex.Matches(replacementPhrase);
        int afterOffset = after.Length - after.TrimStart().Length;
        string trimmedAfter = after.TrimStart();
        int leadingPhraseLength = FindLeadingPhraseLength(trimmedAfter);
        string leadingPhrase = trimmedAfter[..leadingPhraseLength].TrimEnd();
        var originalWords = WordRegex.Matches(leadingPhrase);
        if (replacementWords.Count == 0 || originalWords.Count == 0 ||
            !string.IsNullOrEmpty(ExtractPreposition(leadingPhrase)) ||
            !string.Equals(replacementWords[^1].Value, originalWords[^1].Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        consumedLength = afterOffset + leadingPhrase.Length;
        return true;
    }

    private static int FindLeadingPhraseLength(string text)
    {
        foreach (Match boundary in ContinuationBoundaryRegex.Matches(text))
        {
            if (boundary.Index == 0 && boundary.Groups["preposition"].Success)
            {
                continue;
            }

            return boundary.Index;
        }

        return text.Length;
    }

    private static (int index, int length, bool knownCategory) FindReparandum(string textBefore, string alteration)
    {
        // 1. Weekday match
        if (WeekdayRegex.IsMatch(alteration))
        {
            var matches = WeekdayRegex.Matches(textBefore);
            if (matches.Count > 0)
            {
                var last = matches[^1];
                return (last.Index, last.Length, true);
            }
            return (-1, 0, true);
        }

        // 2. Relative time adverb match
        if (RelativeTimeAdverbRegex.IsMatch(alteration))
        {
            var matches = RelativeTimeAdverbRegex.Matches(textBefore);
            if (matches.Count > 0)
            {
                var last = matches[^1];
                return (last.Index, last.Length, true);
            }
            return (-1, 0, true);
        }

        // 3. Clock time match
        if (ClockTimeRegex.IsMatch(alteration))
        {
            var matches = ClockTimeRegex.Matches(textBefore);
            if (matches.Count > 0)
            {
                var last = matches[^1];
                return (last.Index, last.Length, true);
            }
            return (-1, 0, true);
        }

        // 4. Number word match
        if (NumberWordRegex.IsMatch(alteration))
        {
            var matches = NumberWordRegex.Matches(textBefore);
            if (matches.Count > 0)
            {
                var last = matches[^1];
                return (last.Index, last.Length, true);
            }
            return (-1, 0, true);
        }

        // 5. Prepositional phrase match
        string altPrep = ExtractPreposition(alteration);
        if (!string.IsNullOrEmpty(altPrep))
        {
            string altRest = alteration[altPrep.Length..].Trim();
            int wordCount = Math.Max(1, Regex.Count(altRest, @"\b[a-zA-Z0-9äöüÄÖÜß\-_']+\b"));
            var prepPhraseRegex = new Regex(
                $@"\b{Regex.Escape(altPrep)}(?:\s+[a-zA-Z0-9äöüÄÖÜß\-_']+){{1,{wordCount}}}\b",
                RegexOptions.IgnoreCase);
            var matches = prepPhraseRegex.Matches(textBefore);
            if (matches.Count > 0)
            {
                var last = matches[^1];
                return (last.Index, last.Length, true);
            }
            return (-1, 0, true);
        }

        return (-1, 0, false);
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
