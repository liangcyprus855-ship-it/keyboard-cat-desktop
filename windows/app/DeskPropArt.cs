using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

internal static class DeskPropArt
{
    private static readonly Brush MugBody = Freeze(0xFFF6EA);
    private static readonly Pen MugEdge = FreezePen(0xC9B5A0, 1.0);
    private static readonly Pen MugHandleOuter = FreezePen(0xC9B5A0, 3.6);
    private static readonly Pen MugHandleInner = FreezePen(0xFFF6EA, 1.8);
    private static readonly Brush Saucer = Freeze(0xE6D8C8);
    private static readonly Brush Espresso = Freeze(0x6E3E22);
    private static readonly Pen SteamPen = FreezePen(0xE8DDD1, 1.3);

    private static readonly Brush IcedCup = Freeze(0xEAF5FA);
    private static readonly Brush IcedCoffee = Freeze(0x9C5D36);
    private static readonly Brush IceCube = Freeze(0xF5FBFF);
    private static readonly Pen IcedEdge = FreezePen(0x93B1BE, 0.9);
    private static readonly Pen StrawPen = FreezePen(0x4AA37A, 2.0);

    private static readonly Brush BrickTop = Freeze(0xD97856);
    private static readonly Brush BrickFace = Freeze(0xB8563A);
    private static readonly Pen BrickEdge = FreezePen(0x7B3322, 0.85);
    private static readonly Brush SignDark = Freeze(0x273142);
    private static readonly Pen SignEdge = FreezePen(0x161D29, 0.9);
    private static readonly Brush GoldMoon = Freeze(0xF7CD5B);
    private static readonly Brush WhiteText = Freeze(0xF6F7FA);
    private static readonly Brush CreamInk = Freeze(0x5B4A3F);

    private static readonly Brush BubbleFill = Freeze(0xFFFDF8);
    private static readonly Pen BubbleEdge = FreezePen(0xD6C3AF, 1.1);
    private static readonly Brush BubbleTextBrush = Freeze(0x4A3A31);

    private static Brush Freeze(uint rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }

