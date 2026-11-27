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
[assembly: AssemblyVersion("1.13.3.0")]
internal static class Program
{
    internal static void SingleInstanceNames(string desktopName, out string mutexName, out string showEventName)
    {
        const string baseName = @"Local\KeycatDesktop.B48D8B";
        if (String.IsNullOrEmpty(desktopName) ||
            String.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase))
        {
            mutexName = baseName;
            showEventName = baseName + ".Show";
            return;
        }
        char[] chars = new char[Math.Min(desktopName.Length, 48)];
        for (int i = 0; i < chars.Length; i++)
        {
            char c = desktopName[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '-' || c == '_';
            chars[i] = ok ? c : '_';
        }
        mutexName = baseName + "." + new string(chars);
        showEventName = mutexName + ".Show";
    }

    [STAThread]
    public static void Main()
    {
        string mutexName, showEventName;
        SingleInstanceNames(WindowsInput.CurrentDesktopName(), out mutexName, out showEventName);
        bool first;
        using (var mutex = new Mutex(true, mutexName, out first))
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName))
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
    private readonly DispatcherTimer watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
    private readonly StyleMenu styleMenu = new StyleMenu();
    private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
    private readonly Drawing.Icon icon;
    private DispatcherTimer startupUpdateTimer;
    private IntPtr hwnd, inputBuffer;
    private Drawing.Rectangle cursorMonitor;
    private bool listening, menuOpen, suppressNextPetClick, renderSubscribed;
    private bool userHidden, fullscreenHidden, appliedClickThrough;
    private double lastFsCheck = -10, lastStatsSync = -10, lastStatsSave = 0;
    private TimeSpan lastRenderTime = TimeSpan.MinValue;
    private WardrobeWindow wardrobe;
    private GuideWindow guide;
    private int checkingUpdates;
    private double suppressClickUntil = -10;
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
        cat.PrivacyMode = settings.PrivacyMode;
        cat.FxLevel = settings.FxLevel;
        cat.DeskProp = settings.DeskProp;
        cat.BreakReminder = settings.BreakReminder;
        cat.TodayKeyCount = settings.TodayKeyCount;
        cat.TodayBurstCount = settings.TodayBurstCount;
        cat.Cat = settings.Cat;
        cat.Keyboard = settings.Keyboard;
        cat.Mouse = settings.Mouse;
        ApplySize();
        icon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        tray.Icon = icon;
        tray.Text = "键盘小黑猫 · 右键设置";
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { userHidden = false; fullscreenHidden = false; Reveal(); };
        menu.Font = new Drawing.Font("Microsoft YaHei UI", 9f);
        menu.Opening += BuildMenu;
        menu.Closed += delegate { menuOpen = false; };
        MouseLeftButtonDown += BeginDrag;
        MouseMove += ContinueDrag;
        MouseLeftButtonUp += delegate
        {
            suppressNextPetClick = false;
            suppressClickUntil = -10;
            if (!settings.Cat.IsPerson()) EndDrag();
        };
        LostMouseCapture += delegate { if (dragStart.HasValue) EndDrag(); };
        MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
        {
            officeHeadPressed = false;
            suppressNextPetClick = false;
            suppressClickUntil = -10;
            // With no background input (for example on a remote desktop), let
            // the menu receive native focus-change dismissal.
            if (!listening) WindowsInput.SetForegroundWindow(hwnd);
            menu.Show(Forms.Cursor.Position); e.Handled = true;
        };
        Cursor = Cursors.Hand;
        watchdogTimer.Tick += delegate
        {
            if (IsVisible) UpdateFrameLoop();
            else Tick();
        };
        IsVisibleChanged += delegate { UpdateFrameLoop(); };
        Closed += delegate { CleanUp(); };
    }

    internal CatScene Scene { get { return cat; } }
    internal Preferences SettingsSnapshot { get { SyncStatsToSettings(); return settings; } }
    internal bool SkipPersistenceForTest { get; set; }
    internal bool IsMenuOpen { get { return menuOpen; } }
    internal bool SuppressClickActive { get { return suppressNextPetClick && clock.Elapsed.TotalSeconds < suppressClickUntil; } }
    internal void SetMenuOpenForTest(bool open) { menuOpen = open; }
    internal void SetHwndForTest(IntPtr testHwnd) { hwnd = testHwnd; }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        hwnd = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(hwnd);
        source.AddHook(Messages);
        ApplyStyle();
        inputBuffer = Marshal.AllocHGlobal(WindowsInput.BufferCapacity);
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
        UpdateFrameLoop();
        ScheduleStartupUpdateCheck();
        ScheduleStartupGuide();
    }

    private void ScheduleStartupGuide()
    {
        if (settings.HasCompletedGuide || Environment.GetEnvironmentVariable("KEYCAT_SKIP_GUIDE") == "1") return;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
        {
            if (!settings.HasCompletedGuide) ShowGuideWindow();
        }));
    }

    private void ScheduleStartupUpdateCheck()
    {
        if (!settings.AutoCheckUpdates || Environment.GetEnvironmentVariable("KEYCAT_NO_AUTO_UPDATE") == "1") return;
        if (settings.LastUpdateCheckUtcTicks > 0)
        {
            TimeSpan elapsed = DateTime.UtcNow - new DateTime(settings.LastUpdateCheckUtcTicks, DateTimeKind.Utc);
            if (elapsed.TotalHours < 24) return;
        }
        startupUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        startupUpdateTimer.Tick += delegate
        {
            startupUpdateTimer.Stop();
            startupUpdateTimer = null;
            TriggerUpdateCheck(true);
        };
        startupUpdateTimer.Start();
    }

    private IntPtr Messages(IntPtr window, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (settings.Cat.IsPerson() && HandleOfficePointer(msg, wParam, lParam))
        {
            handled = true; return IntPtr.Zero;
        }
        if (msg == 0xFF && listening && inputBuffer != IntPtr.Zero)
        {
            if (WindowsInput.CheckBossHotkey(lParam, inputBuffer))
            {
                ToggleUserVisibility();
                handled = true;
                return IntPtr.Zero;
            }
            if (menuOpen)
            {
                if (menu.Visible && WindowsInput.IsMousePress(lParam, inputBuffer))
                {
                    WindowsInput.ScreenPoint pt;
                    if (WindowsInput.GetCursorPos(out pt))
                        DismissMenuOutside(pt);
                    else
                    {
                        int packed = WindowsInput.GetMessagePos();
                        DismissMenuOutside(new WindowsInput.ScreenPoint { X = (short)packed, Y = (short)(packed >> 16) });
                    }
                }
            }
            else if (IsVisible) WindowsInput.Process(lParam, inputBuffer, cat, clock.Elapsed.TotalSeconds);
        }
        if (msg == 0x1C && wParam == IntPtr.Zero && menu.Visible) // WM_ACTIVATEAPP
            menu.Close(Forms.ToolStripDropDownCloseReason.AppFocusChange);
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (msg == 0x7E) Dispatcher.BeginInvoke(new Action(delegate
        {
            cursorMonitor = Drawing.Rectangle.Empty;
            if (!InView()) { DockToCorner(); Save(); }
        }));
        return IntPtr.Zero;
    }

    internal bool DismissMenuOutside(WindowsInput.ScreenPoint point, Func<WindowsInput.ScreenPoint, IntPtr> windowFinder = null)
    {
        if (windowFinder == null && PointInsideDropDown(menu, point)) return false;
        IntPtr hit = (windowFinder ?? WindowsInput.WindowFromPoint)(point);
        if (hit == hwnd)
        {
            suppressNextPetClick = true;
            suppressClickUntil = clock.Elapsed.TotalSeconds + 0.25;
            if (menu.Visible) menu.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
            menuOpen = false;
            return true;
        }
        // A submenu is a separate top-level HWND. Its OwnerItem leads back to
        // the root menu; a hosted scale slider first leads to its parent control.
        Forms.Control control = Forms.Control.FromChildHandle(hit);
        while (control != null)
        {
            if (control == menu) return false;
            var popup = control as Forms.ToolStripDropDown;
            control = popup != null && popup.OwnerItem != null ? popup.OwnerItem.Owner : control.Parent;
        }
        if (menu.Visible) menu.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
        menuOpen = false;
        return true;
    }

    private static bool PointInsideDropDown(Forms.ToolStripDropDown dropDown, WindowsInput.ScreenPoint point)
    {
        if (dropDown == null || !dropDown.Visible) return false;
        if (dropDown.Bounds.Contains(point.X, point.Y)) return true;
        foreach (Forms.ToolStripItem item in dropDown.Items)
        {
            var mi = item as Forms.ToolStripDropDownItem;
            if (mi != null && mi.HasDropDownItems && PointInsideDropDown(mi.DropDown, point))
                return true;
        }
        return false;
    }

    private void ApplyStyle()
    {
        ApplyStyle(settings.ClickThrough);
    }

    private void ApplyStyle(bool effectiveClickThrough)
    {
        appliedClickThrough = effectiveClickThrough;
        if (hwnd == IntPtr.Zero) return;
        int style = WindowsInput.GetWindowLong(hwnd, -20) | 0x08000000 | 0x80;
        style = effectiveClickThrough ? style | 0x20 : style & ~0x20;
        WindowsInput.SetWindowLong(hwnd, -20, style);
    }

    private void SyncStatsToSettings()
    {
        if (settings.EnsureToday(DateTime.Now))
        {
            cat.TodayKeyCount = 0;
            cat.TodayBurstCount = 0;
        }
        else
        {
            settings.TodayKeyCount = cat.TodayKeyCount;
            settings.TodayBurstCount = cat.TodayBurstCount;
        }
    }

    internal void ToggleUserVisibility()
    {
        if (userHidden || !IsVisible)
        {
            userHidden = false;
            fullscreenHidden = false;
            Reveal();
        }
        else
        {
            userHidden = true;
            fullscreenHidden = false;
            Hide();
            cat.ClearInput(clock.Elapsed.TotalSeconds);
        }
    }

    internal string BuildDiagnosticsText()
    {
        SyncStatsToSettings();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "键盘小黑猫 v{0}\r\nOS: {1}\r\n角色: {2} | 键盘: {3} | 鼠标: {4} | 装饰: {5}\r\n" +
            "缩放: {6:0.#}% | 动效: {7} | 隐私打码: {8} | 全屏避让: {9} | 鼠标穿透: {10}\r\n" +
            "监听状态: {11} | 今日统计: {12} 击键 / {13} 暴走",
            KeycatCore.KC_APP_VERSION, Environment.OSVersion.VersionString,
            StyleMenu.TitleOf(settings.Cat), settings.Keyboard, settings.Mouse, settings.DeskProp,
            settings.Scale * 100.0, settings.FxLevel == KeycatCore.KC_FX_QUIET ? "柔和" : "高能",
            settings.PrivacyMode, settings.AutoHideFullscreen, settings.ClickThrough,
            listening ? "正常" : "未启动", cat.TodayKeyCount, cat.TodayBurstCount);
    }

    internal void ShowGuideWindow()
    {
        if (guide == null)
        {
            guide = new GuideWindow(this);
            guide.Closed += delegate { guide = null; };
        }
        guide.SyncPreviewFromOwner();
        if (!guide.IsVisible) guide.Show();
        guide.Activate();
    }

    internal void MarkGuideCompleted()
    {
        if (settings.HasCompletedGuide) return;
        settings.HasCompletedGuide = true;
        Save();
    }

    internal void OpenWardrobeFromGuide()
    {
        ShowWardrobe();
    }

    private void ShowWardrobe()
    {
        if (wardrobe == null) {
            wardrobe = new WardrobeWindow(this);
            wardrobe.Closed += delegate { wardrobe = null; };
        }
        wardrobe.RefreshState();
        if (!wardrobe.IsVisible) wardrobe.Show();
        wardrobe.Activate();
    }

    internal void ApplyWardrobeCharacter(CatStyle next)
    {
        CatStyle previous = settings.Cat;
        settings.Cat = cat.Cat = next;
        if (previous.IsPerson() || next.IsPerson()) cat.ResetOfficeInteraction();
        cat.InvalidateVisual();
        Save();
    }

    internal void ApplyWardrobeKeyboard(KeyboardStyle next)
    {
        SelectDevices(next, settings.Mouse);
    }

    internal void ApplyWardrobeMouse(MouseStyle next)
    {
        SelectDevices(settings.Keyboard, next);
    }

    internal void ApplyWardrobeDevices(KeyboardStyle kb, MouseStyle ms)
    {
        SelectDevices(kb, ms);
    }

    internal void ApplyWardrobeDeskProp(int prop)
    {
        settings.DeskProp = cat.DeskProp = prop;
        Save();
    }

    internal void ApplyWardrobeFxLevel(int fx)
    {
        settings.FxLevel = cat.FxLevel = fx;
        Save();
    }

    internal void ApplyWardrobeScale(double scale)
    {
        var anchor = new Point(Left + Width, Top + Height);
        settings.Scale = Math.Max(Preferences.MinPercent / 100.0, Math.Min(Preferences.MaxPercent / 100.0, scale));
        ApplySize();
        Left = anchor.X - Width;
        Top = anchor.Y - Height;
        if (!InView()) DockToCorner();
        Save();
    }

    internal void ApplyWardrobeToggle(Action<Preferences> update)
    {
        update(settings);
        cat.LabelsEnabled = settings.Labels;
        cat.PrivacyMode = settings.PrivacyMode;
        cat.BreakReminder = settings.BreakReminder;
        if (!settings.Labels) cat.ClearLabels();
        Topmost = settings.OnTop;
        ApplyStyle(settings.ClickThrough);
        if (!settings.AutoHideFullscreen && fullscreenHidden && !userHidden)
        {
            fullscreenHidden = false;
            Reveal();
        }
        Save();
    }

    internal void ToggleAutoStartFromWardrobe()
    {
        ToggleAutoStart();
    }
    private void ApplySize()
    {
        double s = settings.Scale;
        Width = KeycatCore.KC_WINDOW_WIDTH * s; Height = KeycatCore.KC_WINDOW_HEIGHT * s;
        cat.Width = KeycatCore.KC_SCENE_WIDTH * s; cat.Height = KeycatCore.KC_SCENE_HEIGHT * s;
        Canvas.SetLeft(cat, -KeycatCore.KC_SCENE_OFFSET_X * s); Canvas.SetTop(cat, -KeycatCore.KC_SCENE_OFFSET_Y * s);
    }

    private void UpdateFrameLoop()
    {
        bool active = IsVisible && hwnd != IntPtr.Zero;
        if (active && !renderSubscribed)
        {
            lastRenderTime = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
            renderSubscribed = true;
        }
        else if (!active && renderSubscribed)
        {
            CompositionTarget.Rendering -= OnRendering;
            renderSubscribed = false;
        }
        bool needWatchdog = !IsVisible && hwnd != IntPtr.Zero && !userHidden && settings.AutoHideFullscreen;
        if (needWatchdog)
        {
            if (!watchdogTimer.IsEnabled) watchdogTimer.Start();
        }
        else if (watchdogTimer.IsEnabled)
        {
            watchdogTimer.Stop();
        }
    }

    private void OnRendering(object sender, EventArgs e)
    {
        var args = e as RenderingEventArgs;
        if (args != null)
        {
            if (args.RenderingTime == lastRenderTime) return;
            lastRenderTime = args.RenderingTime;
        }
        Tick();
    }

    internal void Tick(Func<WindowsInput.ScreenPoint?> cursorSampler = null)
    {
        double now = clock.Elapsed.TotalSeconds;
        if (now - lastStatsSync >= 1.0)
        {
            lastStatsSync = now;
            SyncStatsToSettings();
        }
        if (now - lastStatsSave >= 30.0)
        {
            lastStatsSave = now;
            Save();
        }
        if (cursorSampler == null && hwnd != IntPtr.Zero && !userHidden && settings.AutoHideFullscreen && now - lastFsCheck >= 0.45)
        {
            lastFsCheck = now;
            bool wardrobeActive = wardrobe != null && wardrobe.IsVisible;
            bool guideActive = guide != null && guide.IsVisible;
            bool fs = !menuOpen && !wardrobeActive && !guideActive && WindowsInput.IsOtherAppFullscreen(hwnd);
            if (fs && !fullscreenHidden)
            {
                fullscreenHidden = true;
                Hide();
                cat.ClearInput(now);
            }
            else if (!fs && fullscreenHidden)
            {
                fullscreenHidden = false;
                Reveal();
            }
        }
        if (!IsVisible) return;
        if (cursorSampler == null && hwnd != IntPtr.Zero)
        {
            bool altHeld = settings.ClickThrough && WindowsInput.GetAsyncKeyState(0x12) < 0;
            bool desiredThrough = settings.ClickThrough && !altHeld && !dragging && !menuOpen;
            if (desiredThrough != appliedClickThrough)
                ApplyStyle(desiredThrough);
        }
        if (!menuOpen)
        {
            WindowsInput.ScreenPoint screen;
            bool hasCursor;
            if (cursorSampler != null)
            {
                WindowsInput.ScreenPoint? sampled = cursorSampler();
                hasCursor = sampled.HasValue;
                screen = sampled ?? default(WindowsInput.ScreenPoint);
            }
            else
            {
                hasCursor = WindowsInput.GetCursorPos(out screen);
            }
            if (hasCursor)
            {
                Point p = PresentationSource.FromVisual(cat) != null
                    ? cat.PointFromScreen(new Point(screen.X, screen.Y))
                    : new Point(screen.X, screen.Y);
                if (!cursorMonitor.Contains(screen.X, screen.Y))
                    cursorMonitor = Forms.Screen.FromPoint(new Drawing.Point(screen.X, screen.Y)).Bounds;
                Vector onMonitor = new Vector(
                    2.0 * (screen.X - cursorMonitor.Left) / Math.Max(1, cursorMonitor.Width - 1) - 1,
                    2.0 * (screen.Y - cursorMonitor.Top) / Math.Max(1, cursorMonitor.Height - 1) - 1);
                bool leftDown = cursorSampler == null && WindowsInput.GetAsyncKeyState(1) < 0;
                bool rightDown = cursorSampler == null && WindowsInput.GetAsyncKeyState(2) < 0;
                bool canHover = !settings.Cat.IsPerson() ||
                    (!menuOpen && !dragging && !appliedClickThrough && WindowsInput.WindowFromPoint(screen) == hwnd);
                cat.Pointer(new Point(p.X / settings.Scale, p.Y / settings.Scale), leftDown, rightDown, now, onMonitor, canHover);
            }
        }
        cat.Advance(now);
    }

    private Point CursorInDips()
    {
        var p = Forms.Cursor.Position;
        return source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
    }
    private bool TryDismissBreakBubble(Point clientDip)
    {
        if (cat.BreakOpacityForTest <= 0.05) return false;
        double sx = KeycatCore.KC_SCENE_OFFSET_X + clientDip.X / settings.Scale;
        double sy = KeycatCore.KC_SCENE_OFFSET_Y + clientDip.Y / settings.Scale;
        if (sx >= 278.0 && sx <= 376.0 && sy >= 14.0 && sy <= 46.0)
        {
            cat.DismissBreak();
            return true;
        }
        return false;
    }

    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        if (menuOpen)
        {
            if (menu.Visible) menu.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
            menuOpen = false;
            e.Handled = true;
            return;
        }
        if (SuppressClickActive)
        {
            suppressNextPetClick = false;
            e.Handled = true;
            return;
        }
        if (appliedClickThrough) return;
        if (TryDismissBreakBubble(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }
        if (settings.Cat.IsPerson()) return;
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
        if (appliedClickThrough || source == null) return false;
        if (menuOpen)
        {
            if (startsGesture)
            {
                if (menu.Visible) menu.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
                menuOpen = false;
                suppressNextPetClick = true;
                suppressClickUntil = clock.Elapsed.TotalSeconds + 0.25;
                return true;
            }
            return false;
        }
        if (startsGesture && SuppressClickActive)
        {
            suppressNextPetClick = false;
            return true;
        }
        if (msg == Up && SuppressClickActive)
        {
            suppressNextPetClick = false;
            suppressClickUntil = -10;
            return true;
        }

        // Use the point delivered with this button message. The global cursor
        // sample may already be elsewhere when a quick press reaches the window.
        int packed = unchecked((int)position.ToInt64());
        Point client = source.CompositionTarget.TransformFromDevice.Transform(
            new Point((short)(packed & 0xffff), (short)(packed >> 16)));
        if (startsGesture && TryDismissBreakBubble(client)) return true;
        Point screen = source.CompositionTarget.TransformFromDevice.Transform(PointToScreen(client));
        bool head = OfficeColleagueArt.For(settings.Cat).ContainsHead(
            new Point(KeycatCore.KC_SCENE_OFFSET_X + client.X / settings.Scale,
                      KeycatCore.KC_SCENE_OFFSET_Y + client.Y / settings.Scale));
        bool rightPressed = (buttons.ToInt64() & 2) != 0;
        if (startsGesture)
        {
            WindowsInput.SetCapture(hwnd);
            if (WindowsInput.GetCapture() == hwnd)
            {
                dragStart = screen; dragOrigin = new Point(Left, Top);
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
                           !rightPressed && !dragging && !BeyondDragThreshold(screen - dragStart.Value);
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
        fullscreenHidden = false;
        userHidden = false;
        if (!IsVisible) Show();
        if (!InView()) DockToCorner();
        cat.InvalidateVisual();
        UpdateFrameLoop();
    }
    private void Save()
    {
        if (SkipPersistenceForTest) return;
        SyncStatsToSettings();
        settings.X = Left; settings.Y = Top; settings.Save();
    }
    private static Forms.ToolStripMenuItem Item(string text, bool selected, Action action)
    {
        var item = new KeycatMenuItem(text) { Checked = selected };
        item.Click += delegate { action(); };
        return item;
    }
    private Forms.ToolStripMenuItem SizeMenu()
    {
        var anchor = new Point(Left + Width, Top + Height);
        bool resized = false;
        string title = settings.Cat.IsPerson() ? "角色大小" : "小猫大小";
        var size = new KeycatMenuItem("调整尺寸") { Tag = string.Format("{0:0.#}%", settings.Scale * 100.0) };
        var slider = new SleekScaleControl(settings.Scale, title);
        size.DropDownItems.Add(new Forms.ToolStripControlHost(slider) { Padding = Forms.Padding.Empty, Margin = Forms.Padding.Empty });
        size.DropDownOpened += delegate
        {
            anchor = new Point(Left + Width, Top + Height);
            slider.Focus();
        };
        size.DropDown.Closing += delegate(object owner, Forms.ToolStripDropDownClosingEventArgs args)
        {
            if (args.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked) args.Cancel = true;
        };
        slider.ScaleChanged += delegate
        {
            size.Tag = slider.PercentageText;
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

    private MessageBoxResult ShowTopmostDialog(string text, MessageBoxButton buttons, MessageBoxImage iconType)
    {
        var dialogOwner = new Window
        {
            Title = Title,
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        try
        {
            dialogOwner.Show();
            dialogOwner.Activate();
            IntPtr ownerHwnd = new WindowInteropHelper(dialogOwner).Handle;
            if (ownerHwnd != IntPtr.Zero) WindowsInput.SetForegroundWindow(ownerHwnd);
            return MessageBox.Show(dialogOwner, text, Title, buttons, iconType);
        }
        finally
        {
            try { dialogOwner.Close(); } catch { }
        }
    }

    private void ToggleAutoStart()
    {
        bool target = !AutoStart.IsEnabled();
        try
        {
            AutoStart.SetEnabled(target);
        }
        catch (Exception ex)
        {
            ShowTopmostDialog("无法更改开机自启动设置：\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TriggerUpdateCheck(bool silent)
    {
        if (Interlocked.CompareExchange(ref checkingUpdates, 1, 0) != 0)
        {
            if (!silent)
                ShowTopmostDialog("正在检查更新，请稍候。", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!silent)
        {
            try { tray.ShowBalloonTip(2500, Title, "正在检查新版本…", Forms.ToolTipIcon.Info); } catch { }
        }
        var worker = new Thread(delegate()
        {
            GitHubRelease release = null;
            Exception error = null;
            try
            {
                release = Updater.FetchLatestRelease();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (error != null)
                {
                    Interlocked.Exchange(ref checkingUpdates, 0);
                    if (!silent)
                    {
                        MessageBoxResult openPage = ShowTopmostDialog(
                            "检查更新失败：" + error.Message + "\n\n是否打开 GitHub 发布页面手动下载？",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (openPage == MessageBoxResult.Yes)
                            Process.Start(new ProcessStartInfo(Updater.ReleasesPageUrl) { UseShellExecute = true });
                    }
                    return;
                }
                settings.LastUpdateCheckUtcTicks = DateTime.UtcNow.Ticks;
                Save();
                if (release == null || release.Draft || release.Prerelease ||
                    !Updater.IsNewerVersion(release.TagName, KeycatCore.KC_APP_VERSION))
                {
                    Interlocked.Exchange(ref checkingUpdates, 0);
                    if (!silent)
                        ShowTopmostDialog("当前已是最新版本（v" + KeycatCore.KC_APP_VERSION + "）。",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                PromptAndInstallUpdate(release);
            }));
        });
        worker.IsBackground = true;
        worker.Start();
    }

    private void PromptAndInstallUpdate(GitHubRelease release)
    {
        string exePath = AutoStart.CurrentExecutablePath();
        bool isInstalled = Updater.IsInstalledCopy(exePath);
        GitHubAsset asset = Updater.SelectWindowsAsset(release, isInstalled);
        GitHubAsset checksumAsset = Updater.SelectChecksumAsset(release);
        string summary = (release.Body ?? String.Empty).Trim();
        if (summary.Length > 300) summary = summary.Substring(0, 300) + "...";
        string prompt = "发现新版本 " + release.TagName + "（当前 v" + KeycatCore.KC_APP_VERSION + "）。";
        if (summary.Length > 0) prompt += "\n\n更新说明：\n" + summary;
        prompt += asset != null
            ? "\n\n是否立即下载并安装更新？"
            : "\n\n未找到直接可运行的 Windows 安装包，是否前往发布页面？";
        MessageBoxResult choice = ShowTopmostDialog(prompt, MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (choice != MessageBoxResult.Yes)
        {
            Interlocked.Exchange(ref checkingUpdates, 0);
            return;
        }
        if (asset == null)
        {
            Process.Start(new ProcessStartInfo(release.HtmlUrl ?? Updater.ReleasesPageUrl) { UseShellExecute = true });
            Interlocked.Exchange(ref checkingUpdates, 0);
            return;
        }
        try { tray.ShowBalloonTip(3000, Title, "正在下载 " + release.TagName + " 更新包…", Forms.ToolTipIcon.Info); } catch { }
        bool isInstallerAsset = asset.Name.IndexOf("安装包", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                asset.Name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0;
        var downloader = new Thread(delegate()
        {
            string downloaded = null;
            Exception error = null;
            try
            {
                downloaded = Updater.DownloadAssetToTemp(asset, checksumAsset);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Interlocked.Exchange(ref checkingUpdates, 0);
                if (error != null)
                {
                    MessageBoxResult openPage = ShowTopmostDialog(
                        "下载更新失败：" + error.Message + "\n\n是否打开发布页面手动下载？",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (openPage == MessageBoxResult.Yes)
                        Process.Start(new ProcessStartInfo(release.HtmlUrl ?? Updater.ReleasesPageUrl) { UseShellExecute = true });
                    return;
                }
                try
                {
                    Updater.LaunchDownloadedUpdate(downloaded, isInstallerAsset, exePath);
                    Close();
                }
                catch (Exception launchEx)
                {
                    ShowTopmostDialog("启动更新程序失败：\n" + launchEx.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }));
        });
        downloader.IsBackground = true;
        downloader.Start();
    }

    private static Forms.ToolStripMenuItem ItemWithTag(string text, bool selected, string badge, Action action)
    {
        var item = Item(text, selected, action);
        item.Tag = badge;
        return item;
    }

    private void BuildMenu(object sender, CancelEventArgs e)
    {
        if (settings.Cat.IsPerson() && dragStart.HasValue) EndDrag();
        menuOpen = true;
        SyncStatsToSettings();
        while (menu.Items.Count > 0) menu.Items[0].Dispose();

        menu.Padding = new Forms.Padding(2, 4, 2, 4);
        Drawing.Bitmap profileBmp = styleMenu.GetProfileAvatar(settings.Cat);
        var header = new ProfileHeaderMenuItem(
            profileBmp,
            StyleMenu.TitleOf(settings.Cat),
            StyleMenu.GearSummary(settings.Keyboard, settings.Mouse, settings.DeskProp),
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "今日 {0:N0} 击键 · {1} 暴走", cat.TodayKeyCount, cat.TodayBurstCount),
            ShowWardrobe);
        header.Disposed += delegate { profileBmp.Dispose(); };
        menu.Items.Add(header);
        menu.Items.Add(ItemWithTag("外观与桌搭面板…", false, "18角色", ShowWardrobe));
        menu.Items.Add(new Forms.ToolStripSeparator());

        Forms.ToolStripMenuItem chars = styleMenu.Characters(settings.Cat, ApplyWardrobeCharacter);
        chars.Tag = StyleMenu.TitleOf(settings.Cat);
        menu.Items.Add(chars);
        menu.Items.Add(styleMenu.Keyboards(settings.Keyboard, ApplyWardrobeKeyboard));
        menu.Items.Add(styleMenu.Mice(settings.Mouse, ApplyWardrobeMouse));
        menu.Items.Add(styleMenu.DeskProps(settings.DeskProp, ApplyWardrobeDeskProp));
        menu.Items.Add(SizeMenu());
        menu.Items.Add(styleMenu.FxLevels(settings.FxLevel, ApplyWardrobeFxLevel));
        menu.Items.Add(Item("一键苹果妙控套装", settings.Keyboard == KeyboardStyle.Apple && settings.Mouse == MouseStyle.Apple,
            () => SelectDevices(KeyboardStyle.Apple, MouseStyle.Apple)));

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("显示浮动键帽", settings.Labels, delegate
        {
            settings.Labels = cat.LabelsEnabled = !settings.Labels;
            cat.ClearLabels(); Save();
        }));
        menu.Items.Add(ItemWithTag("键帽隐私打码 (••)", settings.PrivacyMode, "防泄密", delegate
        {
            settings.PrivacyMode = cat.PrivacyMode = !settings.PrivacyMode; Save();
        }));
        menu.Items.Add(ItemWithTag("鼠标穿透", settings.ClickThrough, "Alt 可拖", delegate
        {
            settings.ClickThrough = !settings.ClickThrough; ApplyStyle(); Save();
        }));
        menu.Items.Add(Item("保持窗口置顶", settings.OnTop, delegate { Topmost = settings.OnTop = !settings.OnTop; Save(); }));

        var more = new KeycatMenuItem("更多习惯与系统") { Tag = "设置/诊断" };
        more.DropDownItems.Add(ItemWithTag("全屏游戏/视频自动隐身", settings.AutoHideFullscreen, "自动避让", delegate
        {
            settings.AutoHideFullscreen = !settings.AutoHideFullscreen;
            if (!settings.AutoHideFullscreen && fullscreenHidden && !userHidden)
            {
                fullscreenHidden = false; Reveal();
            }
            Save();
        }));
        more.DropDownItems.Add(ItemWithTag("久坐喝水休息提醒", settings.BreakReminder, "45分钟", delegate
        {
            settings.BreakReminder = cat.BreakReminder = !settings.BreakReminder; Save();
        }));
        more.DropDownItems.Add(Item("开机自启动", AutoStart.IsEnabled(), ToggleAutoStart));
        more.DropDownItems.Add(new Forms.ToolStripSeparator());
        more.DropDownItems.Add(Item("回到屏幕右下角", false, delegate { DockToCorner(); Reveal(); Save(); }));
        more.DropDownItems.Add(Item("复制诊断信息", false, delegate
        {
            try
            {
                Clipboard.SetText(BuildDiagnosticsText());
                tray.ShowBalloonTip(2000, Title, "已复制版本与状态信息到剪贴板。", Forms.ToolTipIcon.Info);
            }
            catch { }
        }));
        more.DropDownItems.Add(Item(Interlocked.CompareExchange(ref checkingUpdates, 0, 0) != 0 ? "正在检查更新…" : "检查更新…", false, delegate { TriggerUpdateCheck(false); }));
        menu.Items.Add(more);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(ItemWithTag("新手使用引导…", false, "3步交互", ShowGuideWindow));
        bool office = settings.Cat.IsPerson();
        menu.Items.Add(ItemWithTag(IsVisible ? "暂时隐藏" : (office ? "显示角色" : "显示小猫"), false, "Ctrl+Alt+H", ToggleUserVisibility));
        menu.Items.Add(Item(office ? "退出桌宠" : "退出小黑猫", false, Close));

        KeycatMenuRenderer.ApplyTo(menu);
        KeycatMenuRenderer.NormalizeRootWidths(menu, 292);
        e.Cancel = false;
    }
    private void CleanUp()
    {
        if (wardrobe != null) { try { wardrobe.Close(); } catch { } wardrobe = null; }
        if (guide != null) { try { guide.Close(); } catch { } guide = null; }
        if (startupUpdateTimer != null) { startupUpdateTimer.Stop(); startupUpdateTimer = null; }
        if (renderSubscribed) { CompositionTarget.Rendering -= OnRendering; renderSubscribed = false; }
        watchdogTimer.Stop(); Save();
        if (listening) { listening = false; try { WindowsInput.Listen(hwnd, false); } catch (Win32Exception) { } }
        if (source != null) source.RemoveHook(Messages);
        if (inputBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(inputBuffer); inputBuffer = IntPtr.Zero; }
        tray.Visible = false; tray.Dispose(); menu.Dispose(); styleMenu.Dispose(); if (icon != null) icon.Dispose();
    }
}
