using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Refrigerator;

internal static class Win32
{
    public const int WM_HOTKEY = 0x0312;
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    public static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out var pid);
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch { return ""; }
    }

    public static IntPtr FindMainWindow(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var p = Process.GetProcessesByName(name).FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
                if (p is not null) return p.MainWindowHandle;
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    public static bool IsAllowedForeground(AppConfig cfg)
    {
        var p = GetForegroundProcessName();
        if (string.IsNullOrWhiteSpace(p)) return cfg.GenericAppsEnabled;
        if (cfg.RobloxEnabled && p.Contains("Roblox", StringComparison.OrdinalIgnoreCase)) return true;
        if (cfg.DiscordEnabled && p.Contains("Discord", StringComparison.OrdinalIgnoreCase)) return true;
        return cfg.GenericAppsEnabled;
    }
}
