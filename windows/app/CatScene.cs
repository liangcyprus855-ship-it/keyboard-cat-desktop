using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

// All positions are expressed on a 480 x 320 drawing surface.
internal sealed class CatScene : FrameworkElement
{
    private sealed class Cap
    {
        public string Label;
        public Point Start;
        public double Time, Angle;
    }

    private sealed class Impact
    {
        public Point Start;
        public double Time, Intensity, SeedAngle;
    }

    private readonly KCSceneState state = new KCSceneState();
    private readonly List<Cap> caps = new List<Cap>();
    private readonly List<Impact> impacts = new List<Impact>();
    private readonly Dictionary<int, double> strikes = new Dictionary<int, double>();
    private readonly Dictionary<int, double> highlights = new Dictionary<int, double>();
    private readonly List<int> expired = new List<int>();
    private readonly Random random = new Random();
    private bool dirtyRender = true;
    private KCOfficePose lastRenderedPose = KCOfficePose.KC_POSE_IDLE;
    private double lastMouseOffsetX, lastMouseOffsetY;

    public const double CanvasWidth = KeycatCore.KC_SCENE_WIDTH, CanvasHeight = KeycatCore.KC_SCENE_HEIGHT;

    public CatScene()
    {
        KeycatCore.kc_scene_init(state, 0.0);
        SyncDeskTargets();
    }

    public bool LabelsEnabled
    {
        get { return state.labelsEnabled != 0; }
        set { state.labelsEnabled = value ? 1 : 0; dirtyRender = true; }
    }

    public bool PrivacyMode
    {
        get { return state.privacyMode != 0; }
        set { state.privacyMode = value ? 1 : 0; dirtyRender = true; }
    }

    public int FxLevel
    {
        get { return state.fxLevel; }
        set { state.fxLevel = value == KeycatCore.KC_FX_QUIET ? KeycatCore.KC_FX_QUIET : KeycatCore.KC_FX_VIBRANT; dirtyRender = true; InvalidateVisual(); }
    }

    public int DeskProp
    {
        get { return state.deskProp; }
        set { state.deskProp = Math.Max(0, Math.Min(4, value)); dirtyRender = true; InvalidateVisual(); }
    }

    public bool BreakReminder
    {
        get { return state.breakReminderEnabled != 0; }
        set
        {
            state.breakReminderEnabled = value ? 1 : 0;
            if (!value) KeycatCore.kc_scene_dismiss_break(state);
            dirtyRender = true;
            InvalidateVisual();
        }
    }

    public int TodayKeyCount
    {
        get { return state.todayKeyCount; }
        set { state.todayKeyCount = Math.Max(0, value); }
    }

    public int TodayBurstCount
    {
        get { return state.todayBurstCount; }
        set { state.todayBurstCount = Math.Max(0, value); }
    }

    internal double ActiveWorkSecondsForTest
    {
        get { return state.activeWorkSeconds; }
        set { state.activeWorkSeconds = Math.Max(0, value); }
    }

    internal double BreakOpacityForTest
    {
        get { return KeycatCore.kc_scene_break_opacity(state, state.time); }
    }

    public void DismissBreak()
    {
        KeycatCore.kc_scene_dismiss_break(state);
        dirtyRender = true;
        InvalidateVisual();
    }

    public CatStyle Cat
    {
        get { return (CatStyle)state.catStyle; }
        set
        {
            KeycatCore.kc_scene_set_character(state, (int)value);
            if (value.IsPerson()) impacts.Clear();
            dirtyRender = true;
        }
    }

    public KeyboardStyle Keyboard
    {
        get { return (KeyboardStyle)state.keyboardStyle; }
        set
        {
            state.keyboardStyle = (int)value;
            SyncDeskTargets();
            if (Cat.IsPerson() && CurrentOfficePose() == OfficePose.Idle)
                state.right = state.spaceKey;
            dirtyRender = true;
        }
    }

    public MouseStyle Mouse
    {
        get { return (MouseStyle)state.mouseStyle; }
        set
        {
            state.mouseStyle = (int)value;
            state.mousePaw = ToCorePt(MouseArt.PawTarget);
            dirtyRender = true;
        }
    }

