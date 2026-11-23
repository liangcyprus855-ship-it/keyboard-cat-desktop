using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Bake the lowered wrist for the start of a reaction, in the coordinate system
// of the finished pose. Both desktop renderers then use the same registered art.
internal static class GestureFrames
{
    static double Ease(double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return t*t*(3-2*t);
    }

    // The soft falloff bends the forearm at its attached sleeve. Moving only
    // this area keeps the crown, eyes, opposite arm and waist in place.
    static double Influence(double x, double y, bool thumb, bool office)
    {
        if (thumb)
        {
            double handX = office ? -.31 : -.25;
            double horizontal = 1 - Ease((Math.Abs(x-handX)-.045)/.155);
            return horizontal * Ease((y-(office ? .26 : .36))/.16)
                              * (1-Ease((y-(office ? .66 : .68))/.17));
        }
        double across = 1 - Ease((Math.Abs(x+.035)-.048)/.16);
        double face = office ? .435 : .55;
        return across * Ease((y-face)/.13) * (1-Ease((y-(face+.21))/.16));
    }

    static void Make(string original, string entry, bool thumb, bool office)
    {
        BitmapSource image;
        using (var file = File.OpenRead(original))
            image = new PngBitmapDecoder(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var input = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        int w = input.PixelWidth, h = input.PixelHeight, stride = 4*w;
        byte[] src = new byte[stride*h];
        input.CopyPixels(src, stride, 0);
        byte[] dst = (byte[])src.Clone();
        double dx = thumb ? .048 : -.023;
        double dy = thumb ? .085 : .067;
        for (int row=0;row<h;row++)
        for (int col=0;col<w;col++)
        {
            double x=(col+.5-w*.5)/h, y=(row+.5)/h;
            if (Influence(x,y,thumb,office)==0) continue;
            // The inverse displacement lies on the line parallel to (dx,dy).
            // Bisection avoids aliasing even near the edge of the moving sleeve.
            double lo=0, hi=1;
            for (int k=0;k<16;k++)
            {
                double t=(lo+hi)*.5;
                if (t < Influence(x-dx*t,y-dy*t,thumb,office)) lo=t; else hi=t;
            }
            double distance=(lo+hi)*.5;
            double sx=col-h*dx*distance, sy=row-h*dy*distance;
            if (sx<0 || sy<0 || sx>=w-1 || sy>=h-1)
            {
                Array.Clear(dst, (row*w+col)*4, 4);
                continue;
            }
            int x0=(int)sx, y0=(int)sy, start=(y0*w+x0)*4, dest=(row*w+col)*4;
            double fx=sx-x0, fy=sy-y0;
            for (int c=0;c<4;c++)
            {
                double top=src[start+c]*(1-fx)+src[start+4+c]*fx;
                double bottom=src[start+stride+c]*(1-fx)+src[start+stride+4+c]*fx;
                dst[dest+c]=(byte)Math.Round(top*(1-fy)+bottom*fy);
            }
        }
        var baked=BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,dst,stride);
        var png=new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(baked));
        Directory.CreateDirectory(Path.GetDirectoryName(entry));
        using (var file=File.Create(entry)) png.Save(file);
    }

    [STAThread]
    static void Main(string[] args)
    {
        string assets=Path.GetFullPath(args[0]);
        foreach (string role in new[]{"office","hairclip","ribbon","headband","glasses"})
        foreach (string pose in new[]{"hover","click"})
        {
            string folder=Path.Combine(assets,"characters",role);
            string target=Path.Combine(folder,pose+"-entry.png");
            string original=role=="office" ? Path.Combine(assets,"office-colleague-"+pose+".png") : Path.Combine(folder,pose+".png");
            Make(original,target,pose=="hover",role=="office");
        }
    }
}
