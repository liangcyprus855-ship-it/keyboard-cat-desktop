using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal enum OfficePose { Idle, Working, Hover, Click }

internal sealed class OfficeColleagueArt
{
    const double HandScale = 1.30;
    static readonly Dictionary<CatStyle,string[]> Styles = ReadStyles();
    static readonly Dictionary<CatStyle,OfficeColleagueArt> Characters = new Dictionary<CatStyle,OfficeColleagueArt>();
    readonly double CenterX, Top, Height, SolidHead, BottomHead;
    readonly Point LeftShoulder, RightShoulder;
    readonly BitmapSource Figure, Portrait;
    readonly Lazy<BitmapSource> Hover, Click;
    readonly Rect Bounds;
    readonly Lazy<BitArray> HeadMask;
    readonly Lazy<DrawingGroup> HeadHitSurface;
    readonly Brush Suit, Skin, Shirt;
    readonly Pen SuitEdge, Crease, SkinEdge;

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
        Hover=new Lazy<BitmapSource>(()=>LoadFigure(style+".Hover"));
        Click=new Lazy<BitmapSource>(()=>LoadFigure(style+".Click"));
        Bounds=ImageBounds(Figure); Portrait=CropPortrait();
        HeadMask=new Lazy<BitArray>(MakeHeadMask);
        HeadHitSurface=new Lazy<DrawingGroup>(MakeHeadHitSurface);
    }
    static readonly Geometry Hand = Shape(
        "M -4.3,-5.2 C -4.7,-3.3 -5.1,-2.0 -5.4,-0.1 " +
        "C -5.6,1.4 -5.4,3.7 -5.1,4.9 C -4.8,6.0 -3.7,6.0 -3.4,4.9 " +
        "L -3.1,3.5 L -3.0,6.1 C -2.9,7.4 -1.2,7.7 -.9,6.3 " +
        "L -.7,4.1 L -.4,6.8 C -.2,8.3 1.5,8.2 1.7,6.8 " +
        "L 1.9,4.1 L 2.2,6.4 C 2.4,7.8 4.0,7.5 4.1,6.1 " +
        "L 4.5,1.6 Q 4.6,.3 5.1,1.0 Q 5.4,2.0 6.4,2.1 " +
        "Q 7.9,1.9 7.2,.1 L 5.8,-2.7 Q 5.2,-4.1 3.8,-5.2 " +
        "L 3.3,-7.8 L -3.5,-7.7 Z");

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
            // Along the collar, keep the warm face and hair pixels and exclude
            // the blue jacket and white shirt sharing those same scan lines.
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
    public void Draw(DrawingContext dc, OfficePose pose, Point left, Point right, bool interactive)
    {
        if (pose == OfficePose.Hover || pose == OfficePose.Click)
        {
            // The stable head surface keeps layered-window hits on the original
            // hairline when the current gesture changes its visible outline.
            if (interactive) dc.DrawDrawing(HeadHitSurface.Value);
            BitmapSource figure = pose == OfficePose.Hover ? Hover.Value : Click.Value;
            dc.DrawImage(figure,ImageBounds(figure));
            return;
        }

        DrawIdle(dc);
        if (pose == OfficePose.Working)
        {
            DrawArm(dc,RightShoulder,right,1);
            DrawArm(dc,LeftShoulder,left,-1);
        }
    }

    static Vector Unit(Vector v) { return v.LengthSquared>.000001 ? v/v.Length : new Vector(0,1); }
    static Vector Normal(Vector v) { return new Vector(-v.Y,v.X); }

    void DrawArm(DrawingContext dc,Point shoulder,Point target,int side)
    {
        // The fingers stay over their target. Bending the elbow outward gives a
        // tucked, legible forearm when the hand comes up between keystrokes.
        double angle=Math.Max(-31,Math.Min(31,-(target.X-shoulder.X)*.34));
        double rad=angle*Math.PI/180;
        Vector down=new Vector(-Math.Sin(rad),Math.Cos(rad));
        Point palm=target-down*(5.5*HandScale);
        Point wrist=palm-down*(7.4*HandScale);
        Vector reach=wrist-shoulder;
        double bend=Math.Max(8,Math.Min(20,29-reach.Length*.15));
        Point elbow=shoulder+reach*.54-Normal(Unit(reach))*side*bend;
        Vector upper=Unit(elbow-shoulder), lower=Unit(wrist-elbow);
        Vector a=Normal(upper), b=Normal(Unit(upper+lower)), c=Normal(lower);
        Point end=wrist-lower*3.6;
        double radius=Math.Min(13,Math.Min((elbow-shoulder).Length,(end-elbow).Length)*.38);
        Point before=elbow-upper*radius, after=elbow+lower*radius;
        Vector joint=b*(8.5/Math.Max(.5,Vector.Multiply(a,b)));
        var sleeve=new StreamGeometry();
        using(var g=sleeve.Open())
        {
            g.BeginFigure(shoulder+a*8,true,true);
            g.BezierTo(shoulder+upper*9+a*9.8,before-upper*4+a*8.5,before+a*8.5,true,true);
            g.QuadraticBezierTo(elbow+joint,after+c*8.5,true,true);
            g.BezierTo(after+lower*4+c*8.5,end-lower*6+c*6.2,end+c*5.7,true,true);
            g.LineTo(end-c*5.7,true,true);
            g.BezierTo(end-lower*6-c*6.2,after+lower*4-c*8.5,after-c*8.5,true,true);
            g.QuadraticBezierTo(elbow-joint,before-a*8.5,true,true);
            g.BezierTo(before-upper*4-a*8.5,shoulder+upper*9-a*9.8,shoulder-a*8,true,true);
            g.BezierTo(shoulder-a*8-upper*9.3,shoulder+a*8-upper*9.3,shoulder+a*8,true,true);
        }
        dc.DrawGeometry(Suit,SuitEdge,sleeve);
        dc.DrawLine(Crease,elbow+b*(side*6.7),elbow+lower*5+b*(side*2.2));
        // A small area of white shirt at the edge of the jacket stays attached
        // through strong reaches, even when the hand rotates across the board.
        Vector handNormal=Normal(down);
        var cuff=new StreamGeometry();
        using(var g=cuff.Open())
        {
            g.BeginFigure(end+c*5.0,true,true); g.LineTo(end-c*5.0,true,true);
            g.LineTo(palm-down*(6.5*HandScale)-handNormal*(4.0*HandScale),true,true);
            g.LineTo(palm-down*(6.5*HandScale)+handNormal*(4.0*HandScale),true,true);
        }
        dc.DrawGeometry(Shirt,null,cuff);
        dc.PushTransform(new TranslateTransform(palm.X,palm.Y));
        dc.PushTransform(new RotateTransform(angle));
        dc.PushTransform(new ScaleTransform(-side*HandScale,HandScale));
        dc.DrawGeometry(Skin,SkinEdge,Hand);
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}
