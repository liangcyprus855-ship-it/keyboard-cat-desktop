using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

internal sealed class KeycatMenuColorTable : ProfessionalColorTable
{
    private static readonly Color BgCream = Color.FromArgb(252, 249, 245);
    private static readonly Color BorderWarm = Color.FromArgb(224, 214, 200);
    private static readonly Color SepWarm = Color.FromArgb(234, 226, 215);

    public override Color ToolStripDropDownBackground { get { return BgCream; } }
    public override Color MenuBorder { get { return BorderWarm; } }
    public override Color MenuItemBorder { get { return Color.Transparent; } }
    public override Color MenuItemSelected { get { return Color.Transparent; } }
    public override Color MenuItemSelectedGradientBegin { get { return Color.Transparent; } }
    public override Color MenuItemSelectedGradientEnd { get { return Color.Transparent; } }
    public override Color ImageMarginGradientBegin { get { return BgCream; } }
    public override Color ImageMarginGradientMiddle { get { return BgCream; } }
    public override Color ImageMarginGradientEnd { get { return BgCream; } }
    public override Color SeparatorDark { get { return SepWarm; } }
    public override Color SeparatorLight { get { return Color.Transparent; } }
}

internal sealed class ProfileHeaderMenuItem : ToolStripMenuItem
{
    private readonly Bitmap avatar;
    private readonly string roleTitle, subtitle, statsLine;

    public ProfileHeaderMenuItem(Bitmap avatarImage, string title, string sub, string stats, Action openWardrobe)
    {
        avatar = avatarImage;
        roleTitle = title ?? "键盘小黑猫";
        subtitle = sub ?? "";
        statsLine = stats ?? "";
        AutoSize = false;
        Size = new Size(292, 68);
        if (openWardrobe != null)
            Click += delegate { openWardrobe(); };
    }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        return new Size(292, 68);
    }

    internal Bitmap AvatarBitmap { get { return avatar; } }
    internal string RoleTitleText { get { return roleTitle; } }
    internal string SubtitleText { get { return subtitle; } }
    internal string StatsLineText { get { return statsLine; } }
}

internal sealed class KeycatMenuItem : ToolStripMenuItem
{
    private const int FixedWidth = 292;
    private const int FixedHeight = 29;

    public KeycatMenuItem(string text) : base(text) { }
    public KeycatMenuItem(string text, Image image) : base(text, image) { }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        Size baseSize = base.GetPreferredSize(constrainingSize);
        return IsOnDropDown && !(Owner is ToolStripDropDownMenu && OwnerItem != null)
            ? new Size(Math.Max(FixedWidth, baseSize.Width + 24), Math.Max(FixedHeight, baseSize.Height))
            : new Size(Math.Max(FixedWidth, baseSize.Width + 24), FixedHeight);
    }
}

