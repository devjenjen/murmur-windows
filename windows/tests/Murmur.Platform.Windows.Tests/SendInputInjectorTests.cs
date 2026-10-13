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

    [Fact]
    public void Successful_focus_restore_does_not_allow_wrong_process()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var wrong = new TextInjectionTarget((nint)42, 101);
        var sent = false;
        var injector = new SendInputInjector(
            () => wrong, () => false, _ => true, _ => sent = true);
        var ex = Should.Throw<TextInjectionException>(() =>
            injector.Insert("secret", expected, TextInsertionMode.AtCaret));
        ex.Reason.ShouldBe(TextInjectionFailureReason.TargetChanged);
        sent.ShouldBeFalse();
    }

    [Fact]
    public void Restore_fails_when_restoreWindowFocus_returns_false()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var wrong = new TextInjectionTarget((nint)99, 200);
        var sent = false;
        var injector = new SendInputInjector(
            () => wrong, () => false, _ => false, _ => sent = true);
        var ex = Should.Throw<TextInjectionException>(() =>
            injector.Insert("secret", expected, TextInsertionMode.AtCaret));
        ex.Reason.ShouldBe(TextInjectionFailureReason.TargetUnavailable);
        sent.ShouldBeFalse();
    }

    [Fact]
    public void Target_disappears_after_restore_throws_TargetChanged()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var readCount = 0;
        var sent = false;
        var injector = new SendInputInjector(
            () =>
            {
                readCount++;
                return readCount == 1 ? new TextInjectionTarget((nint)99, 200) : default;
            },
            () => false,
            _ => true,
            _ => sent = true);

        var ex = Should.Throw<TextInjectionException>(() =>
            injector.Insert("secret", expected, TextInsertionMode.AtCaret));
        ex.Reason.ShouldBe(TextInjectionFailureReason.TargetChanged);
        sent.ShouldBeFalse();
    }

    [Fact]
    public void Initial_target_correct_then_pid_changes_before_send_throws_TargetChanged()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var readCount = 0;
        var sent = false;
        var injector = new SendInputInjector(
            () =>
            {
                readCount++;
                return readCount == 1 ? expected : new TextInjectionTarget((nint)42, 999);
            },
            () => false,
            _ => true,
            _ => sent = true);

        var ex = Should.Throw<TextInjectionException>(() =>
            injector.Insert("secret", expected, TextInsertionMode.AtCaret));
        ex.Reason.ShouldBe(TextInjectionFailureReason.TargetChanged);
        sent.ShouldBeFalse();
    }

    [Fact]
    public void Modifier_held_at_start_aborts_without_restoring_or_sending()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var restored = false;
        var sent = false;
        var injector = new SendInputInjector(
            () => expected, () => true, _ => restored = true, _ => sent = true);

        var ex = Should.Throw<TextInjectionException>(() =>
            injector.Insert("secret", expected, TextInsertionMode.AtCaret));
        ex.Reason.ShouldBe(TextInjectionFailureReason.ModifierHeld);
        restored.ShouldBeFalse();
        sent.ShouldBeFalse();
    }

    [Fact]
    public void Modifier_pressed_during_restore_aborts_without_sending()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var wrong = new TextInjectionTarget((nint)99, 200);
        var modCheckCount = 0;
        var sent = false;
        var injector = new SendInputInjector(
            () => expected,
            () =>
            {
                modCheckCount++;
                return modCheckCount > 1;
            },
            _ => true,
            _ => sent = true);

        var ex = Should.Throw<TextInjectionException>(() =>
            injector.Insert("secret", expected, TextInsertionMode.AtCaret));
        ex.Reason.ShouldBe(TextInjectionFailureReason.ModifierHeld);
        sent.ShouldBeFalse();
    }

    [Fact]
    public void Successful_injection_without_window_switch()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var restoredHandles = new List<nint>();
        var sentCount = 0;
        var injector = new SendInputInjector(
            () => expected,
            () => false,
            h => { restoredHandles.Add(h); return true; },
            _ => sentCount++);

        injector.Insert("test", expected, TextInsertionMode.AtCaret);
        sentCount.ShouldBe(1);
        restoredHandles.ShouldBeEmpty();
    }

    [Fact]
    public void Successful_injection_with_focus_restoration_and_previous_window_return()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var intermediate = new TextInjectionTarget((nint)99, 200);
        var restoredHandles = new List<nint>();
        var sentCount = 0;
        var readCount = 0;

        var injector = new SendInputInjector(
            () =>
            {
                readCount++;
                // 1st call: intermediate window is active
                // 2nd call: expected target is restored
                // 3rd call: expected target remains active
                return readCount == 1 ? intermediate : expected;
            },
            () => false,
            h => { restoredHandles.Add(h); return true; },
            _ => sentCount++);

        injector.Insert("test", expected, TextInsertionMode.AtCaret, restorePreviousWindow: true);
        sentCount.ShouldBe(1);
        restoredHandles.Count.ShouldBe(2);
        restoredHandles[0].ShouldBe((nint)42);
        restoredHandles[1].ShouldBe((nint)99);
    }

    [Fact]
    public void Empty_text_does_not_call_restore_or_send()
    {
        var expected = new TextInjectionTarget((nint)42, 100);
        var restored = false;
        var sent = false;
        var read = false;

        var injector = new SendInputInjector(
            () => { read = true; return expected; },
            () => false,
            _ => restored = true,
            _ => sent = true);

        injector.Insert("", expected, TextInsertionMode.AtCaret, restorePreviousWindow: true);
        read.ShouldBeFalse();
        restored.ShouldBeFalse();
        sent.ShouldBeFalse();
    }
}