    private static Pen FreezePen(uint rgb, double width)
    {
        var p = new Pen(Freeze(rgb), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        p.Freeze();
        return p;
    }

    public static void DrawProp(DrawingContext dc, int prop, double time)
    {
        if (prop <= 0 || prop > 4) return;
        const double cx = 365.0, cy = 272.0;
        if (prop == KeycatCore.KC_PROP_COFFEE)
        {
            dc.DrawEllipse(Saucer, MugEdge, new Point(cx, cy + 8.5), 11.5, 4.0);
            dc.DrawEllipse(null, MugHandleOuter, new Point(cx + 8.0, cy + 1.0), 3.0, 4.0);
            dc.DrawEllipse(null, MugHandleInner, new Point(cx + 8.0, cy + 1.0), 3.0, 4.0);
            dc.DrawRoundedRectangle(MugBody, MugEdge, new Rect(cx - 7.5, cy - 6.0, 15.0, 14.0), 4.0, 4.0);
            dc.DrawEllipse(Espresso, MugEdge, new Point(cx, cy - 5.2), 6.2, 2.3);
            double s0 = Math.Sin(time * 2.3) * 1.4;
            double s1 = Math.Cos(time * 2.1) * 1.4;
            var steam = new StreamGeometry();
            using (var g = steam.Open())
            {
                g.BeginFigure(new Point(cx - 2.2, cy - 9.0), false, false);
                g.QuadraticBezierTo(new Point(cx - 3.8 + s0, cy - 13.0), new Point(cx - 1.5 - s0 * 0.6, cy - 17.5), true, false);
                g.BeginFigure(new Point(cx + 2.2, cy - 9.5), false, false);
                g.QuadraticBezierTo(new Point(cx + 3.8 + s1, cy - 13.5), new Point(cx + 1.5 - s1 * 0.6, cy - 18.0), true, false);
            }
            dc.DrawGeometry(null, SteamPen, steam);
        }
        else if (prop == KeycatCore.KC_PROP_ICED)
        {
            dc.DrawLine(StrawPen, new Point(cx + 1.5, cy - 6.0), new Point(cx + 5.0, cy - 18.0));
            var cup = new StreamGeometry();
            using (var g = cup.Open())
            {
                g.BeginFigure(new Point(cx - 7.0, cy - 7.5), true, true);
                g.LineTo(new Point(cx + 7.0, cy - 7.5), true, false);
                g.LineTo(new Point(cx + 5.0, cy + 11.0), true, false);
                g.LineTo(new Point(cx - 5.0, cy + 11.0), true, false);
            }
            dc.DrawGeometry(IcedCup, IcedEdge, cup);
            var liquid = new StreamGeometry();
            using (var g = liquid.Open())
            {
                g.BeginFigure(new Point(cx - 6.1, cy - 2.0), true, true);
                g.LineTo(new Point(cx + 6.1, cy - 2.0), true, false);
                g.LineTo(new Point(cx + 4.4, cy + 10.0), true, false);
                g.LineTo(new Point(cx - 4.4, cy + 10.0), true, false);
            }
            dc.DrawGeometry(IcedCoffee, null, liquid);
            dc.DrawRoundedRectangle(IceCube, IcedEdge, new Rect(cx - 3.8, cy - 4.5, 4.2, 4.2), 0.8, 0.8);
            dc.DrawRoundedRectangle(IceCube, IcedEdge, new Rect(cx + 0.6, cy - 3.6, 3.8, 3.8), 0.8, 0.8);
        }
        else if (prop == KeycatCore.KC_PROP_BRICK)
        {
            dc.DrawRoundedRectangle(BrickFace, BrickEdge, new Rect(cx - 10.5, cy - 2.0, 21.0, 12.0), 2.0, 2.0);
            dc.DrawRoundedRectangle(BrickTop, BrickEdge, new Rect(cx - 10.5, cy - 6.5, 21.0, 6.0), 2.0, 2.0);
            dc.DrawEllipse(MugBody, BrickEdge, new Point(cx - 5.0, cy - 3.6), 2.5, 1.3);
            dc.DrawEllipse(MugBody, BrickEdge, new Point(cx + 4.5, cy - 3.6), 2.5, 1.3);
            DrawMiniTag(dc, "搬砖", cx, cy + 4.0, WhiteText);
        }
        else if (prop == KeycatCore.KC_PROP_MOON)
        {
            var stand = new StreamGeometry();
            using (var g = stand.Open())
            {
                g.BeginFigure(new Point(cx - 11.0, cy + 10.5), true, true);
                g.LineTo(new Point(cx - 7.5, cy - 10.5), true, false);
                g.LineTo(new Point(cx + 7.5, cy - 10.5), true, false);
                g.LineTo(new Point(cx + 11.0, cy + 10.5), true, false);
            }
            dc.DrawGeometry(SignDark, SignEdge, stand);
            dc.DrawEllipse(GoldMoon, null, new Point(cx, cy - 4.2), 3.6, 3.6);
            dc.DrawEllipse(SignDark, null, new Point(cx + 1.5, cy - 4.8), 2.8, 2.8);
            DrawMiniTag(dc, "勿扰", cx, cy + 4.8, GoldMoon);
        }
    }

    private static void DrawMiniTag(DrawingContext dc, string text, double cx, double cy, Brush ink)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            7.2, ink, 1.0);
        dc.DrawText(ft, new Point(cx - ft.Width / 2.0, cy - ft.Height / 2.0));
    }

    public static void DrawBreakBubble(DrawingContext dc, double opacity, double time)
    {
        if (opacity <= 0.01) return;
        double bob = Math.Sin(time * 2.3) * 1.1;
        var rect = new Rect(280.0, 17.0 + bob, 94.0, 26.0);
        dc.PushOpacity(Math.Max(0.0, Math.Min(1.0, opacity)));
        dc.DrawRoundedRectangle(BubbleFill, BubbleEdge, rect, 11.0, 11.0);
        var ft = new FormattedText("休息喝口水 ☕", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            10.5, BubbleTextBrush, 1.0);
        dc.DrawText(ft, new Point(rect.X + (rect.Width - ft.Width) / 2.0, rect.Y + (rect.Height - ft.Height) / 2.0 - 0.5));
        dc.Pop();
    }
}