internal sealed class KeycatMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color BgCream = Color.FromArgb(252, 249, 245);
    private static readonly Color CardBg = Color.FromArgb(255, 255, 255);
    private static readonly Color CardBgHover = Color.FromArgb(255, 246, 238);
    private static readonly Color CardBorder = Color.FromArgb(226, 216, 204);
    private static readonly Color CardBorderHover = Color.FromArgb(240, 178, 128);
    private static readonly Color AvatarRing = Color.FromArgb(255, 243, 231);
    private static readonly Color BorderWarm = Color.FromArgb(223, 213, 200);
    private static readonly Color HoverFill = Color.FromArgb(255, 240, 227);
    private static readonly Color HoverBorder = Color.FromArgb(242, 187, 142);
    private static readonly Color Accent = Color.FromArgb(232, 122, 61);
    private static readonly Color TextMain = Color.FromArgb(47, 41, 37);
    private static readonly Color TextMuted = Color.FromArgb(130, 118, 108);
    private static readonly Color BadgeBg = Color.FromArgb(242, 236, 228);
    private static readonly Color BadgeBorder = Color.FromArgb(224, 214, 201);
    private static readonly Color SepLine = Color.FromArgb(234, 226, 215);

    public KeycatMenuRenderer() : base(new KeycatMenuColorTable())
    {
        RoundedEdges = true;
    }

    internal static void ApplyTo(ToolStripDropDown dropDown)
    {
        if (dropDown == null) return;
        dropDown.Renderer = new KeycatMenuRenderer();
        dropDown.BackColor = BgCream;
        dropDown.Font = new Font("Microsoft YaHei UI", 9.25f);
        var menu = dropDown as ToolStripDropDownMenu;
        if (menu != null)
        {
            bool anyImage = false;
            foreach (ToolStripItem item in dropDown.Items)
            {
                if (!(item is ProfileHeaderMenuItem) && item.Image != null)
                {
                    anyImage = true;
                    break;
                }
            }
            menu.ShowCheckMargin = true;
            menu.ShowImageMargin = anyImage;
        }
        foreach (ToolStripItem item in dropDown.Items)
        {
            var mi = item as ToolStripMenuItem;
            if (mi != null && mi.HasDropDownItems)
                ApplyTo(mi.DropDown);
        }
    }

    internal static void NormalizeRootWidths(ContextMenuStrip menu, int width)
    {
        if (menu == null) return;
        menu.MinimumSize = new Size(width + 4, 0);
        foreach (ToolStripItem item in menu.Items)
        {
            var hdr = item as ProfileHeaderMenuItem;
            if (hdr != null)
            {
                hdr.AutoSize = false;
                hdr.Size = new Size(width, 68);
                continue;
            }
            var mi = item as ToolStripMenuItem;
            if (mi == null) continue;
            mi.AutoSize = false;
            mi.Size = new Size(width, 29);
        }
    }

    internal static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using (var b = new SolidBrush(BgCream))
            e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        Rectangle r = e.AffectedBounds;
        r.Width -= 1; r.Height -= 1;
        using (var p = new Pen(BorderWarm, 1f))
            e.Graphics.DrawRectangle(p, r);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var hdr = e.Item as ProfileHeaderMenuItem;
        if (hdr != null)
        {
            PaintProfileHeader(e.Graphics, hdr);
            return;
        }
        if (!e.Item.Enabled || !(e.Item is ToolStripMenuItem)) return;
        if (!e.Item.Selected && !e.Item.Pressed) return;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(6, 2, e.Item.Bounds.Width - 12, e.Item.Bounds.Height - 4);
        using (GraphicsPath path = RoundedRect(rect, 7f))
        using (var fill = new SolidBrush(HoverFill))
        using (var border = new Pen(HoverBorder, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
    }

    private static void PaintProfileHeader(Graphics g, ProfileHeaderMenuItem hdr)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        bool hovered = hdr.Selected || hdr.Pressed;
        var card = new RectangleF(6, 4, Math.Max(200f, hdr.Width - 12f), hdr.Height - 8);
        using (GraphicsPath path = RoundedRect(card, 10f))
        using (var fill = new SolidBrush(hovered ? CardBgHover : CardBg))
        using (var pen = new Pen(hovered ? CardBorderHover : CardBorder, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        var avBox = new RectangleF(14, 13, 42, 42);
        using (GraphicsPath ap = RoundedRect(avBox, 10f))
        using (var fill = new SolidBrush(AvatarRing))
        using (var pen = new Pen(Color.FromArgb(245, 206, 173), 1f))
        {
            g.FillPath(fill, ap);
            g.DrawPath(pen, ap);
        }
        if (hdr.AvatarBitmap != null)
            g.DrawImage(hdr.AvatarBitmap, new RectangleF(18, 17, 34, 34));

        using (var titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
        using (var subFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
        using (var pillFont = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Bold))
        using (var darkBrush = new SolidBrush(TextMain))
        using (var mutedBrush = new SolidBrush(TextMuted))
        {
            g.DrawString(hdr.RoleTitleText, titleFont, darkBrush, 63f, 11f);
            g.DrawString(hdr.SubtitleText, subFont, mutedBrush, 63f, 29f);
            using (var accentBrush = new SolidBrush(Color.FromArgb(204, 98, 42)))
                g.DrawString(hdr.StatsLineText, subFont, accentBrush, 63f, 44f);

            var pill = new RectangleF(card.Right - 66f, 21, 56, 26);
            using (GraphicsPath pp = RoundedRect(pill, 12f))
            using (var pFill = new SolidBrush(hovered ? Accent : Color.FromArgb(255, 241, 230)))
            using (var pBorder = new Pen(hovered ? Accent : Color.FromArgb(242, 187, 142), 1f))
            {
                g.FillPath(pFill, pp);
                g.DrawPath(pBorder, pp);
            }
            using (var pInk = new SolidBrush(hovered ? Color.White : Color.FromArgb(210, 99, 41)))
            {
                string cta = "衣橱 ›";
                SizeF sz = g.MeasureString(cta, pillFont);
                g.DrawString(cta, pillFont, pInk, pill.X + (pill.Width - sz.Width) / 2f + 1f, pill.Y + (pill.Height - sz.Height) / 2f);
            }
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is ProfileHeaderMenuItem) return;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle r = e.ImageRectangle;
        var box = new RectangleF(r.X + 2, r.Y + (r.Height - 16) / 2f, 16, 16);
        using (GraphicsPath path = RoundedRect(box, 5f))
        using (var fill = new SolidBrush(Accent))
            g.FillPath(fill, path);
        using (var pen = new Pen(Color.White, 1.8f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, new[] {
                new PointF(box.X + 4.2f, box.Y + 8.2f),
                new PointF(box.X + 6.9f, box.Y + 11.0f),
                new PointF(box.X + 11.8f, box.Y + 5.2f)
            });
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ProfileHeaderMenuItem) return;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        bool disabledSection = !e.Item.Enabled;
        e.TextColor = disabledSection ? TextMuted : TextMain;
        if (disabledSection)
            e.TextFont = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Bold);
        base.OnRenderItemText(e);

        string badge = e.Item.Tag as string;
        if (!String.IsNullOrEmpty(badge))
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            SizeF actualText = g.MeasureString(e.Text, e.TextFont);
            float textEnd = e.TextRectangle.X + actualText.Width;
            using (var badgeFont = new Font("Microsoft YaHei UI", 7.75f, FontStyle.Regular))
            {
                SizeF sz = g.MeasureString(badge, badgeFont);
                float pw = sz.Width + 10f;
                float ph = 18f;
                bool hasDrop = e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).HasDropDownItems;
                float rightPad = hasDrop ? 28f : 12f;
                var pill = new RectangleF(e.Item.Bounds.Width - pw - rightPad, (e.Item.Bounds.Height - ph) / 2f, pw, ph);
                if (pill.X > textEnd + 6f)
                {
                    using (GraphicsPath path = RoundedRect(pill, 5f))
                    using (var fill = new SolidBrush(BadgeBg))
                    using (var border = new Pen(BadgeBorder, 1f))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(border, path);
                    }
                    using (var ink = new SolidBrush(TextMuted))
                        g.DrawString(badge, badgeFont, ink, pill.X + 5f, pill.Y + (ph - sz.Height) / 2f + 0.5f);
                }
            }
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using (var pen = new Pen(SepLine, 1f))
            e.Graphics.DrawLine(pen, 14, y, e.Item.Width - 14, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is ProfileHeaderMenuItem) return;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = e.Item.Bounds.Width - 16f;
        float cy = e.Item.Bounds.Height / 2f;
        Color col = e.Item.Selected ? Accent : TextMuted;
        using (var pen = new Pen(col, 1.6f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, new[] {
                new PointF(cx - 2.2f, cy - 3.6f),
                new PointF(cx + 1.8f, cy),
                new PointF(cx - 2.2f, cy + 3.6f)
            });
        }
    }
}

