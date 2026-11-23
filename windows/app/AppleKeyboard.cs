using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

public static partial class DeskArt
{
    private static readonly Dictionary<int,Key> AppleKeys = new Dictionary<int,Key>();
    private static readonly Typeface AppleSymbols = new Typeface("Segoe UI Symbol");
    private const double ApplePitch = 1.02, AppleRear = -.49, AppleLift = .44;

    private static void AppleKey(int row, int code, string legend, string symbol,
        double x, double width, double offset = 0, double height = 1)
    {
        // Map the compact 14.5-unit Mac layout onto the same desk plane as the hand targets.
        var key = new Key { Code=code, Legend=legend, Symbol=symbol, U=x*15/14.5,
            V=AppleRear+(row+offset)*ApplePitch, Units=width*15/14.5, Height=height*ApplePitch };
        key.Center=Project(key.U+key.Units*.5,key.V+key.Height*.5,AppleLift);
        AppleKeys.Add(code,key);
    }

    private static void AppleLetters(int row, string letters, double start)
    {
        for (int i=0; i<letters.Length; i++) AppleKey(row,letters[i],letters[i].ToString(),null,start+i,1);
    }

    private static int AppleCode(int vk)
    {
        switch(vk)
        {
            case 0xA3: return 0xA2; // A compact Apple keyboard has one Control key.
            case 0x2E: return 8;
            case 0x5F: return 261;
            case 0xB1: return 0x76; // Media previous / play / next, mute / volume down / up.
            case 0xB3: return 0x77;
            case 0xB0: return 0x78;
            case 0xAD: return 0x79;
            case 0xAE: return 0x7A;
            case 0xAF: return 0x7B;
            default: return vk;
        }
    }

    internal static string AppleKeyLabel(int vk)
    {
        int code=AppleCode(vk);
        if (code>=112 && code<=123) return "F"+(code-111);
        switch(code)
        {
            case 0x5B: case 0x5C: return "⌘";
            case 0xA4: case 0xA5: case 0x12: return "⌥";
            case 0xA2: case 0x11: return "⌃";
            case 0xA0: case 0xA1: case 0x10: return "⇧";
            case 0x25: return "←"; case 0x26: return "↑";
            case 0x27: return "→"; case 0x28: return "↓";
            case 256: return "fn"; case 261: return "锁定";
            case 8: return "⌫";
            default: return null;
        }
    }

    private static KeySurface BuildApple()
    {
        AppleKey(0,256,"fn","◎",0,1); AppleKey(0,0xA2,"control","⌃",1,1);
        AppleKey(0,0xA4,"option","⌥",2,1); AppleKey(0,0x5B,"command","⌘",3,1.25);
        AppleKey(0,32,"",null,4.25,5); AppleKey(0,0x5C,"command","⌘",9.25,1.25);
        AppleKey(0,0xA5,"option","⌥",10.5,1); AppleKey(0,0x25,"◂",null,11.5,1);
        AppleKey(0,0x28,"▾",null,12.5,1,0,.5); AppleKey(0,0x26,"▴",null,12.5,1,.5,.5);
        AppleKey(0,0x27,"▸",null,13.5,1);

        AppleKey(1,0xA0,"⇧",null,0,2.25); AppleLetters(1,"ZXCVBNM",2.25);
        AppleKey(1,0xBC,",",null,9.25,1); AppleKey(1,0xBE,".",null,10.25,1);
        AppleKey(1,0xBF,"/",null,11.25,1); AppleKey(1,0xA1,"⇧",null,12.25,2.25);

        AppleKey(2,0x14,"中/英","⇪",0,1.75); AppleLetters(2,"ASDFGHJKL",1.75);
        AppleKey(2,0xBA,";",null,10.75,1); AppleKey(2,0xDE,"'",null,11.75,1);
        AppleKey(2,13,"↵",null,12.75,1.75);

        AppleKey(3,9,"⇥",null,0,1.5); AppleLetters(3,"QWERTYUIOP",1.5);
        AppleKey(3,0xDB,"[",null,11.5,1); AppleKey(3,0xDD,"]",null,12.5,1); AppleKey(3,0xDC,"\\",null,13.5,1);

        AppleKey(4,0xC0,"`","~",0,1);
        const string shiftNumbers="!@#$%^&*()";
        for (int i=0;i<10;i++) {char digit=(char)('0'+(i+1)%10); AppleKey(4,digit,digit.ToString(),shiftNumbers[i].ToString(),i+1,1);}
        AppleKey(4,0xBD,"-","_",11,1); AppleKey(4,0xBB,"=","+",12,1); AppleKey(4,8,"⌫",null,13,1.5);

        AppleKey(5,27,"esc",null,0,1.5);
        string[] symbols={"☼","☀","▦","⚲","♩","☾","◂◂","▸Ⅱ","▸▸","◁","◁)","◁))"};
        for(int i=0;i<12;i++) AppleKey(5,112+i,"F"+(i+1),symbols[i],1.5+i,1);
        AppleKey(5,261,"",null,13.5,1);

        var surface = new KeySurface { Board=new DrawingGroup() };
        Brush face=Paint(0xF6F7F9), skirt=Paint(0xA2A5A8), text=Paint(0x4E5359);
        Brush active=Paint(0xC9DEF2), activeText=Paint(0x344C61);
        Geometry deck=Panel(-.18,AppleRear-.12,15.18,AppleRear+6*ApplePitch+.12,.52,.58,0,false);
        using (DrawingContext dc=surface.Board.Open())
        {
            dc.PushTransform(new TranslateTransform(0,2));
            dc.DrawGeometry(Paint(0xA1A5A9),Line(0x999C9F,.42),deck);
            dc.Pop();
            dc.DrawGeometry(Enamel(0xC7C9CB),Line(0xAAADB0,.56),deck);
            foreach (Key key in AppleKeys.Values)
            {
                double u0=key.U+.06,u1=key.U+key.Units-.06,v0=key.V+.053,v1=key.V+key.Height-.053;
                Geometry cap=Panel(u0,v0,u1,v1,.105,.12,AppleLift,false);
                dc.DrawGeometry(skirt,null,Panel(u0-.019,v0-.013,u1+.019,v1+.025,.12,.14,-.14,false));
                dc.DrawGeometry(face,null,cap);
                AppleLegend(dc,key,text);
                var lit=new DrawingGroup();
                using(DrawingContext press=lit.Open())
                {
                    press.DrawGeometry(active,null,cap);
                    AppleLegend(press,key,activeText);
                }
                lit.Freeze(); surface.Lights.Add(key.Code,lit);
            }
        }
        surface.Board.Freeze();
        return surface;
    }

