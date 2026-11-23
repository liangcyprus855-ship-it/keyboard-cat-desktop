using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("键盘小黑猫")]
[assembly: AssemblyDescription("会跟着键盘和鼠标一起动的小黑猫桌宠")]
[assembly: AssemblyVersion("1.9.1.0")]
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        bool first;
        using (var mutex = new Mutex(true, @"Local\KeycatDesktop.B48D8B", out first))
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KeycatDesktop.B48D8B.Show"))
        {
            if (!first) { show.Set(); return; }
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var pet = new PetWindow();
            RegisteredWaitHandle wake = ThreadPool.RegisterWaitForSingleObject(show,
                delegate { pet.Dispatcher.BeginInvoke(new Action(pet.Reveal)); }, null, Timeout.Infinite, false);
            try { app.Run(pet); }
            finally { wake.Unregister(null); mutex.ReleaseMutex(); }
        }
    }
}

internal sealed class PetWindow : Window
{
    private readonly Preferences settings = Preferences.Load();
    private readonly CatScene cat = new CatScene();
    private readonly Canvas stage = new Canvas();
    private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
    private readonly StyleMenu styleMenu = new StyleMenu();
    private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
    private readonly Drawing.Icon icon;
    private IntPtr hwnd, inputBuffer;
    private Drawing.Rectangle cursorMonitor;
    private bool listening, menuOpen;
    private Point? dragStart;
    private Point dragOrigin;
    private bool dragging, officeHeadPressed;
    private HwndSource source;

