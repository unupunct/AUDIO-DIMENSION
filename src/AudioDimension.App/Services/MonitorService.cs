using System.Runtime.InteropServices;

namespace AudioDimension.App.Services;

public sealed record MonitorInfo(int Index, string Name, int X, int Y, int Width, int Height, bool IsPrimary)
{
    public override string ToString() => $"{Name} ({Width}x{Height}){(IsPrimary ? " - primary" : "")}";
}

/// <summary>Enumerates monitors in physical pixels and places windows on them (multi-monitor preview).</summary>
public static class MonitorService
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    private delegate bool EnumProc(IntPtr hMon, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMon, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    public static IReadOnlyList<MonitorInfo> List()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Rect _, IntPtr _) =>
        {
            var mi = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(h, ref mi))
                list.Add(new MonitorInfo(list.Count + 1, $"Monitor {list.Count + 1}", mi.Monitor.Left, mi.Monitor.Top,
                    mi.Monitor.Right - mi.Monitor.Left, mi.Monitor.Bottom - mi.Monitor.Top, (mi.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return list.Count > 0 ? list : new[] { new MonitorInfo(1, "Monitor 1", 0, 0, 1920, 1080, true) };
    }

    /// <summary>Monitor index (1-based) a window currently sits on.</summary>
    public static int MonitorOf(IntPtr hwnd)
    {
        var h = MonitorFromWindow(hwnd, 2);
        var mi = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(h, ref mi)) return 1;
        var m = List().FirstOrDefault(x => x.X == mi.Monitor.Left && x.Y == mi.Monitor.Top);
        return m?.Index ?? 1;
    }

    public static void Place(IntPtr hwnd, int x, int y, int w, int h) => SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0004 | 0x0010 | 0x0040);
}
