namespace Murmur.Core.Tests;

using System;
using System.IO;
using Murmur.Core.Settings;
using Shouldly;
using Xunit;

public class SettingsStoreTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-tests-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

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
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void Missing_File_Returns_Defaults()
    {
        using var temp = new TempDirectory();
        var actual = new SettingsStore(Path.Combine(temp.Path, "settings.json")).Load();
        actual.ShouldBe(AppSettings.Default);
        actual.StartWithWindows.ShouldBeFalse();
        actual.TextInsertionMode.ShouldBe(TextInsertionMode.AtCaret);
        actual.HistoryEnabled.ShouldBeFalse();
        actual.HistoryRetentionDays.ShouldBe(30);
        actual.HistoryMaxEntries.ShouldBe(1000);
    }

    [Fact]
    public void Save_Then_Load_Round_Trips()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));
        var expected = new AppSettings("endpoint-42", false, true).Normalize();
        store.Save(expected);
        store.Load().ShouldBe(expected);
    }

    [Fact]
    public void Invalid_Json_Is_Preserved_And_Defaulted()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{");
        new SettingsStore(path).Load().ShouldBe(AppSettings.Default);
        File.Exists(Path.Combine(temp.Path, "settings.invalid.json")).ShouldBeTrue();
    }

    [Fact]
    public void Load_Old_Settings_Fills_New_Defaults()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, """
            {"microphoneEndpointId":"mic-1","startWithWindows":true,"setupCompleted":true}
            """);

        var settings = new SettingsStore(path).Load();

        settings.StartWithWindows.ShouldBeTrue();
        settings.TextInsertionMode.ShouldBe(TextInsertionMode.DocumentEnd);
        settings.HistoryEnabled.ShouldBeTrue();
        settings.HistoryRetentionDays.ShouldBe(0);
        settings.HistoryMaxEntries.ShouldBe(0);
        settings.HotkeyId.ShouldBe(AppSettings.DefaultHotkeyId);
        settings.HudPlacement.ShouldBe(AppSettings.DefaultHudPlacement);
        settings.SpeechModelId.ShouldBe("parakeet-v3");
        settings.RemoveFillers.ShouldBeTrue();
    }

    [Fact]
    public void Explicit_Text_Insertion_Mode_Roundtrips()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new SettingsStore(path);
        var expected = AppSettings.Default with { TextInsertionMode = TextInsertionMode.DocumentEnd };

        store.Save(expected);

        store.Load().TextInsertionMode.ShouldBe(TextInsertionMode.DocumentEnd);
    }

    [Fact]
    public void Unknown_Text_Insertion_Mode_Uses_Legacy_Safe_Default()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{\"textInsertionMode\":99}");

        new SettingsStore(path).Load().TextInsertionMode.ShouldBe(TextInsertionMode.DocumentEnd);
    }

    [Fact]
    public void Explicit_History_Policy_Roundtrips()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var expected = AppSettings.Default with
        {
            HistoryEnabled = true,
            HistoryRetentionDays = 7,
            HistoryMaxEntries = 0
        };

        var store = new SettingsStore(path);
        store.Save(expected);

        store.Load().ShouldBe(expected);
    }

    [Fact]
    public void Invalid_History_Policy_Uses_Conservative_Defaults()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{\"historyEnabled\":true,\"historyRetentionDays\":-1,\"historyMaxEntries\":999999}");

        var settings = new SettingsStore(path).Load();

        settings.HistoryEnabled.ShouldBeTrue();
        settings.HistoryRetentionDays.ShouldBe(30);
        settings.HistoryMaxEntries.ShouldBe(1000);
    }

    [Fact]
    public void Save_And_Load_Preserves_New_Choices()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var expected = AppSettings.Default with
        {
            HotkeyId = "vk:117",
            HudPlacement = "top-right",
            SpeechModelId = "parakeet-v3",
            RemoveFillers = false
        };

        var store = new SettingsStore(path);
        store.Save(expected);

        store.Load().ShouldBe(expected);
    }

    [Fact]
    public void Save_Then_Load_Preserves_German_Language()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));
        var expected = AppSettings.Default with { Language = "de" };

        store.Save(expected);

        store.Load().Language.ShouldBe("de");
    }

    [Fact]
    public void Load_Old_Settings_Defaults_Language_To_English()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{\"microphoneEndpointId\":\"mic-1\"}");

        new SettingsStore(path).Load().Language.ShouldBe(AppSettings.DefaultLanguage);
    }

    [Fact]
    public void Load_Unknown_Language_Falls_Back_To_English()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{\"language\":\"fr\"}");

        new SettingsStore(path).Load().Language.ShouldBe(AppSettings.DefaultLanguage);
    }

    [Fact]
    public void Save_failure_preserves_previous_settings()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new SettingsStore(path);
        store.Save(AppSettings.Default with { SetupCompleted = true });
        var previous = File.ReadAllText(path);

        var failingStore = new SettingsStore(
            path,
            File.ReadAllText,
            (_, _, _) => throw new IOException("replace failed"));

        Should.Throw<IOException>(() => failingStore.Save(AppSettings.Default));
        File.ReadAllText(path).ShouldBe(previous);
        Directory.GetFiles(temp.Path, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void Load_access_failure_returns_defaults_and_exposes_diagnostic()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{}");
        var store = new SettingsStore(
            path,
            _ => throw new UnauthorizedAccessException("locked"),
            (source, destination, overwrite) => File.Move(source, destination, overwrite));

        store.Load().ShouldBe(AppSettings.Default);
        store.LastLoadError.ShouldBeOfType<UnauthorizedAccessException>();
        File.Exists(Path.Combine(temp.Path, "settings.invalid.json")).ShouldBeFalse();
    }
}
