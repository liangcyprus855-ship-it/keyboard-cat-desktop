using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Pixel-authentic Live2D cutout rig for the four uploaded puppy illustrations.
internal sealed class DogArt
{
    public const int ArmSegments = 18;

    private struct EyeSpec
    {
        public double X, Y, Rx, Ry, Deg;
        public Rect PupilRect;
        public BitmapSource PupilBmp;
    }

    public struct ArmSegment
    {
        public double T0, T1, TMid, Span;
        public Point RestAnchor;
        public Rect Rect;
        public BitmapSource Bmp;
    }

    private struct ArmRig
    {
        public Point Shoulder, RestPaw;
        public Rect SleeveRect, PawRect;
        public BitmapSource SleeveBmp, PawBmp;
        public ArmSegment[] Segments;
    }

    private static readonly Dictionary<CatStyle, DogArt> Breeds = ReadBreeds();
    private static readonly Pen LidLine = MakePen("#3E2D24", 1.85);

    private readonly double HeadPivotY;
    private readonly Rect HeadRect, BodyRect, RuffRect, TailRect;
    private readonly Point TailPivot;
    private readonly double WagSpeed, WagDeg;
    private readonly BitmapSource HeadBmp, BodyBmp, RuffBmp, TailBmp;
    private readonly Brush LidBrush;
    private readonly EyeSpec EyeL, EyeR;
    private readonly ArmRig ArmL, ArmR;

    public static bool IsDog(CatStyle style)
    {
        return style >= CatStyle.Dachshund && style <= CatStyle.Bichon;
    }

    public static DogArt For(CatStyle style)
    {
        return Breeds[style];
    }

    public double HeadY { get { return HeadPivotY; } }
    public Point ShoulderLeft { get { return ArmL.Shoulder; } }
    public Point RestPawLeft { get { return ArmL.RestPaw; } }
    public Point ShoulderRight { get { return ArmR.Shoulder; } }
    public Point RestPawRight { get { return ArmR.RestPaw; } }
    public double TailSpeed { get { return WagSpeed; } }

    public ArmSegment[] GetSegments(bool left)
    {
        return (left ? ArmL : ArmR).Segments;
    }

    public Point[] SegmentAnchors(bool left)
    {
        ArmRig arm = left ? ArmL : ArmR;
        var pts = new Point[arm.Segments.Length];
        for (int i = 0; i < arm.Segments.Length; i++) pts[i] = arm.Segments[i].RestAnchor;
        return pts;
    }

