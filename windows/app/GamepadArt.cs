using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

// Gamepad mode: the cat holds a handheld controller at chest height.
// The controller is authored in plane coordinates (+x is screen-left,
// +y is screen-up, the pad faces the cat) and projected into the
// 480 x 320 scene, the same way DeskArt projects the keyboard.
// All numbers come from docs/GAMEPAD.md and the confirmed reference
// renders in docs/gamepad-ref/.
public enum GamepadButton
{
    A, B, X, Y,
    LB, RB, LT, RT,
    LS, RS,
    Back, Start,
    DUp, DDown, DLeft, DRight
}

public sealed class GamepadState
{
    private readonly Dictionary<GamepadButton, double> press = new Dictionary<GamepadButton, double>();
    public Vector LeftStick, RightStick;
    public double LeftTrigger, RightTrigger;
    public double Get(GamepadButton button)
    {
        double value;
        return press.TryGetValue(button, out value) ? value : 0;
    }
    public void Set(GamepadButton button, double value)
    {
        if (value <= 0) press.Remove(button);
        else press[button] = Math.Max(0, Math.Min(1, value));
    }
    public void Clear()
    {
        press.Clear();
        LeftStick = new Vector();
        RightStick = new Vector();
        LeftTrigger = 0;
        RightTrigger = 0;
    }
    public bool AnyActivity
    {
        get
        {
            return press.Count > 0 || LeftTrigger > 0 || RightTrigger > 0
                || LeftStick.LengthSquared > 0 || RightStick.LengthSquared > 0;
        }
    }
}

public static class GamepadArt
{
    public const double CenterX = 214, CenterY = 192;
    public const double Width = 80;
    private const double S = 80.0 / 94;
    private const double Ky = 0.80;
    private const double Persp = 0.12;
    private static readonly Typeface Face = new Typeface("Segoe UI");

    private static double Sc(double y)
    {
        double t = Math.Max(0, Math.Min(1, (34 - y) / 63));
        return (1 / (1 - Persp * t)) / (1 / (1 - Persp * 0.5));
    }
    private static Point Project(double x, double y)
    {
        double sc = Sc(y);
        return new Point(CenterX - x * S * sc, CenterY - y * S * Ky * sc);
    }
    private static double Rx(double r, double y) { return r * S * Sc(y); }
    private static double Ry(double r, double y) { return r * S * Ky * Sc(y); }

