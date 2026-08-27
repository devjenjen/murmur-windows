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

        settings.HotkeyId.ShouldBe(AppSettings.DefaultHotkeyId);
        settings.HudPlacement.ShouldBe(AppSettings.DefaultHudPlacement);
        settings.SpeechModelId.ShouldBe("parakeet-v3");
        settings.RemoveFillers.ShouldBeTrue();
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
}
