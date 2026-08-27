using Murmur.Platform.Windows.Hotkey;
using Murmur.Platform.Windows.Native;
using Shouldly;
using Xunit;

namespace Murmur.Platform.Windows.Tests;

public class HotkeyOptionsTests
{
    [Theory]
    [InlineData(null, "right-control")]
    [InlineData("unknown", "right-control")]
    [InlineData("f9", "f9")]
    [InlineData("scroll-lock", "scroll-lock")]
    public void Get_Returns_Selected_Or_Default(string? id, string expected)
        => HotkeyOptions.Get(id).Id.ShouldBe(expected);

    [Fact]
    public void Right_Control_Matches_Generic_Extended_Control()
    {
        var option = HotkeyOptions.Get("right-control");
        option.Matches(Win32.VK_CONTROL, Win32.LLKHF_EXTENDED).ShouldBeTrue();
        option.Matches(Win32.VK_CONTROL, 0).ShouldBeFalse();
    }

    [Fact]
    public void Function_Key_Requires_Exact_Virtual_Key()
    {
        var option = HotkeyOptions.Get("f9");
        option.Matches(Win32.VK_F9, 0).ShouldBeTrue();
        option.Matches(Win32.VK_F10, 0).ShouldBeFalse();
    }

    [Fact]
    public void Custom_Virtual_Key_Is_Parsed_And_Matched()
    {
        var option = HotkeyOptions.Get("vk:117");

        option.Id.ShouldBe("vk:117");
        option.VirtualKey.ShouldBe(117u);
        option.Matches(117, 0).ShouldBeTrue();
        option.Matches(118, 0).ShouldBeFalse();
    }

    [Theory]
    [InlineData("vk:0")]
    [InlineData("vk:256")]
    [InlineData("vk:nope")]
    public void Invalid_Custom_Virtual_Key_Falls_Back_To_Right_Control(string id)
    {
        HotkeyOptions.Get(id).Id.ShouldBe("right-control");
    }
}
