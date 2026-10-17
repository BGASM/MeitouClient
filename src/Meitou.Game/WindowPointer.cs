using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace Meitou.Game;

/// <summary>
/// Where the mouse pointer is in the game window right now, asked of Windows rather than taken from the last mouse event, so a pointer that has
/// left the window (no more events) or a window that is not in front reads as nothing. Edge scrolling uses it.
/// </summary>
static partial class WindowPointer
{
    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")] private static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] private static partial int ScreenToClient(nint hwnd, ref Point point);
    [LibraryImport("user32.dll")] private static partial int GetClientRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();

    /// <summary>The pointer as a fraction of the window's client area (0 to 1 on both axes), or null when the window is not in front or the pointer is outside it.</summary>
    public static Vector2? InWindow(IWindow window)
    {
        if (!OperatingSystem.IsWindows() || window.Native?.Win32 is not { } native) return null;
        nint hwnd = native.Hwnd;
        if (GetForegroundWindow() != hwnd || GetCursorPos(out var point) == 0 || ScreenToClient(hwnd, ref point) == 0 || GetClientRect(hwnd, out var client) == 0) return null;
        int width = client.Right - client.Left, height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0 || point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height) return null;
        return new Vector2(point.X / (float)width, point.Y / (float)height);
    }
}
