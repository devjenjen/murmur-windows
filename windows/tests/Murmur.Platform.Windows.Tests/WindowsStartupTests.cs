namespace Murmur.Platform.Windows.Tests;

using Murmur.Platform.Windows.Startup;
using Shouldly;
using Xunit;

public class WindowsStartupTests
{
    [Fact]
    public void BuildCommand_Quotes_Executable_Path()
    {
        var cmd = WindowsStartup.BuildCommand(@"C:\Program Files\Murmur\Murmur.App.exe");
        cmd.ShouldBe("\"C:\\Program Files\\Murmur\\Murmur.App.exe\"");
    }

    [Fact]
    public void BuildCommand_Normalizes_Relative_Path()
    {
        var cmd = WindowsStartup.BuildCommand("Murmur.App.exe");
        cmd.ShouldStartWith("\"");
        cmd.ShouldEndWith("Murmur.App.exe\"");
    }

    [Fact]
    public void ResolveExecutablePath_Prefers_Existing_Installed_Path()
    {
        WindowsStartup.ResolveExecutablePath(
                @"C:\repo\publish\Murmur.App.exe",
                @"C:\fixture\installed\Murmur.App.exe",
                installedExists: true)
            .ShouldBe(@"C:\fixture\installed\Murmur.App.exe");
    }

    [Fact]
    public void ResolveExecutablePath_Uses_Current_Path_When_Install_Is_Absent()
    {
        WindowsStartup.ResolveExecutablePath(
                @"C:\repo\publish\Murmur.App.exe",
                @"C:\fixture\installed\Murmur.App.exe",
                installedExists: false)
            .ShouldBe(@"C:\repo\publish\Murmur.App.exe");
    }
}
