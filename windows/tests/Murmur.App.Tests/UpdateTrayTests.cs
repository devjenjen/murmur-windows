using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public sealed class UpdateTrayTests
{
    [AvaloniaFact]
    public void App_TrayUsesStableUpdateAvailableIdentifier()
    {
        var app = new App();
        app.Initialize();

        var trayIcons = TrayIcon.GetIcons(app);
        trayIcons.ShouldNotBeNull();

        var menuItem = trayIcons!
            .SelectMany(icon => icon.Menu?.Items ?? [])
            .OfType<NativeMenuItem>()
            .SingleOrDefault(item => item.CommandParameter as string == "Murmur.UpdateAvailable");

        menuItem.ShouldNotBeNull();
    }
}
