using Avalonia;
using Avalonia.Headless;
using Murmur.App;
using Murmur.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Murmur.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
