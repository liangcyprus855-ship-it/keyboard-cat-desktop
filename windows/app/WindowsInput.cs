using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class WindowsInput
{
    internal const int BufferCapacity = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct Device
    {
        public ushort Page, Usage;
        public uint Flags;
        public IntPtr Target;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScreenPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] Device[] devices, uint count, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr message, uint command, IntPtr buffer, ref uint size, uint headerSize);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out ScreenPoint pt);
    [DllImport("user32.dll")] internal static extern int GetMessagePos();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(ScreenPoint pt);
    [DllImport("user32.dll")] internal static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetCapture();
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformationW(IntPtr obj, int index, byte[] info, uint length, out uint needed);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder text, int capacity);

    private static bool bossHotkeyHeld;

    internal static string CurrentDesktopName()
    {
        IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
        if (desktop == IntPtr.Zero) return String.Empty;
        byte[] buffer = new byte[512];
        uint needed = 0;
        if (!GetUserObjectInformationW(desktop, 2, buffer, (uint)buffer.Length, out needed) || needed < 2)
            return String.Empty;
        int bytes = Math.Min(buffer.Length, (int)needed);
        return System.Text.Encoding.Unicode.GetString(buffer, 0, bytes).TrimEnd('\0');
    }

    public static void Listen(IntPtr hwnd, bool enable)
    {
        Device keyboard = new Device { Page = 1, Usage = 6,
            Flags = enable ? 0x100u : 1u, Target = enable ? hwnd : IntPtr.Zero };
        Device mouse = keyboard;
        mouse.Usage = 2;
        if (!RegisterRawInputDevices(new Device[] { keyboard, mouse }, 2, (uint)Marshal.SizeOf(typeof(Device))))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static uint ReadPacket(IntPtr message, IntPtr buffer)
    {
        uint size = BufferCapacity;
        return GetRawInputData(message, 0x10000003, buffer, ref size, (uint)(8 + 2 * IntPtr.Size));
    }

    public static bool IsMousePress(IntPtr message, IntPtr buffer)
    {
        uint read = ReadPacket(message, buffer);
        int header = 8 + 2 * IntPtr.Size;
        if (read == uint.MaxValue || read < header + 24 || Marshal.ReadInt32(buffer) != 0) return false;
        const int down = 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100; // left, right, middle, X1 and X2
        return (Marshal.ReadInt16(buffer, header + 4) & down) != 0;
    }

    public static bool CheckBossHotkey(IntPtr message, IntPtr buffer)
    {
        uint read = ReadPacket(message, buffer);
        int header = 8 + 2 * IntPtr.Size;
        if (read == uint.MaxValue || read < header + 16 || Marshal.ReadInt32(buffer) != 1) return false;
        int scan = (ushort)Marshal.ReadInt16(buffer, header);
        int flags = (ushort)Marshal.ReadInt16(buffer, header + 2);
        int key = (ushort)Marshal.ReadInt16(buffer, header + 6);
        if (scan == 255) return false;
        bool isBreak = (flags & 1) != 0;
        if (key == 0x48)
        {
            if (isBreak) { bossHotkeyHeld = false; return false; }
            if (bossHotkeyHeld) return false;
            bool ctrl = GetAsyncKeyState(0x11) < 0;
            bool alt = GetAsyncKeyState(0x12) < 0;
            if (ctrl && alt)
            {
                bossHotkeyHeld = true;
                return true;
            }
            return false;
        }
        if (isBreak && (key == 0x11 || key == 0xA2 || key == 0xA3 || key == 0x12 || key == 0xA4 || key == 0xA5))
            bossHotkeyHeld = false;
        return false;
    }

    internal static bool RectCoversMonitor(RECT win, RECT mon)
    {
        return win.Left <= mon.Left && win.Top <= mon.Top && win.Right >= mon.Right && win.Bottom >= mon.Bottom;
    }

    public static bool IsOtherAppFullscreen(IntPtr selfHwnd)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == selfHwnd || fg == GetShellWindow()) return false;
        if (!IsWindowVisible(fg) || IsIconic(fg)) return false;
        var cls = new System.Text.StringBuilder(64);
        if (GetClassNameW(fg, cls, cls.Capacity) > 0)
        {
            string name = cls.ToString();
            if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
                return false;
        }
        RECT winRect;
        if (!GetWindowRect(fg, out winRect)) return false;
        IntPtr mon = MonitorFromWindow(fg, 2);
        if (mon == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        if (!GetMonitorInfoW(mon, ref mi)) return false;
        return RectCoversMonitor(winRect, mi.rcMonitor);
    }

    public static void Process(IntPtr message, IntPtr buffer, CatScene scene, double now)
    {
        ProcessPacket(buffer, ReadPacket(message, buffer), scene, now);
    }

    internal static void ProcessPacket(IntPtr buffer, uint read, CatScene scene, double now)
    {
        int header = 8 + 2 * IntPtr.Size;
        if (read == uint.MaxValue || read < header) return;
        int type = Marshal.ReadInt32(buffer);
        if (type == 0 && read >= header + 24)
        {
            // RAWMOUSE's aligned button union begins four bytes into its payload.
            int buttons = (ushort)Marshal.ReadInt16(buffer, header + 4);
            if ((buttons & 1) != 0) scene.MouseButton(1, true, now);
            if ((buttons & 2) != 0) scene.MouseButton(1, false, now);
            if ((buttons & 4) != 0) scene.MouseButton(2, true, now);
            if ((buttons & 8) != 0) scene.MouseButton(2, false, now);
            if ((buttons & (0x0C00 | 0x0010 | 0x0040 | 0x0100)) != 0) scene.MouseScroll(now);
            return;
        }
        if (type != 1 || read < header + 16) return;
        int scan = (ushort)Marshal.ReadInt16(buffer, header);
        int flags = (ushort)Marshal.ReadInt16(buffer, header + 2);
        int key = (ushort)Marshal.ReadInt16(buffer, header + 6);
        if ((flags & 1) != 0 || scan == 255 || key < 1 || key > 254) return;
        if (key == 16) key = scan == 0x36 ? 161 : 160;
        else if (key == 17) key = (flags & 2) != 0 ? 163 : 162;
        else if (key == 18) key = (flags & 2) != 0 ? 165 : 164;
        scene.Strike(key, now);
    }
}
