using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class ScaleTrackBar : TrackBar
{
    public ScaleTrackBar(double scale)
    {
        Minimum = Preferences.MinPercent * 10;
        Maximum = Preferences.MaxPercent * 10;
        SmallChange = 10;
        LargeChange = 100;
        Value = (int)Math.Round(scale * 1000, MidpointRounding.AwayFromZero);
        AutoSize = false;
        Size = new Size(250, 31);
        TickStyle = TickStyle.None;
        AccessibleName = "小猫大小（百分比）";
    }

    public string PercentageText { get { return (Value / 10.0).ToString("0.#") + "%"; } }

    protected override bool IsInputKey(Keys keyData)
    {
        Keys code = keyData & Keys.KeyCode;
        return code == Keys.Left || code == Keys.Right || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Shift && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
        {
            int step = e.KeyCode == Keys.Left ? -1 : 1;
            Value = Math.Max(Minimum, Math.Min(Maximum, Value + step));
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }
}