    public PetWindow()
    {
        Title = "键盘小黑猫";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = settings.OnTop;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        Content = stage;
        stage.ClipToBounds = true;
        stage.Children.Add(cat);
        cat.LabelsEnabled = settings.Labels;
        cat.Cat = settings.Cat;
        cat.Keyboard = settings.Keyboard;
        cat.Mouse = settings.Mouse;
        ApplySize();
        icon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        tray.Icon = icon;
        tray.Text = "键盘小黑猫 · 右键设置";
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { Reveal(); };
        menu.Font = new Drawing.Font("Microsoft YaHei UI", 9f);
        menu.Opening += BuildMenu;
        menu.Closed += delegate { menuOpen = false; };
        MouseLeftButtonDown += BeginDrag;
        MouseMove += ContinueDrag;
        MouseLeftButtonUp += delegate { if (!settings.Cat.IsPerson()) EndDrag(); };
        LostMouseCapture += delegate { if (dragStart.HasValue) EndDrag(); };
        MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
        {
            officeHeadPressed = false;
            menu.Show(Forms.Cursor.Position); e.Handled = true;
        };
        Cursor = Cursors.Hand;
        timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 40);
        timer.Tick += delegate { Tick(); };
        Closed += delegate { CleanUp(); };
    }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        hwnd = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(hwnd);
        source.AddHook(Messages);
        ApplyStyle();
        inputBuffer = Marshal.AllocHGlobal(128);
        tray.Visible = true;
        try { WindowsInput.Listen(hwnd, true); listening = true; }
        catch (Win32Exception e)
        {
            tray.ShowBalloonTip(4500, Title, "键鼠输入监听未能启动。请退出后重开。\n" + e.Message, Forms.ToolTipIcon.Warning);
        }
        if (Preferences.Finite(settings.X) && Preferences.Finite(settings.Y))
        {
            Left = settings.X; Top = settings.Y;
            if (!InView()) DockToCorner();
        }
        else DockToCorner();
        timer.Start();
    }

    private IntPtr Messages(IntPtr window, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (settings.Cat.IsPerson() && HandleOfficePointer(msg,wParam,lParam))
        {
            handled = true; return IntPtr.Zero;
        }
        if (msg == 0xFF && listening && IsVisible && !menuOpen && inputBuffer != IntPtr.Zero)
        {
            WindowsInput.Process(lParam, inputBuffer, cat, clock.Elapsed.TotalSeconds);
        }
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (msg == 0x7E) Dispatcher.BeginInvoke(new Action(delegate
        {
            cursorMonitor = Drawing.Rectangle.Empty;
            if (!InView()) DockToCorner();
        }));
        return IntPtr.Zero;
    }

    private void ApplyStyle()
    {
        int style = WindowsInput.GetWindowLong(hwnd, -20) | 0x08000000 | 0x80;
        style = settings.ClickThrough ? style | 0x20 : style & ~0x20;
        WindowsInput.SetWindowLong(hwnd, -20, style);
    }
    private void ApplySize()
    {
        double s = settings.Scale;
        Width = 306 * s; Height = (284 + DeskLayout.Forward) * s;
        cat.Width = 480 * s; cat.Height = 320 * s;
        Canvas.SetLeft(cat, -75 * s); Canvas.SetTop(cat, -9 * s);
    }
    private void Tick()
    {
        WindowsInput.ScreenPoint screen;
        double now = clock.Elapsed.TotalSeconds;
        if (WindowsInput.GetCursorPos(out screen))
        {
            Point p = cat.PointFromScreen(new Point(screen.X, screen.Y));
            if (!cursorMonitor.Contains(screen.X, screen.Y))
                cursorMonitor = Forms.Screen.FromPoint(new Drawing.Point(screen.X, screen.Y)).Bounds;
            Vector onMonitor = new Vector(
                2.0 * (screen.X - cursorMonitor.Left) / Math.Max(1, cursorMonitor.Width - 1) - 1,
                2.0 * (screen.Y - cursorMonitor.Top) / Math.Max(1, cursorMonitor.Height - 1) - 1);
            bool leftDown = WindowsInput.GetAsyncKeyState(1) < 0;
            bool rightDown = WindowsInput.GetAsyncKeyState(2) < 0;
            bool canHover = !settings.Cat.IsPerson() ||
                (!menuOpen && !dragging && !settings.ClickThrough && WindowsInput.WindowFromPoint(screen) == hwnd);
            cat.Pointer(new Point(p.X / settings.Scale, p.Y / settings.Scale), leftDown, rightDown, now, onMonitor, canHover);
        }
        cat.Advance(now);
    }

    private Point CursorInDips()
    {
        var p = Forms.Cursor.Position;
        return source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
    }
    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        if (settings.ClickThrough || settings.Cat.IsPerson()) return;
        dragStart = CursorInDips();
        dragOrigin = new Point(Left, Top);
        CaptureMouse(); e.Handled = true;
    }
    private bool HandleOfficePointer(int msg, IntPtr buttons, IntPtr position)
    {
        const int Down = 0x201, Up = 0x202, DoubleDown = 0x203, Move = 0x200;
        if (msg == 0x215 || msg == 0x1F) // capture changed or modal operation cancelled
        {
            if (dragStart.HasValue) EndDrag();
            return false;
        }
        if (msg == 0x204) officeHeadPressed = false;
        bool startsGesture = msg == Down || msg == DoubleDown;
        bool continuesGesture = dragStart.HasValue && (msg == Move || msg == Up);
        if (!startsGesture && !continuesGesture) return false;
        if (settings.ClickThrough || menuOpen || source == null) return false;

        // Use the point delivered with this button message. The global cursor
        // sample may already be elsewhere when a quick press reaches the window.
        int packed = unchecked((int)position.ToInt64());
        Point client = source.CompositionTarget.TransformFromDevice.Transform(
            new Point((short)(packed & 0xffff),(short)(packed >> 16)));
        Point screen = source.CompositionTarget.TransformFromDevice.Transform(PointToScreen(client));
        bool head = OfficeColleagueArt.For(settings.Cat).ContainsHead(new Point(75+client.X/settings.Scale,9+client.Y/settings.Scale));
        bool rightPressed = (buttons.ToInt64() & 2) != 0;
        if (startsGesture)
        {
            WindowsInput.SetCapture(hwnd);
            if (WindowsInput.GetCapture() == hwnd)
            {
                dragStart = screen; dragOrigin = new Point(Left,Top);
                officeHeadPressed = head && !rightPressed;
            }
        }
        else if (msg == Move)
        {
            if ((buttons.ToInt64() & 1) != 0) MoveDrag(screen);
        }
        else
        {
            bool clicked = WindowsInput.GetCapture() == hwnd && officeHeadPressed && head &&
                           !rightPressed && !dragging && !BeyondDragThreshold(screen-dragStart.Value);
            EndDrag();
            if (clicked) cat.ClickOfficeHead(clock.Elapsed.TotalSeconds);
        }
        return true;
    }
    private bool BeyondDragThreshold(Vector movement)
    {
        if (!settings.Cat.IsPerson()) return movement.Length >= 4;
        return Math.Abs(movement.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(movement.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }
    private void ContinueDrag(object sender, MouseEventArgs e)
    {
        if (settings.Cat.IsPerson() || !dragStart.HasValue || e.LeftButton != MouseButtonState.Pressed) return;
        MoveDrag(CursorInDips());
    }
    private void MoveDrag(Point screen)
    {
        Vector delta = screen - dragStart.Value;
        if (!dragging && !BeyondDragThreshold(delta)) return;
        dragging = true;
        Left = dragOrigin.X + delta.X; Top = dragOrigin.Y + delta.Y;
    }
    private void EndDrag()
    {
        dragStart = null; officeHeadPressed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        else if (settings.Cat.IsPerson() && WindowsInput.GetCapture() == hwnd)
            WindowsInput.ReleaseCapture();
        if (dragging) { dragging = false; if (!InView()) DockToCorner(); Save(); }
    }
    private Rect LogicalArea(Drawing.Rectangle r)
    {
        var transform = source.CompositionTarget.TransformFromDevice;
        return new Rect(transform.Transform(new Point(r.Left, r.Top)), transform.Transform(new Point(r.Right, r.Bottom)));
    }
    private bool InView()
    {
        if (!Preferences.Finite(Left) || !Preferences.Finite(Top)) return false;
        var center = new Point(Left + Width * .45, Top + Height * .48);
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            if (LogicalArea(screen.WorkingArea).Contains(center)) return true;
        return false;
    }
    private void DockToCorner()
    {
        Rect area = LogicalArea(Forms.Screen.PrimaryScreen.WorkingArea);
        Left = area.Right - Width - 16; Top = area.Bottom - Height - 12;
    }
    public void Reveal()
    {
        if (!IsVisible) Show();
        if (!InView()) DockToCorner();
        timer.Start();
    }
    private void Save()
    {
        settings.X = Left; settings.Y = Top; settings.Save();
    }
    private static Forms.ToolStripMenuItem Item(string text, bool selected, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text) { Checked = selected };
        item.Click += delegate { action(); };
        return item;
    }
    private Forms.ToolStripMenuItem SizeMenu()
    {
        var anchor = new Point(Left + Width, Top + Height);
        bool resized = false;
        string title = settings.Cat.IsPerson() ? "角色大小" : "小猫大小";
        var size = new Forms.ToolStripMenuItem(title);
        var slider = new ScaleTrackBar(settings.Scale, title);
        var valueLabel = new Forms.ToolStripLabel(slider.PercentageText);
        size.DropDownItems.Add(valueLabel);
        size.DropDownItems.Add(new Forms.ToolStripControlHost(slider));
        size.DropDownOpened += delegate
        {
            anchor = new Point(Left + Width, Top + Height);
            slider.Focus();
        };
        size.DropDown.Closing += delegate(object owner, Forms.ToolStripDropDownClosingEventArgs args)
        {
            if (args.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked) args.Cancel = true;
        };
        slider.ValueChanged += delegate
        {
            valueLabel.Text = slider.PercentageText;
            settings.Scale = slider.Value / 1000.0;
            ApplySize();
            Left = anchor.X - Width; Top = anchor.Y - Height;
            resized = true;
        };
        size.DropDownClosed += delegate
        {
            if (!resized) return;
            if (!InView()) DockToCorner();
            Save(); resized = false;
        };
        return size;
    }
    private void SelectDevices(KeyboardStyle keyboard, MouseStyle mouse)
    {
        settings.Keyboard = cat.Keyboard = keyboard;
        settings.Mouse = cat.Mouse = mouse;
        cat.InvalidateVisual();
        Save();
    }

    private void BuildMenu(object sender, CancelEventArgs e)
    {
        if (settings.Cat.IsPerson() && dragStart.HasValue) EndDrag();
        menuOpen = true;
        while (menu.Items.Count > 0) menu.Items[0].Dispose();
        menu.Items.Add(styleMenu.Characters(settings.Cat, delegate(CatStyle next)
        {
            if (settings.Cat.IsPerson() || next.IsPerson())
                cat.ResetOfficeInteraction();
            settings.Cat = cat.Cat = next;
            cat.InvalidateVisual(); Save();
        }));
        menu.Items.Add(SizeMenu());
        menu.Items.Add(styleMenu.Keyboards(settings.Keyboard, next => SelectDevices(next,settings.Mouse)));
        menu.Items.Add(styleMenu.Mice(settings.Mouse, next => SelectDevices(settings.Keyboard,next)));
        menu.Items.Add(Item("使用苹果键鼠套装", settings.Keyboard == KeyboardStyle.Apple && settings.Mouse == MouseStyle.Apple,
            () => SelectDevices(KeyboardStyle.Apple,MouseStyle.Apple)));
        menu.Items.Add(Item("显示浮动键帽", settings.Labels, delegate
        {
            settings.Labels = cat.LabelsEnabled = !settings.Labels;
            cat.ClearLabels(); Save();
        }));
        menu.Items.Add(Item("保持置顶", settings.OnTop, delegate { Topmost = settings.OnTop = !settings.OnTop; Save(); }));
        menu.Items.Add(Item("鼠标穿透", settings.ClickThrough, delegate
        {
            settings.ClickThrough = !settings.ClickThrough; ApplyStyle(); Save();
        }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("回到屏幕右下角", false, delegate { DockToCorner(); Reveal(); Save(); }));
        bool office = settings.Cat.IsPerson();
        menu.Items.Add(Item(IsVisible ? "暂时隐藏" : (office ? "显示角色" : "显示小猫"), false, delegate
        {
            if (IsVisible) { timer.Stop(); Hide(); cat.ClearLabels(); } else Reveal();
        }));
        menu.Items.Add(Item(office ? "退出桌宠" : "退出小黑猫", false, Close));
        e.Cancel = false;
    }
    private void CleanUp()
    {
        timer.Stop(); Save();
        if (listening) { listening = false; try { WindowsInput.Listen(hwnd, false); } catch (Win32Exception) { } }
        if (source != null) source.RemoveHook(Messages);
        if (inputBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(inputBuffer); inputBuffer = IntPtr.Zero; }
        tray.Visible = false; tray.Dispose(); menu.Dispose(); styleMenu.Dispose(); if (icon != null) icon.Dispose();
    }
}
