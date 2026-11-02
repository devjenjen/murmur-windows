using System.Runtime.InteropServices;
using Murmur.App.Views;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public class HudWindowNativeTopmostTests
{
    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const uint WsPopup = 0x80000000;
    private static readonly nint HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [Fact]
    public void HudWindow_TopmostCanBeRestoredAndSurvivesExtendedStyleUpdate()
    {
        var handle = CreateWindowExW(
            WsExNoActivate,
            "STATIC",
            "Murmur HUD native topmost test",
            WsPopup,
            17,
            23,
            90,
            42,
            0,
            0,
            0,
            0);

        handle.ShouldNotBe(nint.Zero);

        try
        {
            ShowWindow(handle, 8).ShouldBeFalse(); // SW_SHOWNA: match OnOpened without changing focus.
            var originalBounds = GetBounds(handle);
            var foregroundBefore = GetForegroundWindow();

            for (var showCycle = 0; showCycle < 3; showCycle++)
            {
                // Simulate a Show cycle that leaves the HUD in the normal z-order band.
                SetWindowPos(
                    handle,
                    HwndNotTopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoActivate | SwpNoMove | SwpNoSize).ShouldBeTrue();
                ((long)GetWindowLongPtrW(handle, GwlExStyle) & WsExTopmost).ShouldBe(0L);

                HudWindow.TrySetNativeTopmost(handle, out var errorCode).ShouldBeTrue();
                errorCode.ShouldBe(0);
                ((long)GetWindowLongPtrW(handle, GwlExStyle) & WsExTopmost).ShouldNotBe(0L);

                // Simulate Avalonia rebuilding extended styles without the WS_EX_TOPMOST bit.
                SetWindowLongPtrW(
                    handle,
                    GwlExStyle,
                    WsExNoActivate | WsExToolWindow | WsExTransparent);
                var styleAfterUpdate = (long)GetWindowLongPtrW(handle, GwlExStyle);
                (styleAfterUpdate & WsExTopmost).ShouldNotBe(0L);
                (styleAfterUpdate & (WsExNoActivate | WsExToolWindow | WsExTransparent))
                    .ShouldBe((long)(WsExNoActivate | WsExToolWindow | WsExTransparent));

                var actualBounds = GetBounds(handle);
                actualBounds.Left.ShouldBe(originalBounds.Left);
                actualBounds.Top.ShouldBe(originalBounds.Top);
                actualBounds.Right.ShouldBe(originalBounds.Right);
                actualBounds.Bottom.ShouldBe(originalBounds.Bottom);
                GetForegroundWindow().ShouldBe(foregroundBefore);
            }
        }
        finally
        {
            DestroyWindow(handle);
        }
    }

    private static NativeRect GetBounds(nint handle)
    {
        GetWindowRect(handle, out var bounds).ShouldBeTrue();
        return bounds;
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint handle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
