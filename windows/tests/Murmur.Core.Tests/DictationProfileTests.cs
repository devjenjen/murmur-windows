using System.Text.Json;
using Murmur.Core.Formatting;
using Murmur.Core.Settings;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class DictationProfileTests
{
    [Fact]
    public void Default_Settings_Normalize_Contains_Standard_And_Math_Profiles()
    {
        var settings = AppSettings.Default.Normalize();

        settings.Profiles.Count.ShouldBe(2);
        settings.Profiles[0].Id.ShouldBe("standard");
        settings.Profiles[0].FormatterKind.ShouldBe(ProfileFormatterKind.Standard);
        settings.Profiles[1].Id.ShouldBe("math");
        settings.Profiles[1].FormatterKind.ShouldBe(ProfileFormatterKind.Math);
        settings.ActiveProfileId.ShouldBe("standard");
        settings.ActiveProfile.Id.ShouldBe("standard");
        settings.ProfileToggleHotkeyId.ShouldBe("f8");
    }

    [Fact]
    public void Legacy_Json_Without_Profiles_Migrates_RemoveFillers_To_Both_Profiles()
    {
        var legacyJson = """
        {
            "microphoneEndpointId": "mic-1",
            "startWithWindows": true,
            "setupCompleted": true,
            "hotkeyId": "right-control",
            "hudPlacement": "bottom-center",
            "speechModelId": "parakeet-v3",
            "language": "de",
            "removeFillers": false,
            "audioFeedbackEnabled": true
        }
        """;

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, legacyJson);
            var store = new SettingsStore(tempFile);
            var loaded = store.Load();

            loaded.MicrophoneEndpointId.ShouldBe("mic-1");
            loaded.StartWithWindows.ShouldBeTrue();
            loaded.SetupCompleted.ShouldBeTrue();
            loaded.HotkeyId.ShouldBe("right-control");
            loaded.SpeechModelId.ShouldBe("parakeet-v3");
            loaded.Language.ShouldBe("de");
            loaded.RemoveFillers.ShouldBeFalse();
            loaded.AudioFeedbackEnabled.ShouldBeTrue();

            loaded.Profiles.Count.ShouldBe(2);
            loaded.Profiles[0].RemoveFillers.ShouldBeFalse();
            loaded.Profiles[1].RemoveFillers.ShouldBeFalse();
            loaded.ActiveProfileId.ShouldBe("standard");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Unknown_ActiveProfileId_Falls_Back_To_Standard()
    {
        var settings = new AppSettings(null, true, true)
        {
            ActiveProfileId = "non-existent-profile"
        }.Normalize();

        settings.ActiveProfileId.ShouldBe("standard");
        settings.ActiveProfile.Id.ShouldBe("standard");
    }

    [Fact]
    public void Profile_Roundtrip_Serialization()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(tempFile);
            var original = new AppSettings(null, true, true)
            {
                ActiveProfileId = "math",
                Profiles =
                [
                    new DictationProfile { Id = "standard", Name = "Standard", FormatterKind = ProfileFormatterKind.Standard, RemoveFillers = false },
                    new DictationProfile { Id = "math", Name = "Mathe", FormatterKind = ProfileFormatterKind.Math, RemoveFillers = true, MathMode = MathFormatMode.Latex }
                ]
            };

            store.Save(original);
            var loaded = store.Load();

            loaded.ActiveProfileId.ShouldBe("math");
            loaded.ActiveProfile.Id.ShouldBe("math");
            loaded.ActiveProfile.MathMode.ShouldBe(MathFormatMode.Latex);
            loaded.Profiles[0].RemoveFillers.ShouldBeFalse();
            loaded.Profiles[1].RemoveFillers.ShouldBeTrue();

            var savedJson = File.ReadAllText(tempFile);
            savedJson.ShouldNotContain("activeProfile\"");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Colliding_HotkeyId_And_ProfileToggle_Selects_Alternative_FKey()
    {
        var settings = new AppSettings(null, true, true)
        {
            HotkeyId = "f8",
            ProfileToggleHotkeyId = "f8"
        }.Normalize();

        settings.ProfileToggleHotkeyId.ShouldBe("f9");
    }

    [Fact]
    public void Empty_Profiles_List_Is_Rebuilt()
    {
        var settings = new AppSettings(null, true, true)
        {
            Profiles = []
        }.Normalize();

        settings.Profiles.Count.ShouldBe(2);
        settings.Profiles[0].Id.ShouldBe("standard");
        settings.Profiles[1].Id.ShouldBe("math");
    }
}