    internal OfficePose CurrentOfficePoseValue { get { return CurrentOfficePose(); } }
    internal Point LeftHandPoint { get { return ToWpfPt(state.left); } }
    internal Point RightHandPoint { get { return ToWpfPt(state.right); } }
    internal bool OfficeLeftActive { get { return state.officeLeft != 0; } }
    internal bool OfficeRightActive { get { return state.officeRight != 0; } }
    internal double GripValue { get { return state.grip; } }
    internal double GazeX { get { return state.gazeX; } }
    internal bool HeadHovered { get { return state.headHovered != 0; } }
    internal KCCatMotion CatMotionForTest { get { return KeycatCore.kc_cat_motion(state); } }
    internal int ActiveImpactCountForTest { get { return impacts.Count; } }
    internal KCOfficeBodyPose OfficeBodyPoseForTest(double centerX, double bottomY, Point leftShoulder, Point rightShoulder) {
        return KeycatCore.kc_office_body_pose(state, centerX, bottomY, ToCorePt(leftShoulder), ToCorePt(rightShoulder));
    }

    private void SyncDeskTargets()
    {
        state.mousePaw = ToCorePt(MouseArt.PawTarget);
        state.spaceKey = ToCorePt(DeskArt.KeyPoint(Keyboard, 32));
        state.jKey = ToCorePt(DeskArt.KeyPoint(Keyboard, 74));
        state.fKey = ToCorePt(DeskArt.KeyPoint(Keyboard, 70));
    }

    private static KCPoint ToCorePt(Point p) { return KeycatCore.kc_pt(p.X, p.Y); }
    private static Point ToWpfPt(KCPoint p) { return new Point(p.x, p.y); }

    public void ClearLabels() { caps.Clear(); dirtyRender = true; InvalidateVisual(); }

    public void ClearInput(double now)
    {
        caps.Clear();
        impacts.Clear();
        strikes.Clear();
        highlights.Clear();
        KeycatCore.kc_scene_reset_input(state, now);
        dirtyRender = true;
        InvalidateVisual();
    }

    public void ResetOfficeInteraction()
    {
        KeycatCore.kc_scene_reset_office(state);
        impacts.Clear();
        dirtyRender = true;
    }

    public void ClickOfficeHead(double now)
    {
        KeycatCore.kc_scene_click_office_head(state, now);
        dirtyRender = true;
        InvalidateVisual();
    }

    private OfficePose CurrentOfficePose()
    {
        return (OfficePose)KeycatCore.kc_scene_office_pose(state);
    }

    public void Strike(int vk, double now)
    {
        Point target = DeskArt.KeyPoint(Keyboard, vk);
        strikes[vk] = now;
        KeycatCore.kc_scene_strike(state, vk, ToCorePt(target), now);
        if (!Cat.IsPerson())
        {
            double intensity = KeycatCore.kc_cat_impact_intensity(state);
            if (intensity > 0.0)
            {
                if (impacts.Count >= 7) impacts.RemoveAt(0);
                impacts.Add(new Impact
                {
                    Start = target,
                    Time = now,
                    Intensity = intensity,
                    SeedAngle = random.Next(0, 60)
                });
            }
        }
        dirtyRender = true;
        if (!LabelsEnabled) return;
        string label = Keyboard == KeyboardStyle.Apple ? DeskArt.AppleKeyLabel(vk) ?? KeyLabels.Get(vk) : KeyLabels.Get(vk);
        if (label == null) return;
        if (PrivacyMode) label = "••";
        if (caps.Count >= 9) caps.RemoveAt(0);
        caps.Add(new Cap
        {
            Label = label,
            Time = now,
            Start = new Point(target.X + random.Next(-5, 6), target.Y - 14),
            Angle = random.Next(-7, 8)
        });
    }

    public void Pointer(Point p, bool buttonDown, double now)
    {
        Pointer(p, buttonDown, false, now, new Vector(Math.Tanh((p.X - 214) / 600), Math.Tanh((p.Y - 150) / 400)));
    }

    public void Pointer(Point p, bool buttonDown, double now, Vector positionOnMonitor)
    {
        Pointer(p, buttonDown, false, now, positionOnMonitor);
    }

    public void Pointer(Point p, bool physicalLeft, bool physicalRight, double now, Vector positionOnMonitor)
    {
        Pointer(p, physicalLeft, physicalRight, now, positionOnMonitor, true);
    }