    private static void AppleText(DrawingContext dc,string label,double x,double y,double size,Brush ink,Typeface font)
    {
        var glyph=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,font,size,ink,1.0);
        dc.DrawText(glyph,new Point(x-glyph.Width/2,y-glyph.Height/2));
    }

    private static void AppleLegend(DrawingContext dc, Key key, Brush ink)
    {
        Point p=key.Center;
        dc.PushTransform(new RotateTransform(180,p.X,p.Y));
        if (key.Code==261)
        {
            var pen=new Pen(ink,.42);
            dc.DrawRoundedRectangle(null,pen,new Rect(p.X-1.16,p.Y-.13,2.32,1.85),.28,.28);
            dc.DrawEllipse(null,pen,new Point(p.X,p.Y-.8),.77,.84);
        }
        else if (key.Symbol==null)
        {
            if (key.Legend.Length>0) AppleText(dc,key.Legend,p.X,p.Y,key.Height<.6?2.45:3.45,ink,Face);
        }
        else
        {
            double y=p.Y-1.15;
            if (key.Code==256 || key.Code==115 || key.Code==116)
            {
                var line=new Pen(ink,.32);
                if (key.Code==256)
                {
                    dc.DrawEllipse(null,line,new Point(p.X,y),1.35,1.27);
                    dc.DrawEllipse(null,line,new Point(p.X,y),.53,1.25);
                    dc.DrawLine(line,new Point(p.X-1.28,y),new Point(p.X+1.28,y));
                }
                else if (key.Code==115)
                {
                    dc.DrawEllipse(null,line,new Point(p.X-.3,y-.3),.98,.98);
                    dc.DrawLine(line,new Point(p.X+.4,y+.4),new Point(p.X+1.36,y+1.36));
                }
                else
                {
                    dc.DrawRoundedRectangle(null,line,new Rect(p.X-.47,y-1.40,.94,1.68),.45,.45);
                    dc.DrawLine(line,new Point(p.X-.89,y+.12),new Point(p.X-.6,y+.73));
                    dc.DrawLine(line,new Point(p.X-.6,y+.73),new Point(p.X+.6,y+.73));
                    dc.DrawLine(line,new Point(p.X+.6,y+.73),new Point(p.X+.89,y+.12));
                    dc.DrawLine(line,new Point(p.X,y+.73),new Point(p.X,y+1.37));
                }
            }
            else AppleText(dc,key.Symbol,p.X,y,2.65,ink,AppleSymbols);
            double size=key.Legend.Length>=6?2.08:2.65;
            AppleText(dc,key.Legend,p.X,p.Y+1.32,size,ink,Face);
        }
        dc.Pop();
    }
}