    private static Dictionary<CatStyle, DogArt> ReadBreeds()
    {
        var map = new Dictionary<CatStyle, DogArt>();
        using (var stream = typeof(DogArt).Assembly.GetManifestResourceStream("KeycatDesktop.Pets.tsv"))
        using (var reader = new StreamReader(stream))
        {
            reader.ReadLine();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                string[] f = line.Split('\t');
                var style = (CatStyle)Enum.Parse(typeof(CatStyle), f[1]);
                map.Add(style, new DogArt(style, f));
            }
        }
        return map;
    }

    private DogArt(CatStyle style, string[] f)
    {
        Func<int, double> num = i => double.Parse(f[i], CultureInfo.InvariantCulture);
        HeadPivotY = num(3);
        HeadRect = new Rect(num(4), num(5), num(6), num(7));
        BodyRect = new Rect(num(8), num(9), num(10), num(11));
        TailPivot = new Point(num(12), num(13));
        TailRect = new Rect(num(14), num(15), num(16), num(17));
        WagSpeed = num(18);
        WagDeg = num(19);

        ArmL = BuildArm(
            new Point(num(20), num(21)), new Point(num(22), num(23)),
            new Rect(num(24), num(25), num(26), num(27)),
            new Rect(num(28), num(29), num(30), num(31)),
            LoadPart(style + ".sleeveL"), LoadPart(style + ".pawL"));
        ArmR = BuildArm(
            new Point(num(32), num(33)), new Point(num(34), num(35)),
            new Rect(num(36), num(37), num(38), num(39)),
            new Rect(num(40), num(41), num(42), num(43)),
            LoadPart(style + ".sleeveR"), LoadPart(style + ".pawR"));

        LidBrush = FreezeBrush("#" + f[44]);
        EyeL = new EyeSpec
        {
            X = num(45), Y = num(46), Rx = num(47), Ry = num(48), Deg = num(49),
            PupilRect = new Rect(num(50), num(51), num(52), num(53)),
            PupilBmp = LoadPart(style + ".pupilL")
        };
        EyeR = new EyeSpec
        {
            X = num(54), Y = num(55), Rx = num(56), Ry = num(57), Deg = num(58),
            PupilRect = new Rect(num(59), num(60), num(61), num(62)),
            PupilBmp = LoadPart(style + ".pupilR")
        };
        RuffRect = new Rect(num(63), num(64), num(65), num(66));

        HeadBmp = LoadPart(style + ".head");
        BodyBmp = LoadPart(style + ".body");
        RuffBmp = LoadPart(style + ".ruff");
        TailBmp = LoadPart(style + ".tail");
    }

    private static ArmRig BuildArm(Point shoulder, Point restPaw, Rect sleeveRect, Rect pawRect,
                                   BitmapSource sleeveBmp, BitmapSource pawBmp)
    {
        Vector restVec = restPaw - shoulder;
        double restY = Math.Abs(restVec.Y) < 1.0 ? 1.0 : restVec.Y;
        int pw = sleeveBmp.PixelWidth, ph = sleeveBmp.PixelHeight;
        var segs = new ArmSegment[ArmSegments];
        for (int i = 0; i < ArmSegments; i++)
        {
            int py0 = (int)Math.Floor(i * ph / (double)ArmSegments);
            int py1 = (int)Math.Ceiling((i + 1) * ph / (double)ArmSegments);
            if (i > 0) py0 = Math.Max(0, py0 - 1);
            if (i < ArmSegments - 1) py1 = Math.Min(ph, py1 + 1);
            int slicePh = Math.Max(1, py1 - py0);

            double sy0 = sleeveRect.Y + py0 * (sleeveRect.Height / ph);
            double sy1 = sleeveRect.Y + py1 * (sleeveRect.Height / ph);
            double yMid = 0.5 * (sy0 + sy1);
            double t0 = sy0 / restY, t1 = sy1 / restY, tMid = yMid / restY;
            Point anchor = new Point(tMid * restVec.X, tMid * restVec.Y);

            var crop = new CroppedBitmap(sleeveBmp, new Int32Rect(0, py0, pw, slicePh));
            crop.Freeze();
            segs[i] = new ArmSegment
            {
                T0 = t0, T1 = t1, TMid = tMid, Span = Math.Max(0.02, t1 - t0),
                RestAnchor = anchor,
                Rect = new Rect(sleeveRect.X, sy0, sleeveRect.Width, sy1 - sy0),
                Bmp = crop
            };
        }
        return new ArmRig
        {
            Shoulder = shoulder, RestPaw = restPaw,
            SleeveRect = sleeveRect, PawRect = pawRect,
            SleeveBmp = sleeveBmp, PawBmp = pawBmp,
            Segments = segs
        };
    }

    private static BitmapSource LoadPart(string name)
    {
        using (var stream = typeof(DogArt).Assembly.GetManifestResourceStream("KeycatDesktop.Pet." + name + ".png"))
        {
            if (stream == null) throw new InvalidOperationException("Missing pet image: " + name);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }

    private static Color ParseHex(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex.StartsWith("#") ? hex : "#" + hex);
    }

    private static Brush FreezeBrush(string hex)
    {
        var b = new SolidColorBrush(ParseHex(hex));
        b.Freeze();
        return b;
    }

    private static Pen MakePen(string hex, double width)
    {
        var p = new Pen(FreezeBrush(hex), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        p.Freeze();
        return p;
    }

    private static double Clamp01(double x) { return x < 0 ? 0 : (x > 1 ? 1 : x); }
    private static double Clamp(double x, double lo, double hi) { return x < lo ? lo : (x > hi ? hi : x); }
    private static double Smooth(double x) { x = Clamp01(x); return x * x * (3.0 - 2.0 * x); }

    private static Matrix UnifiedBodyMatrix(Point leftPaw, Point rightPaw, out double leftLean, out double rightReach, out double bodyTiltDeg)
    {
        leftLean = Smooth((157.0 - leftPaw.X) / 50.0);
        rightReach = Clamp((rightPaw.X - 248.0) / 55.0, 0.0, 0.75);
        double netLean = leftLean * 0.85 - rightReach * 0.35;
        double shiftX = -11.0 * leftLean + 5.5 * rightReach;
        bodyTiltDeg = -2.2 * leftLean + 1.5 * rightReach;

        var m = new Matrix(1, 0, 0.24 * netLean, 1, -246.0 * 0.24 * netLean, 0);
        m.RotateAt(bodyTiltDeg, 214.0, 247.0);
        m.Translate(shiftX, 0);
        return m;
    }

    public void DrawTailLayer(DrawingContext dc, double phaseRad)
    {
        dc.PushTransform(new RotateTransform(Math.Sin(phaseRad) * WagDeg, TailPivot.X, TailPivot.Y));
        dc.DrawImage(TailBmp, TailRect);
        dc.Pop();
    }

    public void DrawBodyLayer(DrawingContext dc)
    {
        dc.DrawImage(BodyBmp, BodyRect);
    }

    public void DrawRuffLayer(DrawingContext dc)
    {
        dc.DrawImage(RuffBmp, RuffRect);
    }

    public void DrawSleeveSegLayer(DrawingContext dc, bool left, int idx)
    {
        ArmSegment seg = (left ? ArmL : ArmR).Segments[idx];
        dc.PushTransform(new TranslateTransform(-seg.RestAnchor.X, -seg.RestAnchor.Y));
        dc.DrawImage(seg.Bmp, seg.Rect);
        dc.Pop();
    }

    public void DrawPawLayer(DrawingContext dc, bool left)
    {
        ArmRig arm = left ? ArmL : ArmR;
        dc.DrawImage(arm.PawBmp, arm.PawRect);
    }

    public void DrawHeadLayer(DrawingContext dc, double gazeX, double gazeY, double blink)
    {
        double gx = Clamp(gazeX, -1.0, 1.0);
        double gy = Clamp(gazeY, -1.0, 1.0);
        double bl = Clamp01(blink);
        dc.DrawImage(HeadBmp, HeadRect);
        DrawEye(dc, EyeL, gx, gy, bl);
        DrawEye(dc, EyeR, gx, gy, bl);
    }

    private void DrawEye(DrawingContext dc, EyeSpec eye, double gx, double gy, double blink)
    {
        double rx = eye.Rx * 1.03;
        double ry = eye.Ry * 1.03;
        double span = Math.Max(rx, ry) + 4.0;
        var clip = new EllipseGeometry(new Point(0, 0), rx, ry)
        {
            Transform = new RotateTransform(eye.Deg)
        };
        dc.PushTransform(new TranslateTransform(eye.X, eye.Y));
        dc.PushClip(clip);

        if (blink > 0.80)
        {
            dc.DrawEllipse(LidBrush, null, new Point(0, 0), span, span);
            var closed = new StreamGeometry();
            using (StreamGeometryContext g = closed.Open())
            {
                g.BeginFigure(new Point(-rx * 0.78, ry * 0.05), false, false);
                g.QuadraticBezierTo(new Point(0, ry * 0.36), new Point(rx * 0.78, ry * 0.03), true, false);
            }
            dc.DrawGeometry(null, LidLine, closed);
        }
        else
        {
            double px = gx * eye.Rx * 0.24;
            double py = gy * eye.Ry * 0.21;
            Rect dst = new Rect(eye.PupilRect.X + px, eye.PupilRect.Y + py, eye.PupilRect.Width, eye.PupilRect.Height);
            dc.DrawImage(eye.PupilBmp, dst);
            if (blink > 0.30)
            {
                double cover = Clamp01((blink - 0.30) / 0.50);
                double lidBottom = -ry + cover * ry * 1.42;
                var lid = new StreamGeometry();
                using (StreamGeometryContext g = lid.Open())
                {
                    g.BeginFigure(new Point(-span, -span), true, true);
                    g.LineTo(new Point(span, -span), true, false);
                    g.LineTo(new Point(span, lidBottom), true, false);
                    g.QuadraticBezierTo(new Point(0, lidBottom + ry * 0.22), new Point(-span, lidBottom), true, false);
                }
                dc.DrawGeometry(LidBrush, null, lid);
                var rim = new StreamGeometry();
                using (StreamGeometryContext g = rim.Open())
                {
                    g.BeginFigure(new Point(-rx * 0.90, lidBottom), false, false);
                    g.QuadraticBezierTo(new Point(0, lidBottom + ry * 0.22), new Point(rx * 0.90, lidBottom), true, false);
                }
                dc.DrawGeometry(null, LidLine, rim);
            }
        }

        dc.Pop();
        dc.Pop();
    }

    private static Point Cubic(Point p0, Point p1, Point p2, Point p3, double u)
    {
        double z = 1.0 - u;
        return new Point(
            z * z * z * p0.X + 3 * z * z * u * p1.X + 3 * z * u * u * p2.X + u * u * u * p3.X,
            z * z * z * p0.Y + 3 * z * z * u * p1.Y + 3 * z * u * u * p2.Y + u * u * u * p3.Y);
    }

    private static Point EvalCurve(Point p0, Point p1, Point p2, Point p3, Vector dir0, Vector dir1, double curveLen, double t)
    {
        if (t < 0.0) return p0 + dir0 * (t * curveLen);
        if (t > 1.0) return p3 + dir1 * ((t - 1.0) * curveLen);
        return Cubic(p0, p1, p2, p3, t);
    }

    private static void DrawCurvedCutoutArm(DrawingContext dc, ArmRig arm, bool left, Point targetPaw, Point catRestPaw, Matrix bodyMatrix)
    {
        Vector delta = targetPaw - catRestPaw;
        Point actualPaw = new Point(arm.RestPaw.X + delta.X, arm.RestPaw.Y + delta.Y);
        Point curShoulder = bodyMatrix.Transform(arm.Shoulder);

        Vector restVec = arm.RestPaw - arm.Shoulder;
        double restLen = Math.Max(8.0, restVec.Length);
        double restAng = Math.Atan2(restVec.Y, restVec.X) * 180.0 / Math.PI;

        double side = left ? -1.0 : 1.0;
        double dx = actualPaw.X - curShoulder.X;
        double dy = actualPaw.Y - curShoulder.Y;
        double active = Smooth(delta.Length / 14.0);
        double bend = Clamp((231.0 - actualPaw.Y) / 42.0, 0.0, 1.0);

        Point p1 = new Point(
            curShoulder.X + dx * (1.0 / 3.0 * (1.0 - active) + 0.24 * active) + active * side * (4.0 + 7.5 * bend),
            curShoulder.Y + dy * (1.0 / 3.0 * (1.0 - active) + 0.36 * active) + active * 4.5 * bend);
        Point p2 = new Point(
            curShoulder.X + dx * (2.0 / 3.0 * (1.0 - active) + 0.78 * active) + active * side * (3.0 + 11.5 * bend),
            actualPaw.Y - dy * (1.0 / 3.0 * (1.0 - active)) + active * (-15.0 + 23.0 * bend));

        double curveLen = 0.0;
        Point prev = curShoulder;
        for (int k = 1; k <= 8; k++)
        {
            Point pt = Cubic(curShoulder, p1, p2, actualPaw, k / 8.0);
            curveLen += (pt - prev).Length;
            prev = pt;
        }
        curveLen = Math.Max(8.0, curveLen);

        Vector dir0 = p1 - curShoulder;
        if (dir0.LengthSquared < 1e-4) dir0 = actualPaw - curShoulder;
        dir0.Normalize();
        Vector dir1 = actualPaw - p2;
        if (dir1.LengthSquared < 1e-4) dir1 = actualPaw - curShoulder;
        dir1.Normalize();

        double totalScale = curveLen / restLen;
        double widScale = Math.Max(0.95, Math.Min(1.06, 1.0 / Math.Pow(totalScale, 0.20)));

        for (int i = 0; i < arm.Segments.Length; i++)
        {
            ArmSegment seg = arm.Segments[i];
            Point ptA = EvalCurve(curShoulder, p1, p2, actualPaw, dir0, dir1, curveLen, seg.T0);
            Point ptB = EvalCurve(curShoulder, p1, p2, actualPaw, dir0, dir1, curveLen, seg.T1);
            Point ptM = new Point(0.5 * (ptA.X + ptB.X), 0.5 * (ptA.Y + ptB.Y));
            Vector segVec = ptB - ptA;
            double chord = Math.Max(0.5, segVec.Length);
            double ang = Math.Atan2(segVec.Y, segVec.X) * 180.0 / Math.PI;
            double segLenScale = chord / (seg.Span * restLen);

            dc.PushTransform(new TranslateTransform(ptM.X, ptM.Y));
            dc.PushTransform(new RotateTransform(ang));
            dc.PushTransform(new ScaleTransform(segLenScale, widScale));
            dc.PushTransform(new RotateTransform(-restAng));
            dc.PushTransform(new TranslateTransform(-seg.RestAnchor.X, -seg.RestAnchor.Y));
            dc.DrawImage(seg.Bmp, seg.Rect);
            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        double endAng = Math.Atan2(dir1.Y, dir1.X) * 180.0 / Math.PI;
        double dAng = endAng - restAng;
        while (dAng > 180.0) dAng -= 360.0;
        while (dAng < -180.0) dAng += 360.0;

        dc.PushTransform(new TranslateTransform(actualPaw.X, actualPaw.Y));
        dc.PushTransform(new RotateTransform(dAng * 0.30));
        dc.DrawImage(arm.PawBmp, arm.PawRect);
        dc.Pop();
        dc.Pop();
    }

    public void Draw(DrawingContext dc, double t, double gazeX, double gazeY,
                     double blink, Point leftPaw, Point rightPaw, double leftActivity, double rightActivity)
    {
        double gx = Clamp(gazeX, -1.0, 1.0);
        double gy = Clamp(gazeY, -1.0, 1.0);
        double breath = Math.Sin(t * 1.7) * 0.60;
        double leftLean, rightReach, bodyTiltDeg;
        Matrix mBody = UnifiedBodyMatrix(leftPaw, rightPaw, out leftLean, out rightReach, out bodyTiltDeg);

        dc.PushTransform(new MatrixTransform(mBody));
        DrawTailLayer(dc, t * WagSpeed);
        DrawBodyLayer(dc);
        dc.Pop();

        DrawCurvedCutoutArm(dc, ArmR, false, rightPaw, new Point(229.0, 233.0), mBody);
        DrawCurvedCutoutArm(dc, ArmL, true, leftPaw, new Point(192.0, 233.0), mBody);

        dc.PushTransform(new MatrixTransform(mBody));
        DrawRuffLayer(dc);
        dc.Pop();

        Point headPos = mBody.Transform(new Point(214.0, HeadPivotY));
        dc.PushTransform(new TranslateTransform(headPos.X + 2.5 * gx, headPos.Y + 2.0 * gy + breath));
        dc.PushTransform(new RotateTransform(bodyTiltDeg + 2.8 * gx - 1.6 * leftLean + 1.0 * rightReach));
        DrawHeadLayer(dc, gx, gy, blink);
        dc.Pop();
        dc.Pop();
    }

    public void DrawPortrait(DrawingContext dc)
    {
        DrawHeadLayer(dc, 0, 0, 0);
    }

    public bool ContainsHead(Point pointer, double time, double gazeX, double gazeY, Point leftPaw)
    {
        double gx = Clamp(gazeX, -1.0, 1.0);
        double gy = Clamp(gazeY, -1.0, 1.0);
        double breath = Math.Sin(time * 1.7) * 0.60;
        double leftLean, rightReach, bodyTiltDeg;
        Matrix mBody = UnifiedBodyMatrix(leftPaw, new Point(229.0, 233.0), out leftLean, out rightReach, out bodyTiltDeg);
        Point headPos = mBody.Transform(new Point(214.0, HeadPivotY));
        var m = Matrix.Identity;
        m.Rotate(bodyTiltDeg + 2.8 * gx - 1.6 * leftLean + 1.0 * rightReach);
        m.Translate(headPos.X + 2.5 * gx, headPos.Y + 2.0 * gy + breath);
        m.Invert();
        Point local = m.Transform(pointer);
        return Math.Pow(local.X / 66.0, 2) + Math.Pow((local.Y + 18.0) / 56.0, 2) < 1.0;
    }
}
