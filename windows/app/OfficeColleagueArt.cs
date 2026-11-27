using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal enum OfficePose { Idle, Working, Hover, Click, Sleeping }

internal sealed class OfficeColleagueArt
{
    static readonly Dictionary<CatStyle,string[]> Styles = ReadStyles();
    static readonly Dictionary<CatStyle,OfficeColleagueArt> Characters = new Dictionary<CatStyle,OfficeColleagueArt>();
    static readonly Brush SleepInk = FreezeBrush(Color.FromRgb(105, 108, 151));
    static readonly FormattedText SleepLabel = new FormattedText("Z z", CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 17, SleepInk, 1);

    readonly double CenterX, Top, Height, SolidHead, BottomHead;
    readonly Point LeftShoulder, RightShoulder;
    readonly BitmapSource Figure, Portrait;
    readonly Lazy<BitmapSource> Work, Sleep, Hover, Click;
    readonly Rect Bounds;
    readonly Lazy<BitArray> HeadMask;
    readonly Lazy<DrawingGroup> HeadHitSurface;
    readonly Brush Suit, Skin, Shirt;
    readonly Pen SuitEdge, Crease, SkinEdge;

    static Brush FreezeBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static Dictionary<CatStyle,string[]> ReadStyles()
    {
        var result = new Dictionary<CatStyle,string[]>();
        using (var stream=typeof(OfficeColleagueArt).Assembly.GetManifestResourceStream("KeycatDesktop.Characters.tsv"))
        using (var reader=new StreamReader(stream))
        {
            reader.ReadLine();
            string line;
            while ((line=reader.ReadLine())!=null)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                string[] fields=line.Split('\t');
                result.Add((CatStyle)Enum.Parse(typeof(CatStyle),fields[0]),fields);
            }
        }
        return result;
    }
    public static OfficeColleagueArt For(CatStyle style)
    {
        OfficeColleagueArt art;
        if (!Characters.TryGetValue(style,out art)) Characters.Add(style,art=new OfficeColleagueArt(style,Styles[style]));
        return art;
    }
    OfficeColleagueArt(CatStyle style,string[] f)
    {
        Func<int,double> number = i=>double.Parse(f[i],CultureInfo.InvariantCulture);
        Func<int,int> color = i=>Convert.ToInt32(f[i],16);
        CenterX=number(1); Top=number(2); Height=number(3);
        LeftShoulder=new Point(number(4),number(6)); RightShoulder=new Point(number(5),number(6));
        SolidHead=number(7); BottomHead=number(8);
        Suit=Gradient(color(9),color(10)); SuitEdge=Line(color(11),.72); Crease=Line(color(12),.72);
        Skin=Gradient(color(13),color(14)); SkinEdge=Line(color(15),.62);
        Shirt=Gradient(color(16),color(17));
        Figure=LoadFigure(style==CatStyle.OfficeColleague ? "OfficeColleague" : style+".Idle");
        Work=new Lazy<BitmapSource>(()=>LoadFigure(style+".Work"));
        Sleep=new Lazy<BitmapSource>(()=>LoadFigure(style+".Sleep"));
        Hover=new Lazy<BitmapSource>(()=>LoadFigure(style+".Hover"));
        Click=new Lazy<BitmapSource>(()=>LoadFigure(style+".Click"));
        Bounds=ImageBounds(Figure); Portrait=CropPortrait();
        HeadMask=new Lazy<BitArray>(MakeHeadMask);
        HeadHitSurface=new Lazy<DrawingGroup>(MakeHeadHitSurface);
    }
    static readonly Geometry Hand = Shape(
        "M -3.6,-7.5 C -4.0,-4.1 -5.7,-2.4 -5.6,.8 " +
        "C -5.6,3.1 -5.1,5.1 -4.0,5.3 Q -3.2,5.5 -2.9,4.4 " +
        "Q -2.6,7.0 -1.2,7.0 Q -.2,7.0 .1,5.3 " +
        "Q .5,7.3 1.7,7.1 Q 3.0,6.9 3.2,4.5 " +
        "Q 4.5,5.7 5.1,4.0 L 5.5,.9 Q 6.8,2.5 7.6,1.4 " +
        "Q 8.3,.3 6.9,-1.7 L 4.0,-5.1 L 3.5,-7.5 Z");

    static BitmapSource LoadFigure(string name)
    {
        using (var stream = typeof(OfficeColleagueArt).Assembly.GetManifestResourceStream("KeycatDesktop." + name + ".png"))
        {
            if (stream == null) throw new InvalidOperationException("Missing office colleague image.");
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream;
            bitmap.EndInit(); bitmap.Freeze();
            return bitmap;
        }
    }
    Rect ImageBounds(BitmapSource bitmap)
    {
        double width = Height*bitmap.PixelWidth/bitmap.PixelHeight;
        return new Rect(CenterX-width/2,Top,width,Height);
    }
    BitmapSource CropPortrait()
    {
        int height = (int)Math.Round(Figure.PixelHeight * .67);
        int width = Math.Min(Figure.PixelWidth,height);
        var crop = new CroppedBitmap(Figure,new Int32Rect((Figure.PixelWidth-width)/2,0,width,height));
        crop.Freeze();
        return crop;
    }
    BitArray MakeHeadMask()
    {
        var image = new FormatConvertedBitmap(Figure,PixelFormats.Bgra32,null,0);
        int width = image.PixelWidth, rows = (int)Math.Ceiling(image.PixelHeight * BottomHead);
        byte[] pixels = new byte[width * rows * 4];
        image.CopyPixels(new Int32Rect(0,0,width,rows),pixels,width*4,0);
        var mask = new BitArray(width*rows);
        int upper = width * (int)Math.Ceiling(image.PixelHeight * SolidHead);
        for (int i=0; i<mask.Count; i++)
        {
            int n=i*4;
            mask[i] = pixels[n+3] >= 32 && (i<upper || pixels[n+2]>pixels[n]);
        }
        return mask;
    }
    DrawingGroup MakeHeadHitSurface()
    {
        var mask = HeadMask.Value;
        int width = Figure.PixelWidth, rows = mask.Count / width;
        byte[] pixels = new byte[mask.Count * 4];
        for (int i=0; i<mask.Count; i++)
            if (mask[i]) pixels[i*4+3] = 1;
        var bitmap = BitmapSource.Create(width,rows,96,96,PixelFormats.Pbgra32,null,pixels,width*4);
        bitmap.Freeze();
        var drawing = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(drawing,BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc=drawing.Open())
            dc.DrawImage(bitmap,new Rect(Bounds.X,Top,Bounds.Width,Height*rows/Figure.PixelHeight));
        drawing.Freeze();
        return drawing;
    }
    static Geometry Shape(string path) { var g = Geometry.Parse(path); g.Freeze(); return g; }
    static Color ColorOf(int rgb)
    {
        return Color.FromRgb((byte)(rgb >> 16),(byte)(rgb >> 8),(byte)rgb);
    }
    static Brush Gradient(int light,int dark)
    {
        var b = new LinearGradientBrush(ColorOf(light),ColorOf(dark),new Point(.1,.1),new Point(.9,.85));
        b.Freeze(); return b;
    }
    static Pen Line(int rgb,double width)
    {
        var pen = new Pen(new SolidColorBrush(ColorOf(rgb)),width) {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
        };
        pen.Freeze(); return pen;
    }
    public void DrawPortrait(DrawingContext dc)
    {
        dc.DrawImage(Portrait,new Rect(0,0,60.0*Portrait.PixelWidth/Portrait.PixelHeight,60));
    }

    public bool ContainsHead(Point p)
    {
        if (p.X < Bounds.Left || p.X >= Bounds.Right || p.Y < Top || p.Y >= Top + Height*BottomHead) return false;
        int x=(int)((p.X-Bounds.Left)*Figure.PixelWidth/Bounds.Width);
        int y=(int)((p.Y-Top)*Figure.PixelHeight/Height);
        return HeadMask.Value[y*Figure.PixelWidth+x];
    }
    public void DrawIdle(DrawingContext dc) { dc.DrawImage(Figure,Bounds); }
    public void Draw(DrawingContext dc, OfficePose pose, Point left, Point right, bool interactive,
        bool showLeft = true, bool showRight = true, double time = 0, KCSceneState state = null)
    {
        if (pose == OfficePose.Hover || pose == OfficePose.Click)
        {
            if (interactive) dc.DrawDrawing(HeadHitSurface.Value);
            BitmapSource figure = pose == OfficePose.Hover ? Hover.Value : Click.Value;
            dc.DrawImage(figure,ImageBounds(figure));
            return;
        }

        if (pose == OfficePose.Sleeping)
        {
            double nod = .985 + Math.Cos(time * 2.2) * .009;
            dc.PushTransform(new ScaleTransform(1,nod,CenterX,Bounds.Bottom));
            dc.DrawImage(Sleep.Value,Bounds);
            dc.Pop();
            dc.DrawText(SleepLabel,new Point(Bounds.Right - 10,Bounds.Top + 18 + Math.Sin(time * 2) * 3));
            return;
        }
        if (pose != OfficePose.Working || (!showLeft && !showRight))
        {
            DrawIdle(dc);
            return;
        }
        KCPoint baseLeft = KeycatCore.kc_pt(LeftShoulder.X, LeftShoulder.Y + 2.0);
        KCPoint baseRight = KeycatCore.kc_pt(RightShoulder.X, RightShoulder.Y + 2.0);
        KCOfficeBodyPose body = state != null
            ? KeycatCore.kc_office_body_pose(state, CenterX, Bounds.Bottom, baseLeft, baseRight)
            : new KCOfficeBodyPose { leftShoulder = baseLeft, rightShoulder = baseRight };
        var bodyTransform = new TransformGroup();
        bodyTransform.Children.Add(new RotateTransform(body.tiltDeg, CenterX, Bounds.Bottom));
        bodyTransform.Children.Add(new TranslateTransform(body.offsetX, body.offsetY));

        Point animLeft = WpfPt(body.leftShoulder);
        Point animRight = WpfPt(body.rightShoulder);
        Point bottomCornerL = new Point(animLeft.X - 8, Bounds.Bottom + body.offsetY - 4);
        Point bottomCornerR = new Point(animRight.X + 8, Bounds.Bottom + body.offsetY - 4);
        KCOfficeArmGeometry rightGeom = default(KCOfficeArmGeometry);
        KCOfficeArmGeometry leftGeom = default(KCOfficeArmGeometry);

        // Draw arms behind the full opaque torso/vest/hair so armholes and hair locks stay seamless.
        if (!showLeft) DrawArm(dc, SolveArm(animLeft, bottomCornerL, -1, true));
        if (!showRight) DrawArm(dc, SolveArm(animRight, bottomCornerR, 1, true));
        if (showRight) DrawArm(dc, rightGeom = SolveArm(animRight, right, 1, false));
        if (showLeft) DrawArm(dc, leftGeom = SolveArm(animLeft, left, -1, false));

        dc.PushTransform(bodyTransform);
        dc.DrawImage(Work.Value, Bounds);
        dc.Pop();

        // Keep active typing/mouse fingers visible above the bottom hem and hair tips.
        if (showRight) DrawHand(dc, rightGeom);
        if (showLeft) DrawHand(dc, leftGeom);
    }

    static Point WpfPt(KCPoint p) { return new Point(p.x, p.y); }

    static KCOfficeArmGeometry SolveArm(Point shoulder, Point target, int side, bool resting)
    {
        return KeycatCore.kc_solve_office_arm(
            KeycatCore.kc_pt(shoulder.X, shoulder.Y),
            KeycatCore.kc_pt(target.X, target.Y),
            side,
            resting ? 1 : 0);
    }

    void DrawHand(DrawingContext dc, KCOfficeArmGeometry arm)
    {
        dc.PushTransform(new TranslateTransform(arm.palm.x, arm.palm.y));
        dc.PushTransform(new RotateTransform(arm.handAngleDeg));
        dc.PushTransform(new ScaleTransform(arm.handScaleX, arm.handScaleY));
        dc.DrawGeometry(Skin, SkinEdge, Hand);
        dc.Pop(); dc.Pop(); dc.Pop();
    }

    void DrawArm(DrawingContext dc, KCOfficeArmGeometry arm)
    {
        var sleeve=new StreamGeometry();
        using(var g=sleeve.Open())
        {
            g.BeginFigure(WpfPt(arm.sleeveStart),true,true);
            g.BezierTo(WpfPt(arm.c1a),WpfPt(arm.c1b),WpfPt(arm.before),true,true);
            g.QuadraticBezierTo(WpfPt(arm.elbowJointOuter),WpfPt(arm.after),true,true);
            g.BezierTo(WpfPt(arm.c2a),WpfPt(arm.c2b),WpfPt(arm.endOuter),true,true);
            g.LineTo(WpfPt(arm.endInner),true,true);
            g.BezierTo(WpfPt(arm.c3a),WpfPt(arm.c3b),WpfPt(arm.afterInner),true,true);
            g.QuadraticBezierTo(WpfPt(arm.elbowJointInner),WpfPt(arm.beforeInner),true,true);
            g.BezierTo(WpfPt(arm.c4a),WpfPt(arm.c4b),WpfPt(arm.shoulderInner),true,true);
            g.BezierTo(WpfPt(arm.c5a),WpfPt(arm.c5b),WpfPt(arm.sleeveStart),true,true);
        }
        sleeve.Freeze();
        dc.DrawGeometry(Suit,null,sleeve);
        dc.DrawLine(Crease,WpfPt(arm.creaseStart),WpfPt(arm.creaseEnd));
        var cuff=new StreamGeometry();
        using(var g=cuff.Open())
        {
            g.BeginFigure(WpfPt(arm.cuff0),true,true);
            g.LineTo(WpfPt(arm.cuff1),true,true);
            g.LineTo(WpfPt(arm.cuff2),true,true);
            g.LineTo(WpfPt(arm.cuff3),true,true);
        }
        cuff.Freeze();
        dc.DrawGeometry(Shirt,null,cuff);
        dc.DrawLine(SuitEdge,WpfPt(arm.edgeStart),WpfPt(arm.edgeEnd));
        DrawHand(dc, arm);
    }
}
