using Murmur.Core.History;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class HistoryStoreTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"murmur-history-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static DictationHistoryEntry Entry(Guid id, DateTimeOffset timestamp, string text) =>
        new(id, timestamp, "Parakeet v3", 1500, 240, text);

    [Fact]
    public void Append_And_Load_Round_Trip_All_Fields()
    {
        using var temp = new TempDirectory();
        var store = new HistoryStore(System.IO.Path.Combine(temp.Path, "history.jsonl"));
        var expected = Entry(Guid.NewGuid(), new DateTimeOffset(2026, 8, 21, 20, 15, 0, TimeSpan.FromHours(2)), "Hallo Welt.");

        store.Append(expected);

        store.Load(out var skipped).ShouldBe([expected]);
        skipped.ShouldBe(0);
    }

    [Fact]
    public void Load_Returns_Newest_First_And_Skips_Bad_Lines()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "history.jsonl");
        var store = new HistoryStore(path);
        var older = Entry(Guid.NewGuid(), new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.FromHours(2)), "Älter.");
        var newer = Entry(Guid.NewGuid(), new DateTimeOffset(2026, 8, 21, 19, 0, 0, TimeSpan.FromHours(2)), "Neuer.");

        store.Append(older);
        File.AppendAllText(path, Environment.NewLine + "{broken" + Environment.NewLine);
        store.Append(newer);

        store.Load(out var skipped).ShouldBe([newer, older]);
        skipped.ShouldBe(1);
    }

    [Fact]
    public void Delete_Removes_Only_Selected_Entry()
    {
        using var temp = new TempDirectory();
        var store = new HistoryStore(System.IO.Path.Combine(temp.Path, "history.jsonl"));
        var keep = Entry(Guid.NewGuid(), DateTimeOffset.Now.AddMinutes(-1), "Behalten.");
        var remove = Entry(Guid.NewGuid(), DateTimeOffset.Now, "Löschen.");
        store.Append(keep);
        store.Append(remove);

        store.Delete(remove.Id);

        store.Load(out _).ShouldBe([keep]);
        Directory.GetFiles(temp.Path, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void Clear_Removes_All_Entries_And_Leaves_No_Temporary_File()
    {
        using var temp = new TempDirectory();
        var store = new HistoryStore(System.IO.Path.Combine(temp.Path, "history.jsonl"));
        store.Append(Entry(Guid.NewGuid(), DateTimeOffset.Now, "Text."));

        store.Clear();

        store.Load(out _).ShouldBeEmpty();
        Directory.GetFiles(temp.Path, "*.tmp").ShouldBeEmpty();
    }
}