internal sealed class SleekScaleControl : Control
{
    private readonly string title;
    private readonly int[] presets = new[] { 75, 100, 125, 150 };
    private int val;
    private bool dragging;

    private const int MinVal = Preferences.MinPercent * 10;
    private const int MaxVal = Preferences.MaxPercent * 10;

    public event EventHandler ScaleChanged;

    public SleekScaleControl(double scale, string caption)
    {
        title = caption;
        val = Math.Max(MinVal, Math.Min(MaxVal, (int)Math.Round(scale * 1000, MidpointRounding.AwayFromZero)));
        Size = new Size(268, 84);
        BackColor = Color.FromArgb(252, 249, 245);
        AccessibleName = caption + "（百分比）";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
    }

    public int Value
    {
        get { return val; }
        set
        {
            int next = Math.Max(MinVal, Math.Min(MaxVal, value));
            if (next == val) return;
            val = next;
            Invalidate();
            if (ScaleChanged != null) ScaleChanged(this, EventArgs.Empty);
        }
    }

    public string PercentageText { get { return (val / 10.0).ToString("0.#") + "%"; } }

    private RectangleF TrackRect { get { return new RectangleF(16, 36, Width - 32, 6); } }

    private RectangleF PresetRect(int index)
    {
        float gap = 6f;
        float w = (Width - 32f - gap * 3f) / 4f;
        return new RectangleF(16f + index * (w + gap), 54f, w, 22f);
    }

