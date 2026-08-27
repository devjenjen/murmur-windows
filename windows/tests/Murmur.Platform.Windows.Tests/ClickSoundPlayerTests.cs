namespace Murmur.Platform.Windows.Tests;

using Murmur.Platform.Windows.Audio;
using Shouldly;
using Xunit;

public class ClickSoundPlayerTests
{
    [Fact]
    public void Play_ReturnsFalse_ForMissingAndInvalidExternalWav()
    {
        var nonexistentTemporaryPath = Path.Combine(Path.GetTempPath(), $"murmur-missing-{Guid.NewGuid():N}.wav");
        var invalidTemporaryWavPath = Path.Combine(Path.GetTempPath(), $"murmur-invalid-{Guid.NewGuid():N}.wav");

        try
        {
            File.WriteAllBytes(invalidTemporaryWavPath, [0]);

            using var player = new ClickSoundPlayer();
            player.Play(nonexistentTemporaryPath).ShouldBeFalse();
            player.Play(invalidTemporaryWavPath).ShouldBeFalse();
        }
        finally
        {
            File.Delete(invalidTemporaryWavPath);
        }
    }
}