    private static Color ColorOf(uint rgb)
    {
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    private static Color MixColor(uint a, uint b, double k)
    {
        Color ca = ColorOf(a), cb = ColorOf(b);
        return Color.FromRgb((byte)(ca.R + (cb.R - ca.R) * k),
            (byte)(ca.G + (cb.G - ca.G) * k), (byte)(ca.B + (cb.B - ca.B) * k));
    }
    private static SolidColorBrush Paint(uint rgb)
    {
        SolidColorBrush brush = new SolidColorBrush(ColorOf(rgb));
        brush.Freeze();
        return brush;
    }
    private static Brush Ramp(uint light, uint dark)
    {
        LinearGradientBrush brush = new LinearGradientBrush(ColorOf(light), ColorOf(dark),
            new Point(.3, 0), new Point(.7, 1));
        brush.Freeze();
        return brush;
    }
    private static Brush Crown(uint light, uint mid, uint dark)
    {
        RadialGradientBrush surface = new RadialGradientBrush();
        surface.Center = new Point(.37, .27);
        surface.GradientOrigin = new Point(.36, .24);
        surface.RadiusX = .74;
        surface.RadiusY = .91;
        surface.GradientStops.Add(new GradientStop(ColorOf(light), 0));
        surface.GradientStops.Add(new GradientStop(ColorOf(mid), .64));
        surface.GradientStops.Add(new GradientStop(ColorOf(dark), 1));
        surface.Freeze();
        return surface;
    }
    private static Pen Line(uint rgb, double width)
    {
        Pen pen = new Pen(Paint(rgb), width);
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.LineJoin = PenLineJoin.Round;
        pen.Freeze();
        return pen;
    }
    private static Pen Line(Color color, double width)
    {
        Pen pen = new Pen(new SolidColorBrush(color), width);
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.LineJoin = PenLineJoin.Round;
        pen.Freeze();
        return pen;
    }

    private static Geometry BodyPath()
    {
        StreamGeometry path = new StreamGeometry();
        using (StreamGeometryContext g = path.Open())
        {
            g.BeginFigure(Project(-30, -22), true, true);
            g.BezierTo(Project(-22, -26), Project(22, -26), Project(30, -22), true, false);
            g.BezierTo(Project(40, -20), Project(46, -6), Project(46, 8), true, false);
            g.BezierTo(Project(47, 22), Project(44, 34), Project(36, 34), true, false);
            g.BezierTo(Project(28, 34), Project(24, 26), Project(18, 19), true, false);
            g.LineTo(Project(-18, 19), true, false);
            g.BezierTo(Project(-24, 26), Project(-28, 34), Project(-36, 34), true, false);
            g.BezierTo(Project(-44, 34), Project(-47, 22), Project(-46, 8), true, false);
            g.BezierTo(Project(-46, -6), Project(-40, -20), Project(-30, -22), true, false);
        }
        path.Freeze();
        return path;
    }

    private static readonly DrawingGroup BodyBase = BuildBody();

    private static DrawingGroup BuildBody()
    {
        DrawingGroup group = new DrawingGroup();
        using (DrawingContext dc = group.Open())
        {
            Geometry body = BodyPath();
            double[] offsets = { 3.6, 2.6, 1.6, 0.6 };
            foreach (double off in offsets)
            {
                double k = 1 - off / 3.6;
                dc.PushTransform(new TranslateTransform(0, off));
                dc.DrawGeometry(new SolidColorBrush(MixColor(0x625E55, 0xA8A393, k)), null, body);
                dc.Pop();
            }
            dc.PushTransform(new TranslateTransform(0, 3.6));
            dc.DrawGeometry(null, Line(0x504C45, .85), body);
            dc.Pop();
            dc.DrawGeometry(Crown(0xFFFEF5, 0xF3F1E4, 0xC4C1B3), Line(0x504C45, .55), body);
            StreamGeometry shine = new StreamGeometry();
            using (StreamGeometryContext g = shine.Open())
            {
                g.BeginFigure(Project(41, 12), false, false);
                g.QuadraticBezierTo(Project(43, 23), Project(37, 31), true, false);
            }
            dc.DrawGeometry(null, Line(0xFFFFFF, .95), shine);
        }
        group.Freeze();
        return group;
    }

    private static Geometry TriggerPath(GamepadStyle style, bool left)
    {
        double tx0, tx1, bulge;
        if (style == GamepadStyle.Xbox) { tx0 = 19; tx1 = 34; bulge = -37; }
        else { tx0 = 17; tx1 = 35; bulge = -39; }
        double s = left ? -1 : 1;
        StreamGeometry path = new StreamGeometry();
        using (StreamGeometryContext g = path.Open())
        {
            g.BeginFigure(Project(s * tx0, -23), true, true);
            g.BezierTo(Project(s * (tx0 + 2), bulge), Project(s * (tx1 - 2), bulge),
                Project(s * tx1, -21.5), true, false);
            g.LineTo(Project(s * tx1, -19), true, false);
            g.LineTo(Project(s * tx0, -20), true, false);
        }
        path.Freeze();
        return path;
    }

    private static void DrawTrigger(DrawingContext dc, GamepadStyle style, bool left, GamepadState state)
    {
        double intensity = left ? state.LeftTrigger : state.RightTrigger;
        dc.PushTransform(new TranslateTransform(0, 2 + 1.6 * intensity));
        dc.DrawGeometry(Ramp(0x68727C, 0x414A52),
            Line(MixColor(0x8B95A0, 0xFFD25A, intensity), 0.55 + 0.35 * intensity),
            TriggerPath(style, left));
        dc.Pop();
    }

    private static Geometry BumperPath(GamepadStyle style, bool left)
    {
        double bx0, bx1;
        if (style == GamepadStyle.Xbox) { bx0 = 16; bx1 = 33; }
        else { bx0 = 18; bx1 = 31; }
        double s = left ? -1 : 1;
        StreamGeometry path = new StreamGeometry();
        using (StreamGeometryContext g = path.Open())
        {
            g.BeginFigure(Project(s * bx0, -23.6), true, true);
            g.BezierTo(Project(s * (bx0 + 6), -24.2), Project(s * (bx1 - 5), -23.4),
                Project(s * bx1, -19.3), true, false);
            g.LineTo(Project(s * (bx1 - 1.6), -15.8), true, false);
            g.BezierTo(Project(s * (bx1 - 6), -19.4), Project(s * (bx0 + 6), -20.0),
                Project(s * bx0, -19.6), true, false);
        }
        path.Freeze();
        return path;
    }

    private static void DrawBumper(DrawingContext dc, GamepadStyle style, bool left, GamepadState state)
    {
        double intensity = state.Get(left ? GamepadButton.LB : GamepadButton.RB);
        Geometry path = BumperPath(style, left);
        dc.PushOpacity(1 - 0.6 * intensity);
        dc.PushTransform(new TranslateTransform(0, 1.3));
        dc.DrawGeometry(Paint(0xA8A393), Line(0x504C45, .5), path);
        dc.Pop();
        dc.Pop();
        dc.PushTransform(new TranslateTransform(0, 0.2 + 1.1 * intensity));
        dc.DrawGeometry(Ramp(0xFBF9EF, 0xE2DECC), null, path);
        if (intensity > 0)
        {
            dc.PushOpacity(intensity);
            dc.DrawGeometry(Ramp(0xFFED8F, 0xFFAE37), null, path);
            dc.Pop();
        }
        dc.Pop();
    }

    private static void DrawTouchpad(DrawingContext dc)
    {
        StreamGeometry path = new StreamGeometry();
        const double r = 3;
        using (StreamGeometryContext g = path.Open())
        {
            g.BeginFigure(Project(-14 + r, -19), true, true);
            g.LineTo(Project(14 - r, -19), true, false);
            g.BezierTo(Project(14 - r * 0.45, -19), Project(14, -19 + r * 0.45),
                Project(14, -19 + r), true, false);
            g.LineTo(Project(14, -8 - r), true, false);
            g.BezierTo(Project(14, -8 - r * 0.45), Project(14 - r * 0.45, -8),
                Project(14 - r, -8), true, false);
            g.LineTo(Project(-14 + r, -8), true, false);
            g.BezierTo(Project(-14 + r * 0.45, -8), Project(-14, -8 - r * 0.45),
                Project(-14, -8 - r), true, false);
            g.LineTo(Project(-14, -19 + r), true, false);
            g.BezierTo(Project(-14, -19 + r * 0.45), Project(-14 + r * 0.45, -19),
                Project(-14 + r, -19), true, false);
        }
        dc.DrawGeometry(Ramp(0x414950, 0x1B2228), Line(0x191D23, .4), path);
    }

    private static void DrawStick(DrawingContext dc, GamepadStyle style, bool left, GamepadState state)
    {
        double x, y;
        if (style == GamepadStyle.Xbox) { x = left ? -27 : 15; y = left ? -4 : 9; }
        else { x = left ? -14 : 14; y = 9; }
        Vector tilt = left ? state.LeftStick : state.RightStick;
        double maxTilt = left ? 1.9 : 1.6;
        Point well = Project(x, y);
        double wrx = Rx(7.2, y), wry = Ry(7.2, y);
        dc.DrawEllipse(Paint(0x776440), Line(0x504C45, .45), well, wrx, wry);
        Point deflected = Project(x + tilt.X * maxTilt, y + tilt.Y * maxTilt);
        Vector delta = (deflected - well) * 1.6;
        Point cap = new Point(well.X + delta.X, well.Y - 0.9 + delta.Y);
        double click = state.Get(left ? GamepadButton.LS : GamepadButton.RS);
        cap.Y += 0.9 * click;
        double crx = wrx * 0.84, cry = wry * 0.84;
        dc.DrawEllipse(Ramp(0xFFDE77, 0xCB8C28), null, cap, crx, cry);
        double[] fracs = { -0.28, 0.05, 0.38 };
        foreach (double f in fracs)
        {
            double ly = cap.Y + f * wry;
            dc.DrawLine(Line(0xCB8C28, .48),
                new Point(cap.X - 0.42 * wrx, ly), new Point(cap.X + 0.42 * wrx, ly + 0.12));
        }
    }

    private static Geometry Polygon(Point[] points)
    {
        StreamGeometry path = new StreamGeometry();
        using (StreamGeometryContext g = path.Open())
        {
            g.BeginFigure(points[0], true, true);
            for (int i = 1; i < points.Length; i++) g.LineTo(points[i], true, false);
        }
        path.Freeze();
        return path;
    }

    private static void DrawDpad(DrawingContext dc, GamepadStyle style, GamepadState state)
    {
        double cx = style == GamepadStyle.Xbox ? -15 : -27;
        double cy = style == GamepadStyle.Xbox ? 9 : -4;
        double up = state.Get(GamepadButton.DUp), down = state.Get(GamepadButton.DDown);
        double left = state.Get(GamepadButton.DLeft), right = state.Get(GamepadButton.DRight);
        double intensity = Math.Max(Math.Max(up, down), Math.Max(left, right));
        double sink = 0.8 * intensity;
        double[,] rel = { {-2.3,-5.8},{2.3,-5.8},{2.3,-2.3},{5.8,-2.3},{5.8,2.3},{2.3,2.3},
                          {2.3,5.8},{-2.3,5.8},{-2.3,2.3},{-5.8,2.3},{-5.8,-2.3},{-2.3,-2.3} };
        Point[] verts = new Point[12];
        for (int i = 0; i < 12; i++) verts[i] = Project(cx + rel[i, 0], cy + rel[i, 1]);
        Geometry poly = Polygon(verts);
        dc.PushTransform(new TranslateTransform(0, sink));
        dc.DrawGeometry(Paint(0x989383), Line(0x989383, .65), poly);
        dc.PushTransform(new TranslateTransform(0, -0.6));
        dc.DrawGeometry(Ramp(0xFBF9EF, 0xE2DECC), Line(0x989383, .3), poly);
        if (intensity > 0)
        {
            double[,] arm;
            if (up >= down && up >= left && up >= right)
                arm = new double[,] { {-2.3,-5.8},{2.3,-5.8},{2.3,-2.3},{-2.3,-2.3} };
            else if (down >= left && down >= right)
                arm = new double[,] { {-2.3,2.3},{2.3,2.3},{2.3,5.8},{-2.3,5.8} };
            else if (left >= right)
                arm = new double[,] { {-5.8,-2.3},{-2.3,-2.3},{-2.3,2.3},{-5.8,2.3} };
            else
                arm = new double[,] { {2.3,-2.3},{5.8,-2.3},{5.8,2.3},{2.3,2.3} };
            Point[] corners = new Point[4];
            for (int i = 0; i < 4; i++) corners[i] = Project(cx + arm[i, 0], cy + arm[i, 1]);
            dc.PushOpacity(intensity);
            dc.DrawGeometry(Ramp(0xFFED8F, 0xFFAE37), null, Polygon(corners));
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }

    private static void DrawSmallButtons(DrawingContext dc, GamepadStyle style, GamepadState state)
    {
        if (style != GamepadStyle.Xbox) return;
        // View is the cat's left (plane -6), Menu the cat's right (plane +6).
        double[] xs = { -6, 6 };
        GamepadButton[] buttons = { GamepadButton.Back, GamepadButton.Start };
        for (int i = 0; i < 2; i++)
        {
            Point c = Project(xs[i], -1);
            double intensity = state.Get(buttons[i]);
            dc.DrawEllipse(Paint(intensity > 0 ? 0xFFAE37u : 0x989383u), null,
                c, Rx(1.6, -1), Ry(1.6, -1));
        }
    }

    private static void DrawFaceButton(DrawingContext dc, GamepadStyle style, GamepadButton button,
        double px, double py, GamepadState state)
    {
        Point center = Project(px, py);
        double rx = Rx(3.4, py), ry = Ry(3.4, py);
        double intensity = state.Get(button);
        dc.DrawEllipse(Paint(0x989383), null, center, rx + 0.35, ry + 0.30);
        Point cap = new Point(center.X, center.Y - 0.4 + 1.5 * intensity);
        dc.DrawEllipse(Ramp(0xFBF9EF, 0xE2DECC), null, cap, rx, ry);
        if (intensity > 0)
        {
            dc.PushOpacity(intensity);
            dc.DrawEllipse(Ramp(0xFFED8F, 0xFFAE37), null, cap, rx, ry);
            dc.Pop();
            StreamGeometry shoulder = new StreamGeometry();
            using (StreamGeometryContext g = shoulder.Open())
            {
                g.BeginFigure(new Point(cap.X - 0.70 * rx, cap.Y - 0.28 * ry), false, false);
                g.QuadraticBezierTo(new Point(cap.X, cap.Y - 1.18 * ry),
                    new Point(cap.X + 0.70 * rx, cap.Y - 0.28 * ry), true, false);
            }
            dc.DrawGeometry(null, Line(0x989383, .64), shoulder);
            StreamGeometry edge = new StreamGeometry();
            using (StreamGeometryContext g = edge.Open())
            {
                g.BeginFigure(new Point(cap.X - 0.75 * rx, cap.Y + 0.35 * ry), false, false);
                g.QuadraticBezierTo(new Point(cap.X, cap.Y + 1.05 * ry),
                    new Point(cap.X + 0.75 * rx, cap.Y + 0.35 * ry), true, false);
            }
            dc.DrawGeometry(null, Line(0xFFF4AE, 1.06), edge);
        }
        DrawFaceIcon(dc, style, button, cap, rx, ry, intensity);
    }

    private static void DrawFaceIcon(DrawingContext dc, GamepadStyle style, GamepadButton button,
        Point cap, double rx, double ry, double intensity)
    {
        uint color;
        string letter = null;
        if (style == GamepadStyle.Xbox)
        {
            switch (button)
            {
                case GamepadButton.A: color = 0x2F9E4B; letter = "A"; break;
                case GamepadButton.B: color = 0xD6453D; letter = "B"; break;
                case GamepadButton.X: color = 0x3B7FD4; letter = "X"; break;
                default: color = 0xD99A0B; letter = "Y"; break;
            }
        }
        else
        {
            switch (button)
            {
                case GamepadButton.A: color = 0x4F86E0; break;
                case GamepadButton.B: color = 0xE05A63; break;
                case GamepadButton.X: color = 0xD98BB9; break;
                default: color = 0x33B38E; break;
            }
        }
        if (intensity > 0) color = MixColorRgb(color, 0x4A2A00, 0.5);
        dc.PushTransform(new RotateTransform(180, cap.X, cap.Y));
        if (style == GamepadStyle.Xbox)
        {
            FormattedText glyph = new FormattedText(letter, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Face, rx * 1.34, new SolidColorBrush(ColorOf(color)), 1.0);
            dc.DrawText(glyph, new Point(cap.X - glyph.Width / 2, cap.Y + ry * 0.52 - glyph.Height / 2));
        }
        else
        {
            double sx = rx / 2.93, sy = ry / 2.35;
            Pen pen = Line(color, .78);
            if (button == GamepadButton.A)
            {
                dc.DrawLine(pen, new Point(cap.X - 1.33 * sx, cap.Y - 1.16 * sy),
                    new Point(cap.X + 1.33 * sx, cap.Y + 1.16 * sy));
                dc.DrawLine(pen, new Point(cap.X + 1.33 * sx, cap.Y - 1.16 * sy),
                    new Point(cap.X - 1.33 * sx, cap.Y + 1.16 * sy));
            }
            else if (button == GamepadButton.B)
            {
                dc.DrawEllipse(null, pen, cap, 1.47 * sx, 1.29 * sy);
            }
            else if (button == GamepadButton.X)
            {
                dc.DrawRectangle(null, pen, new Rect(cap.X - 1.35 * sx, cap.Y - 1.17 * sy,
                    2.70 * sx, 2.35 * sy));
            }
            else
            {
                StreamGeometry tri = new StreamGeometry();
                using (StreamGeometryContext g = tri.Open())
                {
                    g.BeginFigure(new Point(cap.X, cap.Y - 1.38 * sy), true, true);
                    g.LineTo(new Point(cap.X + 1.63 * sx, cap.Y + 1.00 * sy), true, false);
                    g.LineTo(new Point(cap.X - 1.64 * sx, cap.Y + 1.00 * sy), true, false);
                }
                dc.DrawGeometry(null, pen, tri);
            }
        }
        dc.Pop();
    }

    private static uint MixColorRgb(uint a, uint b, double k)
    {
        Color c = MixColor(a, b, k);
        return (uint)((c.R << 16) | (c.G << 8) | c.B);
    }

    // Screen displacement of a stick cap from its rest position, for the
    // paw that follows the stick (CatScene). The cap moves with the
    // projected deflection, scaled by 1.6.
    public static Vector StickCapOffset(GamepadStyle style, bool left, Vector tilt)
    {
        double x, y;
        if (style == GamepadStyle.Xbox) { x = left ? -27 : 15; y = left ? -4 : 9; }
        else { x = left ? -14 : 14; y = 9; }
        double maxTilt = left ? 1.9 : 1.6;
        Point well = Project(x, y);
        Point deflected = Project(x + tilt.X * maxTilt, y + tilt.Y * maxTilt);
        return (deflected - well) * 1.6;
    }

    public static void Draw(DrawingContext dc, GamepadStyle style, GamepadState state)
    {
        if (state == null) state = new GamepadState();
        DrawTrigger(dc, style, true, state);
        DrawTrigger(dc, style, false, state);
        dc.DrawDrawing(BodyBase);
        DrawBumper(dc, style, true, state);
        DrawBumper(dc, style, false, state);
        if (style == GamepadStyle.PS5) DrawTouchpad(dc);
        DrawStick(dc, style, true, state);
        DrawStick(dc, style, false, state);
        DrawDpad(dc, style, state);
        DrawSmallButtons(dc, style, state);
        // Face buttons, drawn down, left, right, up (the last drawn sits on top).
        DrawFaceButton(dc, style, GamepadButton.A, 27, 3.4, state);
        DrawFaceButton(dc, style, GamepadButton.X, 19.6, -4, state);
        DrawFaceButton(dc, style, GamepadButton.B, 34.4, -4, state);
        DrawFaceButton(dc, style, GamepadButton.Y, 27, -11.4, state);
    }

    // Screen point of a control, for the gaze target and floating-label start.
    // Left/right are the cat's (the player's) left/right: the cat's left is
    // the viewer's right, i.e. plane -x. The D-pad and stick positions
    // depend on the style.
    public static Point ButtonPoint(GamepadStyle style, GamepadButton button)
    {
        double dx = style == GamepadStyle.Xbox ? -15 : -27;
        double dy = style == GamepadStyle.Xbox ? 9 : -4;
        double sx = style == GamepadStyle.Xbox ? -27 : -14;
        double sy = style == GamepadStyle.Xbox ? -4 : 9;
        double tx = style == GamepadStyle.Xbox ? 15 : 14;
        switch (button)
        {
            case GamepadButton.A: return Project(27, 3.4);
            case GamepadButton.B: return Project(34.4, -4);
            case GamepadButton.X: return Project(19.6, -4);
            case GamepadButton.Y: return Project(27, -11.4);
            case GamepadButton.LB: return Project(-24.5, -19.7);
            case GamepadButton.RB: return Project(24.5, -19.7);
            case GamepadButton.LT: return Project(-26.5, -24) + new Vector(0, 2);
            case GamepadButton.RT: return Project(26.5, -24) + new Vector(0, 2);
            case GamepadButton.LS: return Project(sx, sy);
            case GamepadButton.RS: return Project(tx, 9);
            case GamepadButton.Back: return Project(-6, -1);
            case GamepadButton.Start: return Project(6, -1);
            case GamepadButton.DUp: return Project(dx, dy - 4.05);
            case GamepadButton.DDown: return Project(dx, dy + 4.05);
            case GamepadButton.DLeft: return Project(dx - 4.05, dy);
            default: return Project(dx + 4.05, dy);
        }
    }

    public static string Label(GamepadStyle style, GamepadButton button)
    {
        if (style == GamepadStyle.Xbox)
        {
            switch (button)
            {
                case GamepadButton.A: return "A";
                case GamepadButton.B: return "B";
                case GamepadButton.X: return "X";
                case GamepadButton.Y: return "Y";
                case GamepadButton.LB: return "LB";
                case GamepadButton.RB: return "RB";
                case GamepadButton.LT: return "LT";
                case GamepadButton.RT: return "RT";
                case GamepadButton.LS: return "LS";
                case GamepadButton.RS: return "RS";
                case GamepadButton.Back: return "View";
                case GamepadButton.Start: return "Menu";
                case GamepadButton.DUp: return "↑";
                case GamepadButton.DDown: return "↓";
                case GamepadButton.DLeft: return "←";
                default: return "→";
            }
        }
        switch (button)
        {
            case GamepadButton.A: return "✕";
            case GamepadButton.B: return "○";
            case GamepadButton.X: return "□";
            case GamepadButton.Y: return "△";
            case GamepadButton.LB: return "L1";
            case GamepadButton.RB: return "R1";
            case GamepadButton.LT: return "L2";
            case GamepadButton.RT: return "R2";
            case GamepadButton.LS: return "L3";
            case GamepadButton.RS: return "R3";
            case GamepadButton.Back: return "Create";
            case GamepadButton.Start: return "Options";
            case GamepadButton.DUp: return "↑";
            case GamepadButton.DDown: return "↓";
            case GamepadButton.DLeft: return "←";
            default: return "→";
        }
    }
}