    private void UpdateFromX(int x)
    {
        RectangleF tr = TrackRect;
        double ratio = Math.Max(0.0, Math.Min(1.0, (x - tr.X) / tr.Width));
        Value = (int)Math.Round(MinVal + ratio * (MaxVal - MinVal), MidpointRounding.AwayFromZero);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        Keys c = keyData & Keys.KeyCode;
        return c == Keys.Left || c == Keys.Right || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
        {
            int step = e.Shift ? 1 : 50;
            Value += e.KeyCode == Keys.Left ? -step : step;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        for (int i = 0; i < presets.Length; i++)
        {
            if (PresetRect(i).Contains(e.Location))
            {
                Value = presets[i] * 10;
                return;
            }
        }
        if (e.Y >= 24 && e.Y <= 52)
        {
            dragging = true;
            UpdateFromX(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging) UpdateFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        using (var fTitle = new Font("Microsoft YaHei UI", 8.75f, FontStyle.Bold))
        using (var fPct = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
        using (var dark = new SolidBrush(Color.FromArgb(47, 41, 37)))
        {
            g.DrawString(title, fTitle, dark, 14f, 8f);
            string pct = PercentageText;
            SizeF psz = g.MeasureString(pct, fPct);
            var badge = new RectangleF(Width - psz.Width - 26f, 6f, psz.Width + 12f, 20f);
            using (GraphicsPath bp = KeycatMenuRenderer.RoundedRect(badge, 6f))
            using (var bf = new SolidBrush(Color.FromArgb(255, 240, 227)))
            using (var bb = new Pen(Color.FromArgb(242, 187, 142), 1f))
            {
                g.FillPath(bf, bp);
                g.DrawPath(bb, bp);
            }
            using (var orange = new SolidBrush(Color.FromArgb(210, 99, 41)))
                g.DrawString(pct, fPct, orange, badge.X + 6f, badge.Y + (badge.Height - psz.Height) / 2f);
        }

        RectangleF tr = TrackRect;
        float ratio = (float)(val - MinVal) / (MaxVal - MinVal);
        float tx = tr.X + ratio * tr.Width;
        using (GraphicsPath tp = KeycatMenuRenderer.RoundedRect(tr, 3f))
        using (var tb = new SolidBrush(Color.FromArgb(230, 221, 209)))
            g.FillPath(tb, tp);
        if (tx > tr.X + 2f)
        {
            var active = new RectangleF(tr.X, tr.Y, tx - tr.X, tr.Height);
            using (GraphicsPath ap = KeycatMenuRenderer.RoundedRect(active, 3f))
            using (var ab = new SolidBrush(Color.FromArgb(232, 122, 61)))
                g.FillPath(ab, ap);
        }

        var thumb = new RectangleF(tx - 7.5f, tr.Y + tr.Height / 2f - 7.5f, 15f, 15f);
        using (var tf = new SolidBrush(Color.White))
        using (var tb = new Pen(Color.FromArgb(210, 198, 184), 1.2f))
        using (var dot = new SolidBrush(Color.FromArgb(232, 122, 61)))
        {
            g.FillEllipse(tf, thumb);
            g.DrawEllipse(tb, thumb);
            g.FillEllipse(dot, thumb.X + 4.5f, thumb.Y + 4.5f, 6f, 6f);
        }

        using (var fPreset = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
        {
            for (int i = 0; i < presets.Length; i++)
            {
                RectangleF pr = PresetRect(i);
                bool activePreset = Math.Abs(val - presets[i] * 10) <= 5;
                using (GraphicsPath pp = KeycatMenuRenderer.RoundedRect(pr, 6f))
                using (var pf = new SolidBrush(activePreset ? Color.FromArgb(232, 122, 61) : Color.FromArgb(245, 240, 234)))
                using (var pb = new Pen(activePreset ? Color.FromArgb(211, 103, 45) : Color.FromArgb(224, 214, 200), 1f))
                {
                    g.FillPath(pf, pp);
                    g.DrawPath(pb, pp);
                }
                string lbl = presets[i] + "%";
                SizeF sz = g.MeasureString(lbl, fPreset);
                using (var ink = new SolidBrush(activePreset ? Color.White : Color.FromArgb(95, 86, 78)))
                    g.DrawString(lbl, fPreset, ink, pr.X + (pr.Width - sz.Width) / 2f, pr.Y + (pr.Height - sz.Height) / 2f);
            }
        }
    }
}
