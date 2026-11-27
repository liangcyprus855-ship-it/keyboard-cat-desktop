using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;
using Forms = System.Windows.Forms;

internal sealed class StyleMenu : IDisposable
{
    internal static readonly KeyValuePair<CatStyle, string>[] CharacterList = new KeyValuePair<CatStyle, string>[]
    {
        new KeyValuePair<CatStyle, string>(CatStyle.Black, "经典黑猫"),
        new KeyValuePair<CatStyle, string>(CatStyle.Ginger, "奶油橘猫"),
        new KeyValuePair<CatStyle, string>(CatStyle.JapaneseBobtail, "日本短尾三花"),
        new KeyValuePair<CatStyle, string>(CatStyle.PhotoTabby, "长毛虎斑"),
        new KeyValuePair<CatStyle, string>(CatStyle.OfficeColleague, "西装同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Hairclip, "发卡同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Ribbon, "红结同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Headband, "发带同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Glasses, "圆镜同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Hoodie, "耳机同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Beret, "贝雷帽同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Cap, "棒球帽同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Boss, "组长同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Shiba, "柴犬同事"),
        new KeyValuePair<CatStyle, string>(CatStyle.Dachshund, "腊肠犬"),
        new KeyValuePair<CatStyle, string>(CatStyle.Corgi, "柯基犬"),
        new KeyValuePair<CatStyle, string>(CatStyle.BlackShiba, "黑柴犬"),
        new KeyValuePair<CatStyle, string>(CatStyle.Bichon, "比熊犬"),
    };

    internal static readonly KeyValuePair<KeyboardStyle, string>[] KeyboardList = new KeyValuePair<KeyboardStyle, string>[]
    {
        new KeyValuePair<KeyboardStyle, string>(KeyboardStyle.Classic, "经典奶油"),
        new KeyValuePair<KeyboardStyle, string>(KeyboardStyle.Round, "薄荷打字机"),
        new KeyValuePair<KeyboardStyle, string>(KeyboardStyle.Neon, "冰蓝电竞"),
        new KeyValuePair<KeyboardStyle, string>(KeyboardStyle.Wood, "胡桃木"),
        new KeyValuePair<KeyboardStyle, string>(KeyboardStyle.Apple, "苹果妙控键盘"),
    };

    internal static readonly KeyValuePair<MouseStyle, string>[] MouseList = new KeyValuePair<MouseStyle, string>[]
    {
        new KeyValuePair<MouseStyle, string>(MouseStyle.Classic, "奶油滚轮"),
        new KeyValuePair<MouseStyle, string>(MouseStyle.Pebble, "雾紫鹅卵石"),
        new KeyValuePair<MouseStyle, string>(MouseStyle.Gaming, "冰蓝电竞"),
        new KeyValuePair<MouseStyle, string>(MouseStyle.Trackball, "酒红轨迹球"),
        new KeyValuePair<MouseStyle, string>(MouseStyle.Apple, "苹果妙控鼠标"),
    };

    internal static readonly KeyValuePair<int, string>[] DeskPropList = new KeyValuePair<int, string>[]
    {
        new KeyValuePair<int, string>(KeycatCore.KC_PROP_NONE, "无装饰"),
        new KeyValuePair<int, string>(KeycatCore.KC_PROP_COFFEE, "热咖啡"),
        new KeyValuePair<int, string>(KeycatCore.KC_PROP_ICED, "冰美式"),
        new KeyValuePair<int, string>(KeycatCore.KC_PROP_BRICK, "搬砖中"),
        new KeyValuePair<int, string>(KeycatCore.KC_PROP_MOON, "勿扰牌"),
    };

    internal static readonly KeyValuePair<int, string>[] FxLevelList = new KeyValuePair<int, string>[]
    {
        new KeyValuePair<int, string>(KeycatCore.KC_FX_VIBRANT, "高能解压"),
        new KeyValuePair<int, string>(KeycatCore.KC_FX_QUIET, "柔和不晃眼"),
    };

    private readonly Dictionary<Enum, Drawing.Bitmap> images = new Dictionary<Enum, Drawing.Bitmap>();
    private readonly Dictionary<int, Drawing.Bitmap> propImages = new Dictionary<int, Drawing.Bitmap>();
    private readonly Dictionary<int, Drawing.Bitmap> fxImages = new Dictionary<int, Drawing.Bitmap>();

    public Forms.ToolStripMenuItem Characters(CatStyle selected, Action<CatStyle> choose)
    {
        var parent = Group("角色样式", 29);
        foreach (var entry in CharacterList) Add(parent, entry.Value, entry.Key, selected, choose);
        return parent;
    }

    public Forms.ToolStripMenuItem Keyboards(KeyboardStyle selected, Action<KeyboardStyle> choose)
    {
        var parent = Group("键盘样式", 94);
        foreach (var entry in KeyboardList) Add(parent, entry.Value, entry.Key, selected, choose);
        return parent;
    }

    public Forms.ToolStripMenuItem Mice(MouseStyle selected, Action<MouseStyle> choose)
    {
        var parent = Group("鼠标样式", 29);
        foreach (var entry in MouseList) Add(parent, entry.Value, entry.Key, selected, choose);
        return parent;
    }

    public Forms.ToolStripMenuItem DeskProps(int selected, Action<int> choose)
    {
        var parent = new Forms.ToolStripMenuItem("桌面小物件");
        var list = (Forms.ToolStripDropDownMenu)parent.DropDown;
        list.ShowCheckMargin = true;
        list.ShowImageMargin = true;
        list.ImageScalingSize = new Drawing.Size(26, 26);
        foreach (var entry in DeskPropList)
        {
            int val = entry.Key;
            Drawing.Bitmap icon;
            if (!propImages.TryGetValue(val, out icon))
            {
                icon = PropThumbnail(val);
                propImages[val] = icon;
            }
            var item = new Forms.ToolStripMenuItem(entry.Value, icon)
            {
                Checked = val == selected,
                ImageScaling = Forms.ToolStripItemImageScaling.None
            };
            item.Click += delegate { choose(val); };
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    public Forms.ToolStripMenuItem FxLevels(int selected, Action<int> choose)
    {
        var parent = new Forms.ToolStripMenuItem("动效强度");
        var list = (Forms.ToolStripDropDownMenu)parent.DropDown;
        list.ShowCheckMargin = true;
        list.ShowImageMargin = true;
        list.ImageScalingSize = new Drawing.Size(22, 22);
        foreach (var entry in FxLevelList)
        {
            int val = entry.Key;
            Drawing.Bitmap icon;
            if (!fxImages.TryGetValue(val, out icon))
            {
                icon = FxThumbnail(val);
                fxImages[val] = icon;
            }
            var item = new Forms.ToolStripMenuItem(entry.Value, icon)
            {
                Checked = val == selected,
                ImageScaling = Forms.ToolStripItemImageScaling.None,
                Tag = val == KeycatCore.KC_FX_VIBRANT ? "默认" : "护眼"
            };
            item.Click += delegate { choose(val); };
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    internal static string GearSummary(KeyboardStyle kb, MouseStyle ms, int prop)
    {
        string gear = kb == KeyboardStyle.Apple && ms == MouseStyle.Apple ? "苹果妙控套装" : "定制键鼠";
        foreach (var p in DeskPropList)
            if (p.Key == prop && prop != KeycatCore.KC_PROP_NONE) return gear + " · " + p.Value;
        return gear;
    }

    internal Drawing.Bitmap GetCharacterMenuBitmap(CatStyle style)
    {
        Drawing.Bitmap bmp;
        Enum key = style;
        if (!images.TryGetValue(key, out bmp))
        {
            bmp = Thumbnail(key);
            images[key] = bmp;
        }
        return bmp;
    }

    internal Drawing.Bitmap GetProfileAvatar(CatStyle style)
    {
        var art = new DrawingGroup();
        using (DrawingContext dc = art.Open()) CatArt.DrawPortrait(dc, style);
        return RasterizeDrawing(art, 40, 40);
    }

    internal static string TitleOf(CatStyle style)
    {
        foreach (var entry in CharacterList) if (entry.Key == style) return entry.Value;
        return style.ToString();
    }

    internal static BitmapSource PortraitBitmap(CatStyle style, int size)
    {
        var art = new DrawingGroup();
        using (DrawingContext dc = art.Open()) CatArt.DrawPortrait(dc, style);
        Rect bounds = art.Bounds;
        if (bounds.IsEmpty) bounds = new Rect(-40, -40, 80, 80);
        double scale = Math.Min((size - 4) / bounds.Width, (size - 4) / bounds.Height);
        var drawing = new DrawingVisual();
        using (DrawingContext dc = drawing.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale,
                (size - scale * bounds.Width) / 2 - scale * bounds.X,
                (size - scale * bounds.Height) / 2 - scale * bounds.Y));
            dc.DrawDrawing(art);
            dc.Pop();
        }
        var render = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        render.Render(drawing);
        render.Freeze();
        return render;
    }

    private static Forms.ToolStripMenuItem Group(string title, int imageWidth)
    {
        var group = new Forms.ToolStripMenuItem(title);
        var list = (Forms.ToolStripDropDownMenu)group.DropDown;
        list.ShowCheckMargin = true;
        list.ShowImageMargin = true;
        list.ImageScalingSize = new Drawing.Size(imageWidth, 26);
        return group;
    }

    private void Add<T>(Forms.ToolStripMenuItem parent, string title, T value, T selected, Action<T> choose)
        where T : struct
    {
        Enum key = (Enum)(object)value;
        Drawing.Bitmap preview;
        if (!images.TryGetValue(key, out preview))
        {
            preview = Thumbnail(key);
            images.Add(key, preview);
        }
        var item = new Forms.ToolStripMenuItem(title, preview);
        item.Checked = value.Equals(selected);
        item.ImageScaling = Forms.ToolStripItemImageScaling.None;
        item.Click += delegate { choose(value); };
        parent.DropDownItems.Add(item);
    }

    private static Drawing.Bitmap Thumbnail(Enum style)
    {
        bool keyboard = style is KeyboardStyle;
        int width = keyboard ? 94 : 29;
        const int height = 26;
        var art = new DrawingGroup();
        using (DrawingContext dc = art.Open())
        {
            if (keyboard) DeskArt.DrawKeyboard(dc, (KeyboardStyle)(object)style, null);
            else if (style is CatStyle) CatArt.DrawPortrait(dc, (CatStyle)(object)style);
            else MouseArt.Draw(dc, (MouseStyle)(object)style, 0);
        }
        Rect bounds = art.Bounds;
        double scale = Math.Min((width - 2) / bounds.Width, (height - 2) / bounds.Height);
        var drawing = new DrawingVisual();
        using (DrawingContext dc = drawing.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale,
                (width - scale * bounds.Width) / 2 - scale * bounds.X,
                (height - scale * bounds.Height) / 2 - scale * bounds.Y));
            dc.DrawDrawing(art);
            dc.Pop();
        }
        var render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        render.Render(drawing);
        var image = new Drawing.Bitmap(width, height, Imaging.PixelFormat.Format32bppPArgb);
        var area = new Drawing.Rectangle(0, 0, width, height);
        var pixels = image.LockBits(area, Imaging.ImageLockMode.WriteOnly, image.PixelFormat);
        try { render.CopyPixels(Int32Rect.Empty, pixels.Scan0, height * pixels.Stride, pixels.Stride); }
        finally { image.UnlockBits(pixels); }
        return image;
    }

    private static Drawing.Bitmap PropThumbnail(int prop)
    {
        const int width = 26, height = 26;
        if (prop == KeycatCore.KC_PROP_NONE)
        {
            var empty = new Drawing.Bitmap(width, height, Imaging.PixelFormat.Format32bppPArgb);
            using (Drawing.Graphics g = Drawing.Graphics.FromImage(empty))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Drawing.Pen(Drawing.Color.FromArgb(170, 158, 146), 1.5f))
                {
                    g.DrawEllipse(pen, 6, 6, 14, 14);
                    g.DrawLine(pen, 8, 18, 18, 8);
                }
            }
            return empty;
        }
        var art = new DrawingGroup();
        using (DrawingContext dc = art.Open()) DeskPropArt.DrawProp(dc, prop, 1.2);
        return RasterizeDrawing(art, width, height);
    }

    private static Drawing.Bitmap FxThumbnail(int fx)
    {
        const int size = 22;
        var bmp = new Drawing.Bitmap(size, size, Imaging.PixelFormat.Format32bppPArgb);
        using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (fx == KeycatCore.KC_FX_VIBRANT)
            {
                var pts = new Drawing.PointF[] {
                    new Drawing.PointF(11, 3), new Drawing.PointF(13.2f, 8.8f),
                    new Drawing.PointF(19, 11), new Drawing.PointF(13.2f, 13.2f),
                    new Drawing.PointF(11, 19), new Drawing.PointF(8.8f, 13.2f),
                    new Drawing.PointF(3, 11), new Drawing.PointF(8.8f, 8.8f)
                };
                using (var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(245, 165, 58)))
                using (var edge = new Drawing.Pen(Drawing.Color.FromArgb(218, 96, 36), 1f))
                {
                    g.FillPolygon(fill, pts);
                    g.DrawPolygon(edge, pts);
                }
            }
            else
            {
                using (var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(132, 178, 162)))
                using (var pen = new Drawing.Pen(Drawing.Color.FromArgb(95, 144, 128), 1.5f))
                {
                    g.FillEllipse(fill, 5, 5, 12, 12);
                    g.DrawEllipse(pen, 5, 5, 12, 12);
                }
            }
        }
        return bmp;
    }

    private static Drawing.Bitmap RasterizeDrawing(DrawingGroup art, int width, int height)
    {
        Rect bounds = art.Bounds;
        if (bounds.IsEmpty) bounds = new Rect(0, 0, width, height);
        double scale = Math.Min((width - 3) / bounds.Width, (height - 3) / bounds.Height);
        var drawing = new DrawingVisual();
        using (DrawingContext dc = drawing.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale,
                (width - scale * bounds.Width) / 2 - scale * bounds.X,
                (height - scale * bounds.Height) / 2 - scale * bounds.Y));
            dc.DrawDrawing(art);
            dc.Pop();
        }
        var render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        render.Render(drawing);
        var image = new Drawing.Bitmap(width, height, Imaging.PixelFormat.Format32bppPArgb);
        var area = new Drawing.Rectangle(0, 0, width, height);
        var pixels = image.LockBits(area, Imaging.ImageLockMode.WriteOnly, image.PixelFormat);
        try { render.CopyPixels(Int32Rect.Empty, pixels.Scan0, height * pixels.Stride, pixels.Stride); }
        finally { image.UnlockBits(pixels); }
        return image;
    }

    public void Dispose()
    {
        foreach (Drawing.Bitmap image in images.Values) image.Dispose();
        images.Clear();
        foreach (Drawing.Bitmap image in propImages.Values) image.Dispose();
        propImages.Clear();
        foreach (Drawing.Bitmap image in fxImages.Values) image.Dispose();
        fxImages.Clear();
    }
}
