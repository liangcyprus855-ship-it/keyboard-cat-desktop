using System;
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
    private static byte reportId = 0x01; // USB; Bluetooth uses 0x31
    private static bool styleAnnounced;

    private class PadState
    {
        public bool[] Buttons = new bool[16]; // indexed by GamepadButton
        public double LeftTrigger, RightTrigger;
        public Vector LeftStick, RightStick;
    }
    private static readonly PadState last = new PadState();

    public static bool IsAvailable { get { return connected; } }

    public static void Start(CatScene target)
    {
        if (running) return;
        scene = target;
        running = true;
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
                    styleAnnounced = false;
                    // Announce the PS5 style once per connection.
                    if (!styleAnnounced)
                    {
                        styleAnnounced = true;
                        Action announce = OnDualSenseConnected;
                        if (announce != null) Post(announce);
                    }
                }
                else
                {
                    Thread.Sleep(500);
                    continue;
                }
            }

            // HidD_GetInputReport needs the wanted report id in byte 0.
            // A USB DualSense answers 0x01, a Bluetooth one 0x31; try
            // the current id first, then the other one.
            bool ok = false;
            try
            {
                report[0] = reportId;
                ok = HidD_GetInputReport(deviceHandle, report, report.Length);
                if (!ok && reportId == 0x01)
                {
                    reportId = 0x31;
                    report[0] = reportId;
                    ok = HidD_GetInputReport(deviceHandle, report, report.Length);
                }
                else if (!ok && reportId == 0x31)
                {
                    reportId = 0x01;
                    report[0] = reportId;
                    ok = HidD_GetInputReport(deviceHandle, report, report.Length);
                }
            }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                // Device gone or read failed: drop it and re-enumerate.
                CloseDevice();
                connected = false;
                lock (last) { Array.Clear(last.Buttons, 0, last.Buttons.Length); }
                Thread.Sleep(200);
                continue;
            }

            ParseAndDispatch(report);
            Thread.Sleep(16);
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
        if (infoSet == IntPtr.Zero || infoSet == new IntPtr(-1)) return false;
        try
        {
            SP_DEVICE_INTERFACE_DATA iface = new SP_DEVICE_INTERFACE_DATA();
            iface.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            for (int index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref iface))
                    return false;
                string path = GetDevicePath(infoSet, ref iface);
                if (path == null) continue;
                IntPtr handle = CreateFile(path, GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                try
                {
                    HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                    attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                    if (!HidD_GetAttributes(handle, ref attr)) continue;
                    if (attr.VendorID != SonyVendorId) continue;
                    if (attr.ProductID != DualSenseProductId
                        && attr.ProductID != DualSenseEdgeProductId) continue;
                    IntPtr preparsed = IntPtr.Zero;
                    if (!HidD_GetPreparsedData(handle, out preparsed)) continue;
                    try
                    {
                        HIDP_CAPS caps = new HIDP_CAPS();
                        if (HidD_GetCaps(preparsed, ref caps) != 0) continue;
                        int len = caps.InputReportByteLength;
                        if (len < 32) continue;
                        deviceHandle = handle;
                        reportLength = len;
                        return true;
                    }
                    finally { HidD_FreePreparsedData(preparsed); }
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
            // cbSize is 8 on 64-bit, 4 + char size on 32-bit; the path
            // string starts right after it.
            int header = IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize;
            Marshal.WriteInt32(buffer, header);
            int got;
            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref iface, buffer, required, out got, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringAuto(new IntPtr(buffer.ToInt64() + header));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void ParseAndDispatch(byte[] report)
    {
        if (report == null || report.Length < 11) return;
        int offset;
        if (report[0] == 0x01) offset = 0;          // USB
        else if (report[0] == 0x31) offset = 1;    // Bluetooth: payload +1
        else return;
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

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidD_GetCaps(IntPtr preparsedData, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetInputReport(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
