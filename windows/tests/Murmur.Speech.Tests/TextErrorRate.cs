namespace Murmur.Speech.Tests;

using System.Text;

internal static class TextErrorRate
{
    public static double WordErrorRate(string reference, string hypothesis) =>
        ErrorRate(Words(reference), Words(hypothesis));

    public static double CharacterErrorRate(string reference, string hypothesis) =>
        ErrorRate(Characters(reference), Characters(hypothesis));

    private static double ErrorRate<T>(IReadOnlyList<T> reference, IReadOnlyList<T> hypothesis)
        where T : IEquatable<T>
    {
        if (reference.Count == 0) return hypothesis.Count == 0 ? 0d : 1d;

        var previous = new int[hypothesis.Count + 1];
        var current = new int[hypothesis.Count + 1];
        for (var j = 0; j <= hypothesis.Count; j++) previous[j] = j;

        for (var i = 1; i <= reference.Count; i++)
        {
            current[0] = i;
            for (var j = 1; j <= hypothesis.Count; j++)
            {
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (reference[i - 1].Equals(hypothesis[j - 1]) ? 0 : 1));
            }
            (previous, current) = (current, previous);
        }

        return (double)previous[hypothesis.Count] / reference.Count;
    }

    private static string[] Words(string text) => text
        .Normalize(NormalizationForm.FormC)
        .ToLowerInvariant()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static Rune[] Characters(string text) => text
        .Normalize(NormalizationForm.FormC)
        .ToLowerInvariant()
        .EnumerateRunes()
        .Where(rune => !Rune.IsWhiteSpace(rune))
        .ToArray();
}
