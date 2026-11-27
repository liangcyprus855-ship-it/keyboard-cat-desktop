using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;

internal static class Tests
{
    private static void Assert(bool cond, string message)
    {
        if (!cond) throw new InvalidOperationException("FAIL: " + message);
    }

    private static void Near(double actual, double expected, double tol, string field)
    {
        Assert(Math.Abs(actual - expected) <= tol,
            field + " expected " + expected + ", got " + actual);
    }

    private static void TestCoreAndUnifiedDefaults()
    {
        Assert(KeycatCore.KC_DEFAULT_CAT == 0, "default cat is Black (0)");
        Assert(KeycatCore.KC_DEFAULT_KEYBOARD == 4, "default keyboard is Apple (4)");
        Assert(KeycatCore.KC_DEFAULT_MOUSE == 4, "default mouse is Apple (4)");

        var p = new Preferences();
        Assert(p.Cat == CatStyle.Black, "Preferences default Cat is Black");
        Assert(p.Keyboard == KeyboardStyle.Apple, "Preferences default Keyboard is Apple");
        Assert(p.Mouse == MouseStyle.Apple, "Preferences default Mouse is Apple");
        Assert(p.AutoCheckUpdates, "Preferences AutoCheckUpdates defaults to true");

        string dir = Path.Combine(Path.GetTempPath(), "keycat-win-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "preferences.xml");
            p.Scale = 1.35;
            p.LastUpdateCheckUtcTicks = 123456789L;
            p.Save(file);
            Preferences reloaded = Preferences.Load(file);
            Assert(reloaded.Cat == CatStyle.Black && reloaded.Keyboard == KeyboardStyle.Apple && reloaded.Mouse == MouseStyle.Apple,
                "Preferences preserves unified defaults");
            Near(reloaded.Scale, 1.35, 1e-6, "Preferences scale roundtrip");
            Assert(reloaded.LastUpdateCheckUtcTicks == 123456789L, "Preferences LastUpdateCheckUtcTicks roundtrip");

            File.WriteAllText(file, "<cat scale=\"99\" catStyle=\"Invalid\" />");
            Preferences clamped = Preferences.Load(file);
            Near(clamped.Scale, 2.0, 1e-6, "Preferences clamps scale to max 200%");
            Assert(clamped.Cat == CatStyle.Black, "Preferences falls back to unified default cat");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void TestSceneAndOfficeArmParity()
    {
        var scene = new CatScene();
        Assert(scene.Cat == CatStyle.Black && scene.Keyboard == KeyboardStyle.Apple && scene.Mouse == MouseStyle.Apple,
            "CatScene initializes with unified defaults");

        scene.Strike(65, 1.0);
        scene.Advance(1.1);
        scene.Cat = CatStyle.OfficeColleague;
        scene.ResetOfficeInteraction();
        scene.Advance(1.15);
        Assert(scene.CurrentOfficePoseValue == OfficePose.Idle,
            "Office colleague starts Idle after character switch");

        scene.Strike(65, 2.00);
        scene.Strike(66, 2.06);
        scene.Strike(67, 2.12);
        scene.Strike(68, 2.18);
        scene.Advance(2.20);
        Assert(scene.CurrentOfficePoseValue == OfficePose.Working, "Four rapid keys enter Working");
        Assert(scene.OfficeLeftActive && scene.OfficeRightActive, "Four rapid keys engage both hands");

        scene.Pointer(new Point(214, 105), false, false, 2.30, new Vector(0, 0), true);
        scene.Advance(6.0);
        Assert(scene.CurrentOfficePoseValue == OfficePose.Hover, "Head hover stays Hover even past 3s");

        scene.Pointer(new Point(305, 260), false, false, 6.10, new Vector(0, 0), false);
        scene.Advance(9.20);
        Assert(scene.CurrentOfficePoseValue == OfficePose.Sleeping, "Leaving head and resting 3s enters Sleeping");

        scene.ClickOfficeHead(9.30);
        scene.Advance(10.0);
        Assert(scene.CurrentOfficePoseValue == OfficePose.Click, "Head click enters Click for 1.5s");

        KCOfficeArmGeometry arm = KeycatCore.kc_solve_office_arm(
            KeycatCore.kc_pt(188, 171), KeycatCore.kc_pt(214, 250), -1, 0);
        Near(arm.handScaleX, 1.16, 1e-6, "left arm handScaleX");
        Near(arm.handScaleY, 1.16, 1e-6, "left arm handScaleY");
        Assert(arm.handAngleDeg >= -31 && arm.handAngleDeg <= 31, "arm angle within limits");

        // Verify colleague body motion leans toward left/right typed keys and snaps back to 0 when idle.
        scene.Cat = CatStyle.Boss;
        scene.ResetOfficeInteraction();
        scene.Strike(65, 20.00);
        scene.Advance(20.06);
        KCOfficeBodyPose typingPose = scene.OfficeBodyPoseForTest(214, 242, new Point(190, 177), new Point(238, 177));
        Assert(Math.Abs(typingPose.offsetX) > 0.05 || Math.Abs(typingPose.offsetY) > 0.05 || Math.Abs(typingPose.tiltDeg) > 0.05,
            "colleague body moves dynamically while typing");
        scene.Advance(21.10);
        Assert(scene.CurrentOfficePoseValue == OfficePose.Idle, "returns to Idle after typing stops");
        KCOfficeBodyPose idlePose = scene.OfficeBodyPoseForTest(214, 242, new Point(190, 177), new Point(238, 177));
        Near(idlePose.offsetX, 0, 1e-9, "idle office offsetX resets");
        Near(idlePose.tiltDeg, 0, 1e-9, "idle office tiltDeg resets");

        for (int c = 4; c <= 13; c++)
        {
            var person = (CatStyle)c;
            Assert(person.IsPerson(), "style " + person + " is person");
            OfficeColleagueArt art = OfficeColleagueArt.For(person);
            Assert(art.ContainsHead(new Point(214, 105)), person + " head point hits head mask");
            Assert(!art.ContainsHead(new Point(214, 232)), person + " torso point excluded from head mask");
        }
        Assert(!((CatStyle)3).IsPerson() && !((CatStyle)14).IsPerson(), "person bounds are 4..13");
        for (int c = 14; c <= 17; c++)
        {
            var dog = (CatStyle)c;
            Assert(!dog.IsPerson() && DogArt.IsDog(dog), dog + " is animated pet dog");
            Assert(CatArt.ContainsHead(dog, new Point(214, 105), 1.0, 0, 0, new Point(192, 233)), dog + " head point hits head ellipse");
            Assert(CatArt.ContainsHead(dog, new Point(214, 70), 1.0, 0, 0, new Point(192, 233)), dog + " forehead hits head ellipse");
            Assert(CatArt.ContainsHead(dog, new Point(170, 58), 1.0, 0, 0, new Point(192, 233)), dog + " left ear hits head ellipse");
            Assert(!CatArt.ContainsHead(dog, new Point(214, 155), 1.0, 0, 0, new Point(192, 233)), dog + " chest excluded from head ellipse");
            Assert(!CatArt.ContainsHead(dog, new Point(214, 232), 1.0, 0, 0, new Point(192, 233)), dog + " torso excluded from head");
        }
        scene.Cat = CatStyle.OfficeColleague;
        scene.ResetOfficeInteraction();
        scene.Cat = CatStyle.Corgi;
        Near(scene.LeftHandPoint.X, 192, 1e-6, "switching person->dog resets left hand X to rest");
        Near(scene.LeftHandPoint.Y, 233, 1e-6, "switching person->dog resets left hand Y to rest");
        Near(scene.RightHandPoint.X, 229, 1e-6, "switching person->dog resets right hand X to rest");
        Near(scene.RightHandPoint.Y, 233, 1e-6, "switching person->dog resets right hand Y to rest");
        Assert(CatArt.ContainsHead(CatStyle.Black, new Point(125, 86), 1.0, 0, 0, new Point(104, 260)),
            "Black cat head petting tracks left-leaning head when mousing");
        Assert(!CatArt.ContainsHead(CatStyle.Black, new Point(265, 86), 1.0, 0, 0, new Point(104, 260)),
            "Black cat mousing leaves right void excluded from head hit");
        using (var styleMenu = new StyleMenu())
        {
            var charsMenu = styleMenu.Characters(CatStyle.Shiba, delegate { });
            Assert(charsMenu.DropDownItems.Count == 18, "Characters menu lists 4 cats + 10 colleagues + 4 dogs");
            foreach (System.Windows.Forms.ToolStripItem item in charsMenu.DropDownItems)
                Assert(item.Image != null, "Character menu item has portrait image: " + item.Text);
            var propsMenu = styleMenu.DeskProps(KeycatCore.KC_PROP_COFFEE, delegate { });
            Assert(propsMenu.DropDownItems.Count == 5, "DeskProps menu lists 5 items");
            foreach (System.Windows.Forms.ToolStripItem item in propsMenu.DropDownItems)
                Assert(item.Image != null, "DeskProp menu item has vector thumbnail: " + item.Text);
            var fxMenu = styleMenu.FxLevels(KeycatCore.KC_FX_VIBRANT, delegate { });
            Assert(fxMenu.DropDownItems.Count == 2, "FxLevels menu lists 2 items");
        }
        scene.Cat = CatStyle.Black;
        // Slow typing (0.35s/key) must NOT trigger cat motion/VFX.
        scene.Strike(65, 28.00);
        scene.Strike(83, 28.35);
        scene.Strike(68, 28.70);
        scene.Advance(28.74);
        KCCatMotion slowCat = scene.CatMotionForTest;
        Near(slowCat.fury, 0, 1e-9, "slow typing builds no cat fury");
        Near(slowCat.slam, 0, 1e-9, "slow typing builds no cat slam");
        Near(slowCat.leftWhip, 0, 1e-9, "slow typing draws no left paw whip");
        Near(slowCat.rightWhip, 0, 1e-9, "slow typing draws no right paw whip");
        Near(slowCat.deskOffsetY, 0, 1e-9, "slow typing causes no desk recoil");
        Assert(scene.ActiveImpactCountForTest == 0, "slow typing spawns no impact bursts");

        // Rapid burst triggers forceful motion, whips, desk recoil, and impact bursts starting at key 3+.
        scene.Strike(65, 30.00);
        scene.Strike(83, 30.04);
        Assert(scene.ActiveImpactCountForTest == 0, "two isolated keys stay calm without impact");
        scene.Strike(68, 30.08);
        scene.Strike(70, 30.12);
        scene.Strike(71, 30.16);
        scene.Advance(30.18);
        KCCatMotion catMotion = scene.CatMotionForTest;
        Assert(catMotion.fury > 0.5 && catMotion.slam > 0.5, "rapid cat typing builds slam and fury");
        Assert(catMotion.scaleX > 1.0 && catMotion.scaleY < 1.0, "cat body squashes on forceful key impact");
        Assert(catMotion.leftWhip > 0.14 || catMotion.rightWhip > 0.14, "rapid typing draws paw whip trails");
        Assert(catMotion.deskOffsetY > 0.5 && scene.ActiveImpactCountForTest == 3, "desk recoils and fast key impact bursts spawn");
        scene.ClearLabels();
        Assert(scene.ActiveImpactCountForTest == 3, "clearing floating keycaps preserves typing shockwave impacts");
        // Pressing slowly right after a fast burst must NOT add another impact.
        scene.Strike(72, 30.58);
        scene.Advance(30.60);
        Assert(scene.ActiveImpactCountForTest == 0, "expired impacts cleared and slow follow-up adds none");
        for (int s = 1; s <= 40; s++) scene.Advance(30.60 + s * 0.05);
        Near(scene.CatMotionForTest.slam, 0, 1e-6, "cat slam settles back to 0");
        Assert(scene.ActiveImpactCountForTest == 0, "expired impact bursts clear cleanly");
    }

    private static void TestMenuIsolationAndDismissal()
    {
        Environment.SetEnvironmentVariable("KEYCAT_NO_AUTO_UPDATE", "1");
        var window = new PetWindow();
        window.SkipPersistenceForTest = true;
        try
        {
            IntPtr fakeHwnd = new IntPtr(0x123456);
            window.SetHwndForTest(fakeHwnd);
            window.Scene.Cat = CatStyle.OfficeColleague;
            window.Scene.ResetOfficeInteraction();
            window.Scene.Advance(0.5);
            Assert(window.Scene.CurrentOfficePoseValue == OfficePose.Idle, "office starts Idle");

            // While menu is open, Tick must NOT feed mouse pointer activity into CatScene.
            window.SetMenuOpenForTest(true);
            window.Tick(() => new WindowsInput.ScreenPoint { X = 200, Y = 200 });
            window.Tick(() => new WindowsInput.ScreenPoint { X = 420, Y = 310 });
            Assert(window.Scene.CurrentOfficePoseValue == OfficePose.Idle,
                "moving cursor while menu is open does not wake or drive the pet");

            // Clicking outside menu must dismiss menu cleanly without crashing when menu is closed.
            bool outside = window.DismissMenuOutside(new WindowsInput.ScreenPoint { X = 900, Y = 900 },
                pt => IntPtr.Zero);
            Assert(outside && !window.IsMenuOpen, "clicking outside menu dismisses open menu");

            // Clicking the pet HWND itself while menu is open must dismiss the menu and suppress click-through gesture.
            window.SetMenuOpenForTest(true);
            bool dismissed = window.DismissMenuOutside(new WindowsInput.ScreenPoint { X = 10, Y = 10 },
                pt => fakeHwnd);
            Assert(dismissed && !window.IsMenuOpen, "clicking pet window dismisses open menu");
            Assert(window.SuppressClickActive, "click that dismissed menu suppresses immediate drag/head click");
        }
        finally
        {
            window.Close();
        }
    }

    private static void TestAutoStartAndUpdater()
    {
        string testRegKey = @"Software\KeycatDesktopTest\" + Guid.NewGuid().ToString("N");
        string exeA = @"C:\Apps\Keycat\键盘小黑猫.exe";
        string exeB = @"C:\Other\键盘小黑猫.exe";
        try
        {
            Assert(!AutoStart.IsEnabled(testRegKey, AutoStart.ValueName, exeA), "auto-start initially off");
            AutoStart.SetEnabled(testRegKey, AutoStart.ValueName, exeA, true);
            Assert(AutoStart.IsEnabled(testRegKey, AutoStart.ValueName, exeA), "auto-start enabled for matching exe");
            Assert(!AutoStart.IsEnabled(testRegKey, AutoStart.ValueName, exeB), "auto-start false for different exe");
            AutoStart.SetEnabled(testRegKey, AutoStart.ValueName, exeA, false);
            Assert(!AutoStart.IsEnabled(testRegKey, AutoStart.ValueName, exeA), "auto-start disabled after toggle off");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\KeycatDesktopTest", false);
        }

        Assert(Updater.IsNewerVersion("v1.12.0", "1.11.0"), "v1.12.0 > 1.11.0");
        Assert(!Updater.IsNewerVersion("v1.11.0", "1.11.0"), "v1.11.0 == 1.11.0");
        Assert(!Updater.IsNewerVersion("1.10.2", "1.11.0"), "1.10.2 < 1.11.0");
        Assert(DeskArt.AppleKeyLabel(0x2E) == "⌦", "Apple Delete key label matches macOS");
        Assert(DeskArt.AppleKeyLabel(261) == "锁屏", "Apple Lock key label matches macOS");

        string json = "{\"tag_name\":\"v1.12.0\",\"name\":\"1.12.0\",\"body\":\"Fix\",\"html_url\":\"https://example.com\",\"draft\":false,\"prerelease\":false," +
                      "\"assets\":[{\"name\":\"键盘小黑猫-1.12.0-Windows安装包.exe\",\"browser_download_url\":\"https://example.com/setup.exe\",\"size\":123}," +
                      "{\"name\":\"键盘小黑猫.exe\",\"browser_download_url\":\"https://example.com/portable.exe\",\"size\":100}," +
                      "{\"name\":\"checksums.txt\",\"browser_download_url\":\"https://example.com/checksums.txt\",\"size\":64}]}";
        GitHubRelease release = Updater.ParseReleaseJson(json);
        Assert(release != null && release.TagName == "v1.12.0" && release.Assets.Count == 3, "GitHub release JSON parsed");
        Assert(Updater.SelectWindowsAsset(release, true).Name.Contains("Windows安装包"), "installed copy selects installer asset");
        Assert(Updater.SelectWindowsAsset(release, false).Name == "键盘小黑猫.exe", "portable copy selects standalone exe");
        Assert(Updater.SelectChecksumAsset(release).Name == "checksums.txt", "checksum asset selected");

        string tempFile = Path.GetTempFileName();
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("hello-keycat");
            File.WriteAllBytes(tempFile, payload);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(payload);
                var hex = new StringBuilder();
                foreach (byte b in hash) hex.Append(b.ToString("x2"));
                string manifest = hex + "  键盘小黑猫.exe\n";
                Assert(Updater.VerifySha256(tempFile, manifest, "键盘小黑猫.exe"), "SHA-256 verification succeeds");
                Assert(!Updater.VerifySha256(tempFile, new string('0', 64) + "  键盘小黑猫.exe\n", "键盘小黑猫.exe"),
                    "SHA-256 verification rejects mismatch");
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static void TestRawInputPacket()
    {
        var scene = new CatScene();
        scene.Cat = CatStyle.OfficeColleague;
        scene.ResetOfficeInteraction();
        scene.Advance(1.0);
        int header = 8 + 2 * IntPtr.Size;
        IntPtr buf = Marshal.AllocHGlobal(256);
        try
        {
            for (int i = 0; i < 256; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt32(buf, 0, 0); // RIM_TYPEMOUSE
            Marshal.WriteInt16(buf, header + 4, 0x0400); // RI_MOUSE_WHEEL
            WindowsInput.ProcessPacket(buf, (uint)(header + 24), scene, 1.5);
            scene.Advance(1.6);
            Assert(scene.CurrentOfficePoseValue == OfficePose.Working, "mouse wheel wakes office colleague");
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void TestWin7ApiBaselineAndTls()
    {
        var allowedDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user32.dll", "kernel32.dll" };
        var allowedUser32 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "RegisterRawInputDevices", "GetRawInputData", "GetCursorPos", "GetMessagePos",
            "SetForegroundWindow", "WindowFromPoint", "SetCapture", "GetCapture",
            "ReleaseCapture", "GetAsyncKeyState", "GetWindowLong", "SetWindowLong",
            "GetThreadDesktop", "GetUserObjectInformationW",
            "GetForegroundWindow", "GetShellWindow", "IsWindowVisible", "IsIconic",
            "GetWindowRect", "MonitorFromWindow", "GetMonitorInfoW", "GetClassNameW"
        };
        foreach (Type t in typeof(PetWindow).Assembly.GetTypes())
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var attr = m.GetCustomAttribute<DllImportAttribute>();
                if (attr == null) continue;
                Assert(allowedDlls.Contains(attr.Value), "Win7 baseline P/Invoke DLL: " + attr.Value);
                if (attr.Value.Equals("user32.dll", StringComparison.OrdinalIgnoreCase))
                    Assert(allowedUser32.Contains(attr.EntryPoint ?? m.Name), "Win7 baseline user32 API: " + m.Name);
            }
        }
        Updater.EnableModernTls();
        Assert(((int)System.Net.ServicePointManager.SecurityProtocol & 3072) != 0, "TLS 1.2 enabled");
        Version asmVer = typeof(PetWindow).Assembly.GetName().Version;
        Assert(asmVer.Major + "." + asmVer.Minor + "." + asmVer.Build == KeycatCore.KC_APP_VERSION,
            "AssemblyVersion matches KeycatCore.KC_APP_VERSION");
    }

    private static void TestSingleInstanceDesktopIsolation()
    {
        string m, s;
        Program.SingleInstanceNames("Default", out m, out s);
        Assert(m == @"Local\KeycatDesktop.B48D8B", "Default desktop keeps canonical mutex name");
        Assert(s == @"Local\KeycatDesktop.B48D8B.Show", "Default desktop keeps canonical show event");

        Program.SingleInstanceNames("default", out m, out s);
        Assert(m == @"Local\KeycatDesktop.B48D8B", "Case-insensitive Default desktop keeps canonical mutex");

        Program.SingleInstanceNames("", out m, out s);
        Assert(m == @"Local\KeycatDesktop.B48D8B", "Empty fallback keeps canonical mutex");

        Program.SingleInstanceNames(@"exebox-TEST/01", out m, out s);
        Assert(m == @"Local\KeycatDesktop.B48D8B.exebox-TEST_01", "Non-Default desktop scopes mutex cleanly");
        Assert(s == m + ".Show", "Non-Default desktop scopes show event");

        Assert(WindowsInput.CurrentDesktopName() != null, "CurrentDesktopName reads thread desktop without throwing");
    }

    private static void TestV113Features()
    {
        string dir = Path.Combine(Path.GetTempPath(), "keycat-v113-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "preferences.xml");
            var p = new Preferences
            {
                AutoHideFullscreen = false,
                PrivacyMode = true,
                BreakReminder = false,
                HasCompletedGuide = true,
                FxLevel = KeycatCore.KC_FX_QUIET,
                DeskProp = KeycatCore.KC_PROP_ICED,
                TodayKeyCount = 321,
                TodayBurstCount = 7
            };
            p.Save(file);
            Preferences r = Preferences.Load(file);
            Assert(!r.AutoHideFullscreen && r.PrivacyMode && !r.BreakReminder && r.HasCompletedGuide, "v1.13+ booleans and guideDone roundtrip");
            Assert(r.FxLevel == KeycatCore.KC_FX_QUIET && r.DeskProp == KeycatCore.KC_PROP_ICED, "v1.13 fxLevel and deskProp roundtrip");
            Assert(r.TodayKeyCount == 321 && r.TodayBurstCount == 7, "v1.13 today stats roundtrip");

            Assert(r.EnsureToday(DateTime.Now.AddDays(1)), "EnsureToday resets counters on date rollover");
            Assert(r.TodayKeyCount == 0 && r.TodayBurstCount == 0, "Rolled-over day clears key and burst counts");

            File.WriteAllText(file, "<cat fxLevel=\"99\" deskProp=\"99\" todayKeys=\"-5\" todayBursts=\"-2\" guideDone=\"invalid\" />");
            Preferences clamped = Preferences.Load(file);
            Assert(clamped.FxLevel == KeycatCore.KC_FX_VIBRANT && clamped.DeskProp == 4 && !clamped.HasCompletedGuide, "v1.13+ invalid enums and guideDone clamped cleanly");
            Assert(clamped.TodayKeyCount == 0 && clamped.TodayBurstCount == 0, "Negative stats reset cleanly");
        }
        finally
        {
            Directory.Delete(dir, true);
        }

        var scene = new CatScene();
        scene.PrivacyMode = true;
        scene.FxLevel = KeycatCore.KC_FX_QUIET;
        scene.DeskProp = KeycatCore.KC_PROP_COFFEE;
        scene.BreakReminder = true;
        Assert(scene.PrivacyMode && scene.FxLevel == KeycatCore.KC_FX_QUIET &&
               scene.DeskProp == KeycatCore.KC_PROP_COFFEE && scene.BreakReminder,
               "CatScene v1.13 toggles match");

        double t = 10.0;
        for (int i = 0; i < 30; i++)
        {
            t += 0.06;
            scene.Strike(65 + (i % 4), t);
            scene.Advance(t + 0.02);
        }
        Assert(scene.TodayKeyCount == 30, "CatScene counts typed keys");
        Assert(scene.CatMotionForTest.fury <= 0.270001 && scene.ActiveImpactCountForTest == 0,
            "Quiet mode suppresses heavy motion fury and impacts");
        Assert(scene.TodayBurstCount >= 1, "Stats still count fast bursts in quiet mode");

        scene.FxLevel = KeycatCore.KC_FX_VIBRANT;
        for (int i = 0; i < 15; i++)
        {
            t += 0.05;
            scene.Strike(65 + (i % 4), t);
            scene.Advance(t + 0.01);
        }
        Assert(scene.CatMotionForTest.fury > 0.28 && scene.ActiveImpactCountForTest > 0,
            "Vibrant mode produces combo aura fury and impacts");

        double workStart = t + 1.0;
        scene.ActiveWorkSecondsForTest = 2699.85;
        scene.Strike(65, workStart);
        for (int step = 1; step <= 6; step++)
            scene.Advance(workStart + step * 0.08);
        Assert(scene.BreakOpacityForTest > 0.85, "45-minute continuous work triggers gentle rest bubble");
        scene.DismissBreak();
        Near(scene.BreakOpacityForTest, 0.0, 1e-6, "DismissBreak clears active rest bubble");

        for (int prop = 0; prop <= 4; prop++)
        {
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DeskPropArt.DrawProp(dc, prop, 12.0);
                if (prop == KeycatCore.KC_PROP_MOON) DeskPropArt.DrawBreakBubble(dc, 1.0, 12.0);
            }
            if (prop == 0) Assert(dv.ContentBounds.IsEmpty, "Prop 0 draws nothing");
            else
            {
                Rect b = dv.ContentBounds;
                Assert(!b.IsEmpty && b.Left >= 75.0 && b.Right <= 385.0 && b.Top >= 9.0 && b.Bottom <= 307.0,
                    "Prop " + prop + " stays safely inside scene canvas (" + b + ")");
            }
        }

        var full = new WindowsInput.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var partial = new WindowsInput.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 };
        Assert(WindowsInput.RectCoversMonitor(full, full), "Fullscreen rect matches monitor bounds");
        Assert(!WindowsInput.RectCoversMonitor(partial, full), "Partial window rect does not trigger fullscreen hide");

        var guide = new GuideWindow(null);
        Assert(guide.Title.Contains("交互指南") && guide.CurrentStepForTest == 0, "GuideWindow starts on Step 1 playground");
        guide.SetStepForTest(1);
        Assert(guide.CurrentStepForTest == 1, "GuideWindow advances to Step 2 starter picker");
        guide.SetStepForTest(2);
        Assert(guide.CurrentStepForTest == 2, "GuideWindow advances to Step 3 privacy/hotkeys");
        guide.Close();

        var scaleCtrl = new SleekScaleControl(1.25, "小猫大小");
        Assert(scaleCtrl.PercentageText == "125%", "SleekScaleControl displays rounded preset percent");
        scaleCtrl.Value = 750;
        Assert(scaleCtrl.PercentageText == "75%", "SleekScaleControl updates value cleanly");
        scaleCtrl.Dispose();

        // Verify fine 120 Hz timesteps smoothly advance paw and motion state without dead zones.
        var smoothScene = new CatScene();
        smoothScene.Strike(65, 50.0);
        Point prevLeft = smoothScene.LeftHandPoint;
        int movedSteps = 0;
        for (int frame = 1; frame <= 10; frame++)
        {
            smoothScene.Advance(50.0 + frame / 120.0);
            Point nextLeft = smoothScene.LeftHandPoint;
            if ((nextLeft - prevLeft).Length > 0.01) movedSteps++;
            prevLeft = nextLeft;
        }
        Assert(movedSteps >= 8, "120 Hz steps smoothly interpolate paw trajectory (" + movedSteps + "/10)");
    }

    [STAThread]
    public static int Main()
    {
        try
        {
            TestCoreAndUnifiedDefaults();
            TestSceneAndOfficeArmParity();
            TestMenuIsolationAndDismissal();
            TestAutoStartAndUpdater();
            TestRawInputPacket();
            TestWin7ApiBaselineAndTls();
            TestSingleInstanceDesktopIsolation();
            TestV113Features();
            Console.WriteLine("PASS: all Windows unit tests passed (" + (IntPtr.Size * 8) + "-bit).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace ?? "(no stack)");
            return 1;
        }
    }
}