    public void Pointer(Point p, bool physicalLeft, bool physicalRight, double now, Vector positionOnMonitor, bool canHover)
    {
        bool headHit = canHover && CatArt.ContainsHead(Cat, p, state.time, state.gazeX, state.gazeY, ToWpfPt(state.left));
        KeycatCore.kc_scene_pointer(state, ToCorePt(p), physicalLeft ? 1 : 0, physicalRight ? 1 : 0,
            now, KeycatCore.kc_pt(positionOnMonitor.X, positionOnMonitor.Y), headHit ? 1 : 0);
    }

    public void MouseButton(int virtualKey, bool down, double now)
    {
        KeycatCore.kc_scene_mouse_button(state, virtualKey, down ? 1 : 0, now);
        dirtyRender = true;
    }

    public void MouseScroll(double now)
    {
        KeycatCore.kc_scene_mouse_scroll(state, now);
        dirtyRender = true;
    }

    public void Advance(double now)
    {
        KeycatCore.kc_scene_advance(state, now, random.NextDouble());
        for (int i = caps.Count - 1; i >= 0; i--)
        {
            bool newest = i == caps.Count - 1;
            KCCapLayout layout = KeycatCore.kc_cap_layout(now - caps[i].Time, newest ? 1 : 0,
                ToCorePt(caps[i].Start), caps[i].Angle);
            if (layout.expired != 0) caps.RemoveAt(i);
        }
        for (int i = impacts.Count - 1; i >= 0; i--)
        {
            KCImpactLayout layout = KeycatCore.kc_impact_layout(now - impacts[i].Time, impacts[i].Intensity, impacts[i].SeedAngle);
            if (layout.expired != 0) impacts.RemoveAt(i);
        }
        highlights.Clear();
        expired.Clear();
        foreach (var pair in strikes)
        {
            double age = now - pair.Value;
            if (age > .43) expired.Add(pair.Key);
            else highlights[pair.Key] = 1 - KeycatCore.kc_smooth(age / .43);
        }
        foreach (int key in expired) strikes.Remove(key);

        var pose = (KCOfficePose)KeycatCore.kc_scene_office_pose(state);
        bool mouseMoving = Math.Abs(state.mouseOffset.x - lastMouseOffsetX) > 0.001 ||
                           Math.Abs(state.mouseOffset.y - lastMouseOffsetY) > 0.001;
        bool redraw = dirtyRender ||
                      !Cat.IsPerson() ||
                      pose == KCOfficePose.KC_POSE_WORKING ||
                      pose == KCOfficePose.KC_POSE_SLEEPING ||
                      pose != lastRenderedPose ||
                      caps.Count > 0 ||
                      impacts.Count > 0 ||
                      highlights.Count > 0 ||
                      state.leftClickGlow > 0.001 ||
                      state.rightClickGlow > 0.001 ||
                      mouseMoving ||
                      state.deskProp == KeycatCore.KC_PROP_COFFEE ||
                      KeycatCore.kc_scene_break_opacity(state, now) > 0.01;
        if (redraw)
        {
            lastRenderedPose = pose;
            lastMouseOffsetX = state.mouseOffset.x;
            lastMouseOffsetY = state.mouseOffset.y;
            dirtyRender = false;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        bool isPerson = Cat.IsPerson();
        KCCatMotion motion = KeycatCore.kc_cat_motion(state);
        dc.PushTransform(new ScaleTransform(ActualWidth / CanvasWidth, ActualHeight / CanvasHeight));
        bool deskKick = !isPerson && (Math.Abs(motion.deskOffsetX) > 0.001 ||
                                      Math.Abs(motion.deskOffsetY) > 0.001 ||
                                      Math.Abs(motion.deskTiltDeg) > 0.001);
        if (deskKick)
        {
            var deskTx = new TransformGroup();
            deskTx.Children.Add(new RotateTransform(motion.deskTiltDeg, 214, 268));
            deskTx.Children.Add(new TranslateTransform(motion.deskOffsetX, motion.deskOffsetY));
            dc.PushTransform(deskTx);
        }
        DeskArt.DrawDesk(dc, highlights, state.leftClickGlow, state.rightClickGlow,
            Keyboard, Mouse, new Vector(state.mouseOffset.x, state.mouseOffset.y));
        DeskPropArt.DrawProp(dc, state.deskProp, state.time);
        if (deskKick) dc.Pop();

        if (isPerson)
            OfficeColleagueArt.For(Cat).Draw(dc, CurrentOfficePose(), ToWpfPt(state.left), ToWpfPt(state.right),
                PresentationSource.FromVisual(this) != null, state.officeLeft != 0, state.officeRight != 0, state.time, state);
        else
        {
            bool catActive = Math.Abs(motion.offsetX) > 0.001 || Math.Abs(motion.offsetY) > 0.001 ||
                             Math.Abs(motion.tiltDeg) > 0.001 || Math.Abs(motion.scaleX - 1.0) > 0.001 ||
                             Math.Abs(motion.scaleY - 1.0) > 0.001;
            if (catActive)
            {
                var catTx = new TransformGroup();
                catTx.Children.Add(new ScaleTransform(motion.scaleX, motion.scaleY, 214, 236));
                catTx.Children.Add(new RotateTransform(motion.tiltDeg, 214, 236));
                catTx.Children.Add(new TranslateTransform(motion.offsetX, motion.offsetY));
                dc.PushTransform(catTx);
            }
            CatArt.DrawStyled(dc, Cat, state.time, state.gazeX, state.gazeY, state.blink,
                ToWpfPt(state.left), ToWpfPt(state.right), state.leftActivity, state.rightActivity);
            CatVfx.DrawPawWhip(dc, ToWpfPt(state.left), -1, motion.leftWhip);
            CatVfx.DrawPawWhip(dc, ToWpfPt(state.right), 1, motion.rightWhip);
            CatVfx.DrawComboAura(dc, KeycatCore.kc_cat_head_pose(state), motion, state.time, DogArt.IsDog(Cat));
            if (catActive) dc.Pop();

            for (int i = 0; i < impacts.Count; i++)
            {
                Impact imp = impacts[i];
                CatVfx.DrawImpact(dc, imp.Start, state.time - imp.Time, imp.Intensity, imp.SeedAngle,
                    motion.deskOffsetX, motion.deskOffsetY);
            }
        }
        for (int i = 0; i < caps.Count; i++)
        {
            Cap cap = caps[i];
            KCCapLayout layout = KeycatCore.kc_cap_layout(state.time - cap.Time,
                i == caps.Count - 1 ? 1 : 0, ToCorePt(cap.Start), cap.Angle);
            bool fiery = !isPerson && motion.fury >= 0.45 && i == caps.Count - 1;
            double capScale = fiery ? 1.0 + 0.14 * motion.slam : 1.0;
            DeskArt.DrawCap(dc, cap.Label, ToWpfPt(layout.position), layout.opacity, layout.angleDeg, capScale, fiery);
        }
        DeskPropArt.DrawBreakBubble(dc, KeycatCore.kc_scene_break_opacity(state, state.time), state.time);
        dc.Pop();
    }
}

internal static class KeyLabels
{
    public static string Get(int vk)
    {
        if ((vk >= 48 && vk <= 57) || (vk >= 65 && vk <= 90)) return ((char)vk).ToString();
        if (vk >= 96 && vk <= 105) return (vk - 96).ToString();
        if (vk >= 112 && vk <= 135) return "F" + (vk - 111);
        switch (vk)
        {
            case 8: return "⌫"; case 9: return "Tab"; case 13: return "↵";
            case 16: case 160: case 161: return "⇧";
            case 17: case 162: case 163: return "Ctrl";
            case 18: case 164: case 165: return "Alt";
            case 19: return "Pause"; case 20: return "Caps"; case 27: return "Esc";
            case 32: return "␣"; case 33: return "PgUp"; case 34: return "PgDn";
            case 35: return "End"; case 36: return "Home"; case 37: return "←";
            case 38: return "↑"; case 39: return "→"; case 40: return "↓";
            case 44: return "Prt"; case 45: return "Ins"; case 46: return "Del";
            case 91: case 92: return "Win"; case 93: return "Menu";
            case 106: return "×"; case 107: return "+"; case 109: return "−";
            case 110: case 190: return "."; case 111: case 191: return "/";
            case 144: return "Num"; case 145: return "Scr";
            case 186: return ";"; case 187: return "="; case 188: return ",";
            case 189: return "−"; case 192: return "`"; case 219: return "[";
            case 220: case 226: return "\\"; case 221: return "]"; case 222: return "'";
            default: return null;
        }
    }
}
