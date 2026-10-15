using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace PzLauncher.Desktop.Views;

public partial class MainWindow : Window
{
    private const uint WsCaption = 0x00C00000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmNcPaint = 0x0085;
    private const int GwlStyle = -16;
    private const double StandardDpi = 96;

    public MainWindow()
    {
        if (OperatingSystem.IsWindows())
        {
            // DWM plays the minimize/maximize/restore/close animations only for captioned windows; the extended client
            // area keeps the caption out of the non-client area, so it is never drawn.
            Win32Properties.AddWindowStylesCallback(this, static (style, exStyle) => (style | WsCaption | WsSysMenu, exStyle));
            Win32Properties.AddWndProcHookCallback(this, CaptionStyleWndProcHook);
        }

        InitializeComponent();
    }

    private IntPtr CaptionStyleWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            // Avalonia swallows WM_NCPAINT for non-Full decorations; with a caption, DWM then drops the frame (shadow,
            // border, rounded corners).
            case WmNcPaint:
                handled = true;
                return DefWindowProcW(hWnd, msg, wParam, lParam);
            // Avalonia's minimum counts the caption as non-client, but its WM_NCCALCSIZE gives the caption to the
            // client area, so the client minimum grew by the caption height.
            case WmGetMinMaxInfo:
                handled = true;
                SetMinTrackSize(hWnd, lParam);
                return IntPtr.Zero;
            default:
                return IntPtr.Zero;
        }
    }

    private void SetMinTrackSize(IntPtr hWnd, IntPtr lParam)
    {
        // Same border as Avalonia's restored-window WM_NCCALCSIZE: style only, window DPI, nothing on top.
        var border = default(Win32Rect);
        AdjustWindowRectExForDpi(ref border, GetWindowLongW(hWnd, GwlStyle), false, 0, (uint)(RenderScaling * StandardDpi));

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MinTrackSize.X = (int)(MinWidth * RenderScaling) - border.Left + border.Right;
        info.MinTrackSize.Y = (int)(MinHeight * RenderScaling) + border.Bottom;
        Marshal.StructureToPtr(info, lParam, false);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowLongW(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(
        ref Win32Rect rect,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool menu,
        uint exStyle,
        uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Win32Point Reserved;
        public Win32Point MaxSize;
        public Win32Point MaxPosition;
        public Win32Point MinTrackSize;
        public Win32Point MaxTrackSize;
    }
}
