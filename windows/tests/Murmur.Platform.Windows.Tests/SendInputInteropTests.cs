namespace Murmur.Platform.Windows.Tests;

using System.Runtime.InteropServices;
using Murmur.Core.Abstractions;
using Murmur.Core.Settings;
using Murmur.Platform.Windows.Injection;
using Murmur.Platform.Windows.Native;
using Shouldly;
using Xunit;

public class SendInputInteropTests
{
    [Fact]
    public void Input_Matches_Windows_Abi_Size()
    {
        var expectedSize = IntPtr.Size == 8 ? 40 : 28;

        Marshal.SizeOf<Win32.INPUT>().ShouldBe(expectedSize);
    }

    [Fact]
    public void Input_Sequence_Moves_To_End_Before_Typing()
    {
        var inputs = SendInputInjector.BuildInputs("x", TextInsertionMode.DocumentEnd);

        inputs.Length.ShouldBe(6);
        (inputs[0].u.ki.wVk, inputs[0].u.ki.dwFlags).ShouldBe(((ushort)0x11, 0u));
        (inputs[1].u.ki.wVk, inputs[1].u.ki.dwFlags).ShouldBe(((ushort)0x23, 0u));
        (inputs[2].u.ki.wVk, inputs[2].u.ki.dwFlags).ShouldBe(((ushort)0x23, 0x0002u));
        (inputs[3].u.ki.wVk, inputs[3].u.ki.dwFlags).ShouldBe(((ushort)0x11, 0x0002u));
        (inputs[4].u.ki.wScan, inputs[4].u.ki.dwFlags).ShouldBe(((ushort)'x', 0x0004u));
        (inputs[5].u.ki.wScan, inputs[5].u.ki.dwFlags).ShouldBe(((ushort)'x', 0x0006u));
    }

    [Fact]
    public void AtCaret_does_not_move_cursor()
    {
        var inputs = SendInputInjector.BuildInputs("x", TextInsertionMode.AtCaret);

        inputs.Length.ShouldBe(2);
        inputs[0].u.ki.wVk.ShouldBe((ushort)0);
        inputs[0].u.ki.wScan.ShouldBe((ushort)'x');
        inputs[0].u.ki.dwFlags.ShouldBe(0x0004u);
        inputs[1].u.ki.dwFlags.ShouldBe(0x0006u);
    }

    [Fact]
    public void AtCaret_preserves_surrogate_pairs_and_newlines()
    {
        var inputs = SendInputInjector.BuildInputs("😀\n", TextInsertionMode.AtCaret);

        inputs.Length.ShouldBe(6);
        inputs[0].u.ki.wScan.ShouldBe((ushort)'\ud83d');
        inputs[2].u.ki.wScan.ShouldBe((ushort)'\ude00');
        inputs[4].u.ki.wScan.ShouldBe((ushort)'\n');
    }

    [Fact]
    public void Target_validation_requires_same_window_and_process()
    {
        var expected = new TextInjectionTarget((nint)42, 100);

        SendInputInjector.IsSameTarget(expected, expected).ShouldBeTrue();
        SendInputInjector.IsSameTarget(expected, new TextInjectionTarget((nint)42, 101)).ShouldBeFalse();
        SendInputInjector.IsSameTarget(expected, new TextInjectionTarget((nint)43, 100)).ShouldBeFalse();
        SendInputInjector.IsSameTarget(default, expected).ShouldBeFalse();
    }

    [Fact]
    public void Held_modifier_detection_rejects_high_bit_state_only()
    {
        SendInputInjector.HasHeldModifier(0, 0, 0).ShouldBeFalse();
        SendInputInjector.HasHeldModifier(unchecked((short)0x8000), 0, 0).ShouldBeTrue();
        SendInputInjector.HasHeldModifier(0, 0, unchecked((short)0x8000)).ShouldBeTrue();
    }
}
