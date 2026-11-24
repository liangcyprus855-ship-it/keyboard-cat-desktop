using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

// PS5 DualSense input via the native HID API (docs/GAMEPAD.md section 7,
// milestone G4). XInput cannot see a DualSense that is connected directly
// (no Steam Input / DS4Windows remap), so this reads the controller's HID
// input reports with hid.dll + setupapi.dll and feeds the same CatScene
// events GamepadInput produces.
//
// Supported transports:
//   - USB: input report 0x01, 64 bytes (sticks 1-4, triggers 5-6, seq 7,
//     buttons 8-10). This is the layout SDL and the Linux hid-playstation
//     driver use for the wired report.
//   - Bluetooth: input report 0x31, 78 bytes. The payload is the USB
//     payload shifted by one byte (the report carries a leading byte
//     before the stick data), so every USB offset below is +1.
//
// Button mapping (DualSense -> GamepadButton): Cross -> A, Circle -> B,
// Square -> X, Triangle -> Y (the PS5 face-button positions match the
// Xbox layout positions the art already draws). L1 -> LB, R1 -> RB,
// L3 -> LS, R3 -> RS, Create -> Back, Options -> Start. The D-pad hat
// (low nibble of buttons0: 0 up, 1 up-right, 2 right, 3 down-right,
// 4 down, 5 down-left, 6 left, 7 up-left, 8 neutral) maps to DUp /
// DDown / DLeft / DRight. The PS button has no GamepadButton and is
// ignored. Triggers use the analog bytes with the same 30/255 press
// threshold XInput uses.
//
// When a DualSense is first seen, OnDualSenseConnected fires (on the UI
// thread) so the app can auto-select the PS5 style; the input itself
// works regardless of the style preference.
internal static class DualSenseInput
{
    private const ushort SonyVendorId = 0x054C;
    private const ushort DualSenseProductId = 0x0CE6;
    private const ushort DualSenseEdgeProductId = 0x0DF2;

    private const int TriggerThreshold = 30;
    private const int StickDeadzone = 12; // raw units out of 128

    // Called on the UI thread when a DualSense is first detected, so the
    // app can switch the style to PS5. Set by the app; may be null.
    public static Action OnDualSenseConnected;

    private static CatScene scene;
    private static Thread pollThread;
    private static volatile bool running;
    private static volatile bool connected;
    private static IntPtr deviceHandle = IntPtr.Zero;
    private static int reportLength;

    private class PadState
    {
        public bool[] Buttons = new bool[16]; // indexed by GamepadButton
        public double LeftTrigger, RightTrigger;
        public Vector LeftStick, RightStick;
    }
    private static readonly PadState last = new PadState();

