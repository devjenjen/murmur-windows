using System.Text;
using Shouldly;
using Xunit;

namespace Murmur.Dictionary.Tests;

public sealed class DictionaryFileTests
{
    [Fact]
    public void Parses_terms_corrections_disabled_entries_and_comments()
    {
        var entries = DictionaryFile.Parse("""
            # comment

            Anthropic
            cloud code -> Claude Code
            # off: whisper flow -> Wispr Flow
            """);

        entries.Count.ShouldBe(3);
        entries[0].Kind.ShouldBe(EntryKind.Term);
        entries[0].Write.ShouldBe("Anthropic");
        entries[1].Kind.ShouldBe(EntryKind.Correction);
        entries[1].Hear.ShouldBe("cloud code");
        entries[1].Write.ShouldBe("Claude Code");
        entries[2].Kind.ShouldBe(EntryKind.Correction);
        entries[2].Hear.ShouldBe("whisper flow");
        entries[2].Write.ShouldBe("Wispr Flow");
        entries[2].IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Ignores_malformed_arrows_and_empty_sides()
    {
        DictionaryFile.Parse("-> output\ninput ->\na -> b -> c\nvalid").Select(e => e.Write)
            .ShouldBe(["valid"]);
    }

    [Fact]
    public void Preserves_nfc_text()
    {
        var decomposed = "Cafe\u0301";
        var entry = DictionaryFile.Parse(decomposed)[0];

        entry.Write.ShouldBe(decomposed);
        entry.Write.Normalize(NormalizationForm.FormC).ShouldBe("Café");
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        DictionaryFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            .ShouldBeEmpty();
    }

    [Fact]
    public void LoadOrCreate_seeds_common_proper_noun_corrections_once()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "dictionary.txt");

        var entries = DictionaryFile.LoadOrCreate(path);

        entries.ShouldContain(entry => entry.Hear == "cloud code" && entry.Write == "Claude Code");
        entries.ShouldContain(entry => entry.Hear == "git hub" && entry.Write == "GitHub");
        File.Exists(path).ShouldBeTrue();

        var secondLoad = DictionaryFile.LoadOrCreate(path);
        secondLoad.Count.ShouldBe(entries.Count);

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Save_round_trips_dictionary_entries_atomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "dictionary.txt");
        var entries = new[]
        {
            DictionaryEntry.Term("GitHub"),
            DictionaryEntry.Correction("cloud code", "Claude Code") with { IsEnabled = false }
        };

        DictionaryFile.Save(entries, path);

        var loaded = DictionaryFile.Load(path);
        loaded.Count.ShouldBe(2);
        loaded[0].Write.ShouldBe("GitHub");
        loaded[1].IsEnabled.ShouldBeFalse();
        loaded[1].Hear.ShouldBe("cloud code");

        Directory.Delete(directory, recursive: true);
    }
}
