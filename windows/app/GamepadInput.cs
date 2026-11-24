using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

// Gamepad input via XInput (docs/GAMEPAD.md section 7).
//
// Polls the four XInput slots on a background thread at 60 Hz for
// connected pads and once a second for empty slots (XInputGetState on an
// empty slot is slow), then marshals the resulting button / stick /
// trigger events back to the UI thread, where CatScene applies them.
//
// If no XInput DLL is present, gamepad support is silently disabled and
// keyboard / mouse mode is unaffected. XInput cannot tell the pad's
// brand, so the Xbox / PS5 style always comes from the preference.
internal static class GamepadInput
{
    // XInput button masks (XINPUT_GAMEPAD_*).
    private const ushort Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008;
    private const ushort Start = 0x0010, Back = 0x0020, L3 = 0x0040, R3 = 0x0080;
    private const ushort LB = 0x0100, RB = 0x0200;
    private const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    private const int TriggerThreshold = 30;
    private const int LeftStickDeadzone = 7849, RightStickDeadzone = 8689;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    private delegate uint GetStateDelegate(uint userIndex, out XInputState state);
    private static GetStateDelegate getState;

    private static CatScene scene;
    private static Thread pollThread;
    private static volatile bool running;
    private static readonly Slot[] slots = new Slot[4];

    private class Slot
    {
        public bool Connected;
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
        public DateTime LastProbe = DateTime.MinValue;
    }

    public static bool IsAvailable { get { return getState != null; } }

    public static void Start(CatScene target)
    {
        if (running) return;
        scene = target;
        if (!LoadXInput()) return;
        for (int i = 0; i < 4; i++) slots[i] = new Slot();
        running = true;
        pollThread = new Thread(PollLoop) { IsBackground = true, Name = "GamepadInput" };
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
        scene = null;
    }

    private static bool LoadXInput()
    {
        if (getState != null) return true;
        // xinput1_4.dll (Windows 8+), then 1_3 (the common redistributable),
        // then 9_1_0 (the inbox fallback). All three export XInputGetState
        // with the same signature.
        foreach (string dll in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
        {
            IntPtr lib = LoadLibrary(dll);
            if (lib == IntPtr.Zero) continue;
            IntPtr proc = GetProcAddress(lib, "XInputGetState");
            if (proc == IntPtr.Zero) continue;
            getState = (GetStateDelegate)Marshal.GetDelegateForFunctionPointer(proc, typeof(GetStateDelegate));
            return true;
        }
        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    private static void PollLoop()
    {
        while (running)
        {
            for (uint i = 0; i < 4; i++) PollSlot(i);
            Thread.Sleep(16);
        }
    }

    private static void PollSlot(uint index)
    {
        Slot slot = slots[index];
        DateTime now = DateTime.UtcNow;
        if (!slot.Connected && (now - slot.LastProbe).TotalSeconds < 1) return;
        slot.LastProbe = now;
        XInputState state;
        uint result = getState(index, out state);
        if (result != 0)
        {
            // ERROR_DEVICE_NOT_CONNECTED (1167) is the normal "no pad" case.
            slot.Connected = false;
            return;
        }
        slot.Connected = true;
        if (state.PacketNumber == slot.PacketNumber) return;
        slot.PacketNumber = state.PacketNumber;
        XInputGamepad pad = state.Gamepad;
        DispatchButtons(slot, pad);
        DispatchTriggers(slot, pad);
        DispatchSticks(slot, pad);
    }

    private static void DispatchButtons(Slot slot, XInputGamepad pad)
    {
        ushort changed = (ushort)(slot.Buttons ^ pad.Buttons);
        if (changed == 0) return;
        ushort pressed = (ushort)(changed & pad.Buttons);
        ushort released = (ushort)(changed & slot.Buttons);
        slot.Buttons = pad.Buttons;
        foreach (var pair in new[]
        {
            new { Mask = A, Button = GamepadButton.A },
            new { Mask = B, Button = GamepadButton.B },
            new { Mask = X, Button = GamepadButton.X },
            new { Mask = Y, Button = GamepadButton.Y },
            new { Mask = Up, Button = GamepadButton.DUp },
            new { Mask = Down, Button = GamepadButton.DDown },
            new { Mask = Left, Button = GamepadButton.DLeft },
            new { Mask = Right, Button = GamepadButton.DRight },
            new { Mask = Start, Button = GamepadButton.Start },
            new { Mask = Back, Button = GamepadButton.Back },
            new { Mask = L3, Button = GamepadButton.LS },
            new { Mask = R3, Button = GamepadButton.RS },
            new { Mask = LB, Button = GamepadButton.LB },
            new { Mask = RB, Button = GamepadButton.RB },
        })
        {
            // Copy to locals: the lambdas below must capture this
            // iteration's values, not the shared loop variable.
            ushort mask = pair.Mask;
            GamepadButton button = pair.Button;
            if ((pressed & mask) != 0) Post(() => scene.GamepadPress(button, Now()));
            else if ((released & mask) != 0) Post(() => scene.GamepadRelease(button, Now()));
        }
    }

    private static void DispatchTriggers(Slot slot, XInputGamepad pad)
    {
        if (pad.LeftTrigger != slot.LeftTrigger)
        {
            slot.LeftTrigger = pad.LeftTrigger;
            byte value = pad.LeftTrigger;
            Post(() => scene.GamepadTrigger(true, value / 255.0, Now()));
        }
        if (pad.RightTrigger != slot.RightTrigger)
        {
            slot.RightTrigger = pad.RightTrigger;
            byte value = pad.RightTrigger;
            Post(() => scene.GamepadTrigger(false, value / 255.0, Now()));
        }
    }

    private static void DispatchSticks(Slot slot, XInputGamepad pad)
    {
        Vector left = ApplyDeadzone(pad.ThumbLX, pad.ThumbLY, LeftStickDeadzone);
        Vector right = ApplyDeadzone(pad.ThumbRX, pad.ThumbRY, RightStickDeadzone);
        if (pad.ThumbLX != slot.ThumbLX || pad.ThumbLY != slot.ThumbLY)
        {
            slot.ThumbLX = pad.ThumbLX;
            slot.ThumbLY = pad.ThumbLY;
            Post(() => scene.GamepadStick(true, left, Now()));
        }
        if (pad.ThumbRX != slot.ThumbRX || pad.ThumbRY != slot.ThumbRY)
        {
            slot.ThumbRX = pad.ThumbRX;
            slot.ThumbRY = pad.ThumbRY;
            Post(() => scene.GamepadStick(false, right, Now()));
        }
    }

    private static Vector ApplyDeadzone(short x, short y, int deadzone)
    {
        // XInput axes are -32768..32767, +Y is up (the pad's plane +y).
        double nx = x / 32768.0, ny = y / 32768.0;
        double length = Math.Sqrt(nx * nx + ny * ny);
        if (length * 32768 < deadzone) return new Vector(0, 0);
        return new Vector(nx, ny);
    }

    private static double Now()
    {
        return PetWindow.Now;
    }

    private static void Post(Action action)
    {
        CatScene target = scene;
        if (target == null || !running) return;
        target.Dispatcher.BeginInvoke(action);
    }
}