    // Diagnostic log. The DualSense path has failed silently on real
    // hardware more than once, so every step writes one line to a temp
    // file the user can send back: %TEMP%\keyboard-cat-dualsense.log.
    // Logging never throws; a failed write is silently dropped.
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "keyboard-cat-dualsense.log");
    private static int logLines;
    private static readonly HashSet<string> seenDevices = new HashSet<string>();
    private static void Log(string msg)
    {
        try
        {
            // Cap the file so a stuck loop cannot fill the disk.
            if (logLines >= 2000) return;
            logLines++;
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n");
        }
        catch (Exception) { }
    }

    public static bool IsAvailable { get { return connected; } }

    public static void Start(CatScene target)
    {
        if (running) return;
        scene = target;
        running = true;
        Log("Start: log=" + LogPath);
        pollThread = new Thread(PollLoop) { IsBackground = true, Name = "DualSenseInput" };
        pollThread.Start();
    }

    public static void Stop()
    {
        running = false;
        if (pollThread != null)
        {
            pollThread.Join(500);
            pollThread = null;
        }
        CloseDevice();
        scene = null;
    }

    private static void Post(Action action)
    {
        CatScene target = scene;
        if (target == null || !running) return;
        target.Dispatcher.BeginInvoke(action);
    }

    private static double Now()
    {
        return PetWindow.Now;
    }

    private static void PollLoop()
    {
        byte[] report = null;
        while (running)
        {
            if (!connected)
            {
                if (TryOpenDevice())
                {
                    connected = true;
                    report = new byte[reportLength];
                    // Announce the PS5 style once per connection.
                    Action announce = OnDualSenseConnected;
                    if (announce != null) Post(announce);
                }
                else
                {
                    Thread.Sleep(500);
                    continue;
                }
            }

            // Blocking interrupt read: the controller pushes an input
            // report whenever its state changes, and ReadFile returns it
            // (report id in byte 0). This is the standard way to receive
            // HID input; HidD_GetInputReport (a control transfer) is not
            // answered by every device, and polling it in a loop also
            // burns CPU when the pad is idle. ReadFile blocks here, so
            // the loop wakes only on real input; closing the handle from
            // Stop() unblocks it.
            int bytesRead = 0;
            bool ok = false;
            try
            {
                ok = ReadFile(deviceHandle, report, report.Length, out bytesRead, IntPtr.Zero);
            }
            catch (Exception ex) { ok = false; Log("ReadFile threw: " + ex.GetType().Name); }
            if (!ok || bytesRead < 11)
            {
                // Device gone or read failed: drop it and re-enumerate.
                Log("ReadFile failed: ok=" + ok + " bytesRead=" + bytesRead
                    + " err=" + Marshal.GetLastWin32Error());
                CloseDevice();
                connected = false;
                lock (last) { Array.Clear(last.Buttons, 0, last.Buttons.Length); }
                Thread.Sleep(200);
                continue;
            }

            ParseAndDispatch(report);
        }
    }

    private static void CloseDevice()
    {
        if (deviceHandle != IntPtr.Zero && deviceHandle != new IntPtr(-1))
        {
            CloseHandle(deviceHandle);
            deviceHandle = IntPtr.Zero;
        }
        reportLength = 0;
    }

    // Enumerate HID devices, open the first Sony DualSense (or Edge).
    // Returns true and leaves deviceHandle + reportLength set on success.
    private static bool TryOpenDevice()
    {
        Guid hidGuid = Guid.Empty;
        HidD_GetHidGuid(out hidGuid);
        IntPtr infoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (infoSet == IntPtr.Zero || infoSet == new IntPtr(-1))
        {
            Log("TryOpenDevice: SetupDiGetClassDevs failed, err=" + Marshal.GetLastWin32Error());
            return false;
        }
        try
        {
            SP_DEVICE_INTERFACE_DATA iface = new SP_DEVICE_INTERFACE_DATA();
            iface.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            int enumerated = 0;
            for (int index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref iface))
                {
                    // No more devices (or an error). Log the count once
                    // per distinct total so we can tell enumeration ran.
                    string countKey = "count=" + enumerated;
                    if (seenDevices.Add(countKey))
                        Log("TryOpenDevice: enumerated " + enumerated + " HID devices, no DualSense opened");
                    return false;
                }
                enumerated++;
                string path = GetDevicePath(infoSet, ref iface);
                if (path == null) continue;
                IntPtr handle = CreateFile(path, GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    string key = "openfail=" + path;
                    if (seenDevices.Add(key))
                        Log("TryOpenDevice: CreateFile failed for " + path
                            + ", err=" + Marshal.GetLastWin32Error());
                    continue;
                }
                try
                {
                    HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                    attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                    if (!HidD_GetAttributes(handle, ref attr)) continue;
                    // Log every distinct HID device once, so the log shows
                    // whether a DualSense (Sony VID) is even present and
                    // what PID it carries.
                    string devKey = "vid=0x" + attr.VendorID.ToString("X4")
                        + " pid=0x" + attr.ProductID.ToString("X4");
                    if (seenDevices.Add(devKey))
                        Log("HID device: " + devKey);
                    if (attr.VendorID != SonyVendorId) continue;
                    if (attr.ProductID != DualSenseProductId
                        && attr.ProductID != DualSenseEdgeProductId) continue;
                    // The HID calls below run against real hardware and
                    // have crashed the process before (the app vanished
                    // right after the Sony VID/PID line, with no GetCaps
                    // line). Never let one collection take the app down:
                    // catch everything, log it, and move on.
                    try
                    {
                        IntPtr preparsed = IntPtr.Zero;
                        if (!HidD_GetPreparsedData(handle, out preparsed) || preparsed == IntPtr.Zero)
                        {
                            Log("Sony device found (PID=0x" + attr.ProductID.ToString("X4")
                                + ") but GetPreparsedData failed");
                            continue;
                        }
                        try
                        {
                            HIDP_CAPS caps = new HIDP_CAPS();
                            caps.Reserved = new ushort[17];
                            // NOTE: the caps function is HidP_GetCaps, not
                            // HidD_GetCaps — HidD_GetCaps does not exist in
                            // hid.dll (the HidD_* functions are device-level;
                            // the HidP_* functions parse preparsed data).
                            // It returns NTSTATUS, not BOOLEAN: 0
                            // (STATUS_SUCCESS) means the caps are valid.
                            int status = HidP_GetCaps(preparsed, ref caps);
                            bool capsOk = status == 0;
                            Log("Sony device PID=0x" + attr.ProductID.ToString("X4")
                                + " GetCaps=" + capsOk
                                + " status=0x" + status.ToString("X8")
                                + " UsagePage=" + caps.UsagePage
                                + " Usage=" + caps.Usage
                                + " InReportLen=" + caps.InputReportByteLength);
                            if (!capsOk) continue;
                            // A DualSense exposes several HID collections on
                            // the same VID/PID (gamepad, touchpad-as-mouse,
                            // audio). Only the gamepad top-level collection
                            // (Generic Desktop page, Gamepad/Joystick usage)
                            // carries the buttons, sticks and triggers; the
                            // touchpad collection would only ever yield mouse
                            // movement. Skip anything else.
                            if (caps.UsagePage != 1) continue;
                            if (caps.Usage != 5 && caps.Usage != 4) continue;
                            int len = caps.InputReportByteLength;
                            if (len < 32) continue;
                            deviceHandle = handle;
                            reportLength = len;
                            Log("Opened DualSense gamepad collection, reportLength=" + len);
                            return true;
                        }
                        finally { HidD_FreePreparsedData(preparsed); }
                    }
                    catch (Exception ex)
                    {
                        Log("Sony device handling threw: " + ex.GetType().Name + " " + ex.Message);
                        continue;
                    }
                }
                finally
                {
                    // If this handle is the one we keep, don't close it.
                    if (deviceHandle != handle) CloseHandle(handle);
                }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(infoSet); }
    }

    private static string GetDevicePath(IntPtr infoSet, ref SP_DEVICE_INTERFACE_DATA iface)
    {
        int required = 0;
        SetupDiGetDeviceInterfaceDetail(infoSet, ref iface, IntPtr.Zero, 0, out required, IntPtr.Zero);
        if (required <= 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal(required);
        try
        {
            // cbSize is the value the API validates: 8 on 64-bit,
            // 4 + char size on 32-bit. But DevicePath itself starts right
            // after the 4-byte cbSize field (offset 4), not at cbSize —
            // reading at cbSize skips the leading "\\", leaving "?\" and
            // CreateFile then rejects the path with ERROR_INVALID_NAME.
            int cbSize = IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize;
            Marshal.WriteInt32(buffer, cbSize);
            int got;
            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref iface, buffer, required, out got, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringAuto(new IntPtr(buffer.ToInt64() + 4));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static int reportCount;
    private static void ParseAndDispatch(byte[] report)
    {
        if (report == null || report.Length < 11) return;
        reportCount++;
        if (reportCount <= 5)
        {
            Log("Report #" + reportCount + " id=0x" + report[0].ToString("X2")
                + " len=" + report.Length
                + " bytes=" + BitConverter.ToString(report, 0, Math.Min(11, report.Length)));
        }
        int offset;
        if (report[0] == 0x01) offset = 0;          // USB
        else if (report[0] == 0x31) offset = 1;    // Bluetooth: payload +1
        else
        {
            if (reportCount <= 5) Log("Report #" + reportCount + ": unexpected id, ignored");
            return;
        }
        // After the report id (and the Bluetooth leading byte), the
        // payload is: LX, LY, RX, RY, L2, R2, seq, buttons0, buttons1,
        // buttons2, ...  Index p points at LX.
        int p = 1 + offset;
        if (report.Length < p + 10) return;

        // Sticks: X is 0 left / 255 right, Y is 0 up / 255 down, so Y is
        // flipped to match XInput (positive Y is up). Returns -1..1.
        double lx = (report[p + 0] - 128) / 127.5;
        double ly = (128 - report[p + 1]) / 127.5;
        double rx = (report[p + 2] - 128) / 127.5;
        double ry = (128 - report[p + 3]) / 127.5;
        double l2 = report[p + 4] / 255.0;
        double r2 = report[p + 5] / 255.0;
        byte b0 = report[p + 7];
        byte b1 = report[p + 8];
        byte b2 = report[p + 9];

        bool[] pressed = new bool[16];
        // Face buttons: bit 4 Square, 5 Cross, 6 Circle, 7 Triangle.
        pressed[(int)GamepadButton.X] = (b0 & 0x10) != 0;
        pressed[(int)GamepadButton.A] = (b0 & 0x20) != 0;
        pressed[(int)GamepadButton.B] = (b0 & 0x40) != 0;
        pressed[(int)GamepadButton.Y] = (b0 & 0x80) != 0;
        // D-pad hat in the low nibble of b0.
        int hat = b0 & 0x0F;
        pressed[(int)GamepadButton.DUp] = hat == 0 || hat == 1 || hat == 7;
        pressed[(int)GamepadButton.DRight] = hat == 1 || hat == 2 || hat == 3;
        pressed[(int)GamepadButton.DDown] = hat == 3 || hat == 4 || hat == 5;
        pressed[(int)GamepadButton.DLeft] = hat == 5 || hat == 6 || hat == 7;
        // Shoulders / system: L1, R1, L2, R2, Create, Options, L3, R3.
        pressed[(int)GamepadButton.LB] = (b1 & 0x01) != 0;
        pressed[(int)GamepadButton.RB] = (b1 & 0x02) != 0;
        pressed[(int)GamepadButton.LT] = (b1 & 0x04) != 0 || l2 > TriggerThreshold / 255.0;
        pressed[(int)GamepadButton.RT] = (b1 & 0x08) != 0 || r2 > TriggerThreshold / 255.0;
        pressed[(int)GamepadButton.Back] = (b1 & 0x10) != 0;
        pressed[(int)GamepadButton.Start] = (b1 & 0x20) != 0;
        pressed[(int)GamepadButton.LS] = (b1 & 0x40) != 0;
        pressed[(int)GamepadButton.RS] = (b1 & 0x80) != 0;
        // b2: PS (bit 0), touchpad (bit 1) — no GamepadButton; ignored.

        Vector leftStick = new Vector(
            Math.Abs(lx) < StickDeadzone / 128.0 ? 0 : lx,
            Math.Abs(ly) < StickDeadzone / 128.0 ? 0 : ly);
        Vector rightStick = new Vector(
            Math.Abs(rx) < StickDeadzone / 128.0 ? 0 : rx,
            Math.Abs(ry) < StickDeadzone / 128.0 ? 0 : ry);

        double now = Now();
        for (int i = 0; i < pressed.Length; i++)
        {
            if (pressed[i] == last.Buttons[i]) continue;
            last.Buttons[i] = pressed[i];
            GamepadButton button = (GamepadButton)i;
            Log("Button " + button + (pressed[i] ? " pressed" : " released"));
            if (pressed[i]) Post(() => scene.GamepadPress(button, now));
            else Post(() => scene.GamepadRelease(button, now));
        }
        if (Math.Abs(l2 - last.LeftTrigger) > 0.004)
        {
            last.LeftTrigger = l2;
            Post(() => scene.GamepadTrigger(true, l2, now));
        }
        if (Math.Abs(r2 - last.RightTrigger) > 0.004)
        {
            last.RightTrigger = r2;
            Post(() => scene.GamepadTrigger(false, r2, now));
        }
        if ((leftStick - last.LeftStick).LengthSquared > 0.0004)
        {
            last.LeftStick = leftStick;
            Post(() => scene.GamepadStick(true, leftStick, now));
        }
        if ((rightStick - last.RightStick).LengthSquared > 0.0004)
        {
            last.RightStick = rightStick;
            Post(() => scene.GamepadStick(false, rightStick, now));
        }
    }

    private const int DigcfPresent = 0x00000002;
    private const int DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    // All hid.dll BOOLEAN returns are 1 byte; marshal them as U1 so
    // only the low byte is read. (A plain bool would read a 4-byte BOOL.)
    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);
    // HidP_GetCaps (note the P: the HidD_* functions are device-level,
    // the HidP_* functions parse preparsed data). Returns NTSTATUS:
    // 0 (STATUS_SUCCESS) on success, so the return is a plain int.
    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
    // CharSet.Auto is required: without it the ANSI version is called and
    // writes a 1-byte-per-char path, but GetDevicePath reads it with
    // PtrToStringAuto (Unicode), producing mojibake that CreateFile then
    // rejects with ERROR_FILE_NOT_FOUND. That was the whole bug: every
    // device path was garbage, so no device — DualSense included — could
    // ever be opened.
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr handle, byte[] buffer, int bytesToRead, out int bytesRead, IntPtr overlapped);
}
