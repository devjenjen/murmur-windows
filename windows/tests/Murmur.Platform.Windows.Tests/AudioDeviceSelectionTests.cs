namespace Murmur.Platform.Windows.Tests;

using Murmur.Platform.Windows.Audio;
using Shouldly;
using Xunit;

public class AudioDeviceSelectionTests
{
    [Fact]
    public void Active_Configured_Id_Wins()
    {
        var devices = new AudioInputDevice[]
        {
            new("dev-1", "Default Mic", true),
            new("dev-2", "Selected Mic", false)
        };

        var chosen = WasapiAudioCapture.ChooseEndpointId("dev-2", devices);
        chosen.ShouldBe("dev-2");
    }

    [Fact]
    public void Missing_Configured_Id_Falls_Back_To_Default()
    {
        var devices = new AudioInputDevice[]
        {
            new("dev-1", "Default Mic", true),
            new("dev-2", "Other Mic", false)
        };

        var chosen = WasapiAudioCapture.ChooseEndpointId("dev-missing", devices);
        chosen.ShouldBe("dev-1");
    }

    [Fact]
    public void Null_Configured_Id_Selects_Default()
    {
        var devices = new AudioInputDevice[]
        {
            new("dev-1", "Default Mic", true),
            new("dev-2", "Other Mic", false)
        };

        var chosen = WasapiAudioCapture.ChooseEndpointId(null, devices);
        chosen.ShouldBe("dev-1");
    }

    [Fact]
    public void No_Default_Falls_Back_To_First_Active()
    {
        var devices = new AudioInputDevice[]
        {
            new("dev-1", "Mic 1", false),
            new("dev-2", "Mic 2", false)
        };

        var chosen = WasapiAudioCapture.ChooseEndpointId(null, devices);
        chosen.ShouldBe("dev-1");
    }

    [Fact]
    public void Empty_Devices_Returns_Null()
    {
        var chosen = WasapiAudioCapture.ChooseEndpointId("dev-1", []);
        chosen.ShouldBeNull();
    }
}
