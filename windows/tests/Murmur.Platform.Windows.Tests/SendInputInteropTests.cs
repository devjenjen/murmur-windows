namespace Murmur.Platform.Windows.Tests;

using System.Runtime.InteropServices;
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
        var inputs = SendInputInjector.BuildInputs("x");

        inputs.Length.ShouldBe(6);
        (inputs[0].u.ki.wVk, inputs[0].u.ki.dwFlags).ShouldBe(((ushort)0x11, 0u));
        (inputs[1].u.ki.wVk, inputs[1].u.ki.dwFlags).ShouldBe(((ushort)0x23, 0u));
        (inputs[2].u.ki.wVk, inputs[2].u.ki.dwFlags).ShouldBe(((ushort)0x23, 0x0002u));
        (inputs[3].u.ki.wVk, inputs[3].u.ki.dwFlags).ShouldBe(((ushort)0x11, 0x0002u));
        (inputs[4].u.ki.wScan, inputs[4].u.ki.dwFlags).ShouldBe(((ushort)'x', 0x0004u));
        (inputs[5].u.ki.wScan, inputs[5].u.ki.dwFlags).ShouldBe(((ushort)'x', 0x0006u));
    }
}
