using System;
using System.Windows;
using System.Windows.Media;

// Crisp vector motion trails, impact bursts, and effort sparks for enthusiastic cat typing.
internal static class CatVfx
{
    private const double Pi = Math.PI;
    private static readonly Pen ShockHalo = MakePen(Color.FromArgb(200, 255, 255, 255), 3.6);
    private static readonly Pen ShockGold = MakePen(Color.FromRgb(255, 193, 38), 2.1);
    private static readonly Pen ShockCoral = MakePen(Color.FromRgb(255, 107, 53), 2.3);
    private static readonly Pen InnerRingPen = MakePen(Color.FromRgb(255, 245, 196), 1.5);
    private static readonly Brush SparkFill = FreezeBrush(Color.FromRgb(255, 224, 112));
    private static readonly Brush HotSparkFill = FreezeBrush(Color.FromRgb(255, 148, 77));
    private static readonly Pen SparkEdge = MakePen(Color.FromRgb(214, 96, 28), 0.75);
    private static readonly Pen WhipOuter = MakePen(Color.FromRgb(255, 212, 92), 3.1);
    private static readonly Pen WhipInner = MakePen(Color.FromRgb(255, 252, 232), 1.45);
    private static readonly Pen AuraHalo = MakePen(Color.FromArgb(220, 255, 255, 255), 3.4);
    private static readonly Pen AuraCoral = MakePen(Color.FromRgb(255, 108, 47), 1.85);
    private static readonly Brush StarFill = FreezeBrush(Color.FromRgb(255, 222, 89));
    private static readonly Pen StarEdge = MakePen(Color.FromRgb(224, 98, 32), 0.8);

    private static Brush FreezeBrush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private static Pen MakePen(Color color, double width)
    {
        var p = new Pen(FreezeBrush(color), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        p.Freeze();
        return p;
    }

    public static void DrawPawWhip(DrawingContext dc, Point paw, int side, double whip)
    {
        if (whip <= 0.14) return;
        double alpha = KeycatCore.kc_clamp(whip, 0.0, 1.0);
        double reach = 28.0 + 22.0 * alpha;
        Point startOuter = new Point(paw.X + side * 10.0, paw.Y - reach);
        Point ctrlOuter = new Point(paw.X + side * (16.0 + 5.0 * alpha), paw.Y - reach * 0.44);
        Point endOuter = new Point(paw.X + side * 5.0, paw.Y - 4.0);

        Point startInner = new Point(paw.X - side * 5.0, paw.Y - reach * 0.85);
        Point ctrlInner = new Point(paw.X - side * 3.0, paw.Y - reach * 0.36);
        Point endInner = new Point(paw.X - side * 4.0, paw.Y - 5.0);

        var outerArc = new StreamGeometry();
        using (StreamGeometryContext g = outerArc.Open())
        {
            g.BeginFigure(startOuter, false, false);
            g.QuadraticBezierTo(ctrlOuter, endOuter, true, false);
        }
        var innerArc = new StreamGeometry();
        using (StreamGeometryContext g = innerArc.Open())
        {
            g.BeginFigure(startInner, false, false);
            g.QuadraticBezierTo(ctrlInner, endInner, true, false);
        }

        dc.PushOpacity(alpha * 0.88);
        dc.DrawGeometry(null, AuraHalo, outerArc);
        dc.DrawGeometry(null, WhipOuter, outerArc);
        dc.DrawGeometry(null, WhipInner, innerArc);
        dc.Pop();
    }

    public static void DrawImpact(DrawingContext dc, Point origin, double age, double intensity, double seedAngle,
        double deskOffsetX, double deskOffsetY)
    {
        KCImpactLayout layout = KeycatCore.kc_impact_layout(age, intensity, seedAngle);
        if (layout.expired != 0 || layout.ringOpacity <= 0.01) return;

        Point center = new Point(origin.X + deskOffsetX, origin.Y + deskOffsetY);
        bool hot = intensity >= 0.58;
        dc.PushOpacity(KeycatCore.kc_clamp(layout.ringOpacity, 0.0, 1.0));
        dc.DrawEllipse(null, ShockHalo, center, layout.ringRadius, layout.ringRadius * 0.74);
        dc.DrawEllipse(null, hot ? ShockCoral : ShockGold, center, layout.ringRadius, layout.ringRadius * 0.74);
        if (hot)
            dc.DrawEllipse(null, InnerRingPen, center, layout.innerRadius, layout.innerRadius * 0.74);

        const int rayCount = 6;
        double baseRad = layout.angleOffsetDeg * Pi / 180.0;
        double innerR = layout.ringRadius * 0.62;
        double tipR = layout.ringRadius + (6.5 + 5.5 * layout.sparkScale);
        double sideR = (innerR + tipR) * 0.5;
        double halfSpread = 0.095 * layout.sparkScale;
        Brush fill = hot ? HotSparkFill : SparkFill;

        var sparks = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext g = sparks.Open())
        {
            for (int i = 0; i < rayCount; i++)
            {
                double a = baseRad + i * (2.0 * Pi / rayCount);
                Point innerPt = PolarEllipse(center, a, innerR, 0.76);
                Point leftPt = PolarEllipse(center, a - halfSpread, sideR, 0.76);
                Point tipPt = PolarEllipse(center, a, tipR, 0.76);
                Point rightPt = PolarEllipse(center, a + halfSpread, sideR, 0.76);
                g.BeginFigure(innerPt, true, true);
                g.LineTo(leftPt, true, false);
                g.LineTo(tipPt, true, false);
                g.LineTo(rightPt, true, false);
            }
        }
        dc.DrawGeometry(fill, SparkEdge, sparks);
        dc.Pop();
    }

