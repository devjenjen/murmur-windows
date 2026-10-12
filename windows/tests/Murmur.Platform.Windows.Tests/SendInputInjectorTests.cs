namespace Murmur.Platform.Windows.Tests;

using Murmur.Core.Abstractions;
using Murmur.Core.Settings;
using Murmur.Platform.Windows.Injection;
using Shouldly;
using Xunit;

public class SendInputInjectorTests
{
    [Fact]
    public void IsSameTarget_Returns_True_When_Handles_And_Processes_Match()
    {
        var a = new TextInjectionTarget(1234, 5678);
        var b = new TextInjectionTarget(1234, 5678);
        SendInputInjector.IsSameTarget(a, b).ShouldBeTrue();
    }

    [Fact]
    public void IsSameTarget_Returns_False_When_Handles_Differ()
    {
        var a = new TextInjectionTarget(1234, 5678);
        var b = new TextInjectionTarget(9999, 5678);
        SendInputInjector.IsSameTarget(a, b).ShouldBeFalse();
    }

    [Fact]
    public void IsSameTarget_Returns_False_When_Processes_Differ()
    {
        var a = new TextInjectionTarget(1234, 5678);
        var b = new TextInjectionTarget(1234, 9999);
        SendInputInjector.IsSameTarget(a, b).ShouldBeFalse();
    }

    [Fact]
    public void IsSameTarget_Returns_False_When_Target_Is_Zero_Or_Default()
    {
        var target = new TextInjectionTarget(1234, 5678);
        SendInputInjector.IsSameTarget(default, target).ShouldBeFalse();
        SendInputInjector.IsSameTarget(target, default).ShouldBeFalse();
        SendInputInjector.IsSameTarget(new TextInjectionTarget(0, 5678), target).ShouldBeFalse();
        SendInputInjector.IsSameTarget(new TextInjectionTarget(1234, 0), target).ShouldBeFalse();
    }

    [Fact]
    public void RestoreWindowFocus_Returns_False_For_Zero_Window_Handle()
    {
        SendInputInjector.RestoreWindowFocus(0).ShouldBeFalse();
    }

    [Fact]
    public void RestoreWindowFocus_Returns_False_For_Invalid_Window_Handle()
    {
        SendInputInjector.RestoreWindowFocus(unchecked((nint)0xDEADBEEF)).ShouldBeFalse();
    }

    [Fact]
    public void Insert_Throws_TargetUnavailable_When_Target_Window_Cannot_Be_Restored()
    {
        var injector = new SendInputInjector();
        var invalidTarget = new TextInjectionTarget(unchecked((nint)0xDEADBEEF), 9999);

        var ex = Should.Throw<TextInjectionException>(() =>
            injector.Insert("hello", invalidTarget, TextInsertionMode.AtCaret, restorePreviousWindow: false));

        ex.Reason.ShouldBe(TextInjectionFailureReason.TargetUnavailable);
    }

    [Fact]
    public void Insert_Empty_Text_Returns_Without_Throwing()
    {
        var injector = new SendInputInjector();
        var invalidTarget = new TextInjectionTarget(unchecked((nint)0xDEADBEEF), 9999);

        // Empty text should immediately return and not attempt window focus restoration or injection
        Should.NotThrow(() => injector.Insert("", invalidTarget, TextInsertionMode.AtCaret, restorePreviousWindow: false));
        Should.NotThrow(() => injector.Insert(string.Empty, invalidTarget, TextInsertionMode.DocumentEnd, restorePreviousWindow: true));
    }

    [Fact]
    public void AppSettings_RestorePreviousWindowAfterInjection_Defaults_To_False()
    {
        var settings = new AppSettings(null, false, false);
        settings.RestorePreviousWindowAfterInjection.ShouldBeFalse();
        AppSettings.Default.RestorePreviousWindowAfterInjection.ShouldBeFalse();
    }

    [Fact]
    public void AppSettings_RestorePreviousWindowAfterInjection_Can_Be_Configured()
    {
        var settings = new AppSettings(null, false, false) with
        {
            RestorePreviousWindowAfterInjection = true
        };
        settings.RestorePreviousWindowAfterInjection.ShouldBeTrue();

        var normalized = settings.Normalize();
        normalized.RestorePreviousWindowAfterInjection.ShouldBeTrue();

        var copy = settings with { RestorePreviousWindowAfterInjection = false };
        copy.RestorePreviousWindowAfterInjection.ShouldBeFalse();
        settings.Equals(copy).ShouldBeFalse();
    }
}
