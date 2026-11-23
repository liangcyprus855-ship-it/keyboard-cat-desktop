using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class OfficeColleagueArt
{
    const double CenterX = 214, Top = 46, Height = 196, HandScale = 1.30;
    static readonly BitmapSource Figure = LoadFigure();
    static readonly Rect Bounds = new Rect(CenterX - Height * Figure.PixelWidth / Figure.PixelHeight / 2,
        Top, Height * Figure.PixelWidth / Figure.PixelHeight, Height);
    static readonly BitmapSource Portrait = CropPortrait();
    static readonly Geometry Hand = Shape(
        "M -4.3,-5.2 C -4.7,-3.3 -5.1,-2.0 -5.4,-0.1 " +
        "C -5.6,1.4 -5.4,3.7 -5.1,4.9 C -4.8,6.0 -3.7,6.0 -3.4,4.9 " +
        "L -3.1,3.5 L -3.0,6.1 C -2.9,7.4 -1.2,7.7 -.9,6.3 " +
        "L -.7,4.1 L -.4,6.8 C -.2,8.3 1.5,8.2 1.7,6.8 " +
        "L 1.9,4.1 L 2.2,6.4 C 2.4,7.8 4.0,7.5 4.1,6.1 " +
        "L 4.5,1.6 Q 4.6,.3 5.1,1.0 Q 5.4,2.0 6.4,2.1 " +
        "Q 7.9,1.9 7.2,.1 L 5.8,-2.7 Q 5.2,-4.1 3.8,-5.2 " +
        "L 3.3,-7.8 L -3.5,-7.7 Z");
    static readonly Brush Suit = Gradient(0x393b4a,0x30323f);
    static readonly Brush Skin = Gradient(0xffdbb6,0xffc49c);
    static readonly Brush Shirt = Gradient(0xfffffa,0xdbdce0);
    static readonly Pen SuitEdge = Line(0x292a36,.72);
    static readonly Pen Crease = Line(0x4c4e5d,.72);
    static readonly Pen SkinEdge = Line(0xe89d70,.62);

    static BitmapSource LoadFigure()
    {
        using (var stream = typeof(OfficeColleagueArt).Assembly.GetManifestResourceStream("KeycatDesktop.OfficeColleague.png"))
        {
            if (stream == null) throw new InvalidOperationException("Missing office colleague image.");
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream;
            bitmap.EndInit(); bitmap.Freeze();
            return bitmap;
        }
    }
    static BitmapSource CropPortrait()
    {
        int height = (int)Math.Round(Figure.PixelHeight * .67);
        int width = Math.Min(Figure.PixelWidth,height);
        var crop = new CroppedBitmap(Figure,new Int32Rect((Figure.PixelWidth-width)/2,0,width,height));
        crop.Freeze();
        return crop;
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
    public static void DrawPortrait(DrawingContext dc)
    {
        dc.DrawImage(Portrait,new Rect(0,0,60.0*Portrait.PixelWidth/Portrait.PixelHeight,60));
    }

    static double Lean(Point mouseHand)
    {
        double u = Math.Max(0,Math.Min(1,(157-mouseHand.X)/50));
        return u*u*(3-2*u);
    }
    static Matrix Pose(double t,double gx,double gy,double lean,double blink)
    {
        double shear = .078*lean-.008*gx;
        double stretch = (Math.Sin(t*1.7)*.38 + gy*.35 - blink*1.1)/Height;
        double baseY = Bounds.Bottom;
        return new Matrix(1,0,shear,1+stretch,-shear*baseY,-stretch*baseY);
    }
    public static bool ContainsHead(Point p,double t,double gx,double gy,Point leftHand)
    {
        Matrix m=Pose(t,gx,gy,Lean(leftHand),0);
        m.Invert(); p=m.Transform(p);
        double dx=(p.X-CenterX)/(Bounds.Width*.43), dy=(p.Y-Top-Height*.32)/(Height*.32);
        return dx*dx+dy*dy<1;
    }
    public static void DrawBody(DrawingContext dc,double t,double gx,double gy,double lean,double blink)
    {
        dc.PushTransform(new MatrixTransform(Pose(t,gx,gy,lean,blink)));
        dc.DrawImage(Figure,Bounds); dc.Pop();
    }
    public static void Draw(DrawingContext dc,double t,double gx,double gy,double blink,Point left,Point right)
    {
        double lean=Lean(left);
        Matrix pose=Pose(t,gx,gy,lean,blink);
        DrawBody(dc,t,gx,gy,lean,blink);
        DrawArm(dc,pose.Transform(new Point(246,172)),right,1);
        DrawArm(dc,pose.Transform(new Point(182,172)),left,-1);
    }

    static Vector Unit(Vector v) { return v.LengthSquared>.000001 ? v/v.Length : new Vector(0,1); }
    static Vector Normal(Vector v) { return new Vector(-v.Y,v.X); }

    static void DrawArm(DrawingContext dc,Point shoulder,Point target,int side)
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