    public static void DrawComboAura(DrawingContext dc, KCHeadPose headPose, KCCatMotion motion, double now, bool wideHead = false)
    {
        if (motion.fury < 0.28) return;
        double strength = KeycatCore.kc_clamp((motion.fury - 0.28) / 0.72, 0.0, 1.0);
        double pulse = 0.78 + 0.22 * Math.Sin(now * 26.0);
        double alpha = strength * (0.65 + 0.35 * motion.slam) * pulse;
        double reach = 5.5 + 5.0 * strength + 2.0 * motion.slam;
        double sideOffset = wideHead ? 75.0 : 62.0;
        double sideY = wideHead ? -12.0 : -6.0;
        double edgePad = 3.5 + reach;

        dc.PushOpacity(KeycatCore.kc_clamp(alpha, 0.0, 1.0));
        for (int side = -1; side <= 1; side += 2)
        {
            double anchorX = headPose.position.x + side * (sideOffset + 3.0 * motion.slam);
            if (side < 0) anchorX = Math.Max(anchorX, 88.0 + edgePad);
            else anchorX = Math.Min(anchorX, 368.0 - edgePad);
            double anchorY = headPose.position.y + sideY;
            for (int k = -1; k <= 1; k++)
            {
                double ang = (k * 24.0 - 10.0) * Pi / 180.0;
                double dx = side * Math.Cos(ang);
                double dy = Math.Sin(ang);
                Point p0 = new Point(anchorX + dx * 3.5, anchorY + dy * 3.5);
                Point p1 = new Point(anchorX + dx * (3.5 + reach), anchorY + dy * (3.5 + reach));
                dc.DrawLine(AuraHalo, p0, p1);
                dc.DrawLine(AuraCoral, p0, p1);
            }
        }
        if (motion.fury >= 0.58)
        {
            double starScale = 0.75 + 0.45 * motion.slam;
            double starLx = wideHead ? -70.0 : -52.0;
            double starLy = wideHead ? -62.0 : -46.0;
            double starRx = wideHead ? 76.0 : 54.0;
            double starRy = wideHead ? -65.0 : -42.0;
            DrawComicStar(dc, new Point(Math.Max(headPose.position.x + starLx, 96.0), headPose.position.y + starLy), 7.0 * starScale);
            DrawComicStar(dc, new Point(Math.Min(headPose.position.x + starRx, 362.0), headPose.position.y + starRy), 5.6 * starScale);
        }
        dc.Pop();
    }

    private static void DrawComicStar(DrawingContext dc, Point center, double r)
    {
        double inner = r * 0.28;
        var star = new StreamGeometry();
        using (StreamGeometryContext g = star.Open())
        {
            g.BeginFigure(new Point(center.X, center.Y - r), true, true);
            g.LineTo(new Point(center.X + inner, center.Y - inner), true, false);
            g.LineTo(new Point(center.X + r, center.Y), true, false);
            g.LineTo(new Point(center.X + inner, center.Y + inner), true, false);
            g.LineTo(new Point(center.X, center.Y + r), true, false);
            g.LineTo(new Point(center.X - inner, center.Y + inner), true, false);
            g.LineTo(new Point(center.X - r, center.Y), true, false);
            g.LineTo(new Point(center.X - inner, center.Y - inner), true, false);
        }
        dc.DrawGeometry(StarFill, StarEdge, star);
    }

    private static Point PolarEllipse(Point center, double rad, double radius, double yAspect)
    {
        return new Point(center.X + Math.Cos(rad) * radius, center.Y + Math.Sin(rad) * radius * yAspect);
    }
}
