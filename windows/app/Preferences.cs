using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

internal sealed class Preferences
{
    public const int MinPercent = KeycatCore.KC_MIN_SCALE_PERCENT, MaxPercent = KeycatCore.KC_MAX_SCALE_PERCENT;
    public double X = double.NaN, Y = double.NaN, Scale = 1.0;
    public bool Labels = true, ClickThrough = false, OnTop = true, AutoCheckUpdates = true;
    public bool AutoHideFullscreen = true, PrivacyMode = false, BreakReminder = true, HasCompletedGuide = false;
    public int FxLevel = KeycatCore.KC_FX_VIBRANT, DeskProp = KeycatCore.KC_PROP_NONE;
    public string StatsDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public int TodayKeyCount = 0, TodayBurstCount = 0;
    public long LastUpdateCheckUtcTicks = 0;
    public CatStyle Cat = (CatStyle)KeycatCore.KC_DEFAULT_CAT;
    public KeyboardStyle Keyboard = (KeyboardStyle)KeycatCore.KC_DEFAULT_KEYBOARD;
    public MouseStyle Mouse = (MouseStyle)KeycatCore.KC_DEFAULT_MOUSE;

    internal static string DefaultFileName
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "KeycatDesktop", "preferences.xml"); }
    }

    public static Preferences Load() { return Load(DefaultFileName); }

    public static Preferences Load(string fileName)
    {
        Preferences p = new Preferences();
        try
        {
            if (File.Exists(fileName))
            {
                XElement root = XElement.Load(fileName);
                p.X = (double?)root.Attribute("x") ?? double.NaN;
                p.Y = (double?)root.Attribute("y") ?? double.NaN;
                double scale;
                if (double.TryParse((string)root.Attribute("scale"), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out scale) && Finite(scale))
                    p.Scale = Math.Max(MinPercent / 100.0, Math.Min(MaxPercent / 100.0, scale));
                p.Labels = (bool?)root.Attribute("labels") ?? true;
                p.ClickThrough = (bool?)root.Attribute("clickThrough") ?? false;
                p.OnTop = (bool?)root.Attribute("onTop") ?? true;
                p.AutoCheckUpdates = (bool?)root.Attribute("autoCheckUpdates") ?? true;
                p.AutoHideFullscreen = (bool?)root.Attribute("autoHideFullscreen") ?? true;
                p.PrivacyMode = (bool?)root.Attribute("privacyMode") ?? false;
                p.BreakReminder = ReadBool(root, "breakReminder", true);
                p.HasCompletedGuide = ReadBool(root, "guideDone", false);
                int fxVal;
                if (int.TryParse((string)root.Attribute("fxLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out fxVal))
                    p.FxLevel = fxVal == KeycatCore.KC_FX_QUIET ? KeycatCore.KC_FX_QUIET : KeycatCore.KC_FX_VIBRANT;
                int propVal;
                if (int.TryParse((string)root.Attribute("deskProp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out propVal))
                    p.DeskProp = Math.Max(0, Math.Min(4, propVal));
                string sDate = (string)root.Attribute("statsDate");
                if (!String.IsNullOrEmpty(sDate)) p.StatsDate = sDate;
                int tk;
                if (int.TryParse((string)root.Attribute("todayKeys"), NumberStyles.Integer, CultureInfo.InvariantCulture, out tk) && tk >= 0)
                    p.TodayKeyCount = tk;
                int tb;
                if (int.TryParse((string)root.Attribute("todayBursts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out tb) && tb >= 0)
                    p.TodayBurstCount = tb;
                long lastCheck;
                if (long.TryParse((string)root.Attribute("lastUpdateCheckUtc"), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out lastCheck) && lastCheck >= 0)
                    p.LastUpdateCheckUtcTicks = lastCheck;
                p.Cat = ReadStyle(root, "catStyle", p.Cat);
                p.Keyboard = ReadStyle(root, "keyboard", p.Keyboard);
                p.Mouse = ReadStyle(root, "mouse", p.Mouse);
            }
        }
        catch (Exception) { return new Preferences(); }
        p.EnsureToday(DateTime.Now);
        return p;
    }

    public bool EnsureToday(DateTime now)
    {
        string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (String.Equals(StatsDate, today, StringComparison.Ordinal)) return false;
        StatsDate = today;
        TodayKeyCount = 0;
        TodayBurstCount = 0;
        return true;
    }

    public static bool Finite(double number) { return !double.IsInfinity(number) && !double.IsNaN(number); }

    private static bool ReadBool(XElement settings, string attribute, bool fallback)
    {
        bool value;
        string raw = (string)settings.Attribute(attribute);
        return !String.IsNullOrEmpty(raw) && bool.TryParse(raw, out value) ? value : fallback;
    }

    private static T ReadStyle<T>(XElement settings, string attribute, T fallback) where T : struct
    {
        T value;
        return Enum.TryParse((string)settings.Attribute(attribute), true, out value) && Enum.IsDefined(typeof(T), value)
            ? value : fallback;
    }

    public void Save() { Save(DefaultFileName); }

    public void Save(string fileName)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            var settings = new XElement("cat", new XAttribute("x", X), new XAttribute("y", Y),
                new XAttribute("scale", Scale), new XAttribute("labels", Labels),
                new XAttribute("clickThrough", ClickThrough), new XAttribute("onTop", OnTop),
                new XAttribute("autoCheckUpdates", AutoCheckUpdates),
                new XAttribute("autoHideFullscreen", AutoHideFullscreen),
                new XAttribute("privacyMode", PrivacyMode),
                new XAttribute("breakReminder", BreakReminder),
                new XAttribute("guideDone", HasCompletedGuide),
                new XAttribute("fxLevel", FxLevel),
                new XAttribute("deskProp", DeskProp),
                new XAttribute("statsDate", StatsDate ?? String.Empty),
                new XAttribute("todayKeys", TodayKeyCount),
                new XAttribute("todayBursts", TodayBurstCount),
                new XAttribute("lastUpdateCheckUtc", LastUpdateCheckUtcTicks),
                new XAttribute("catStyle", Cat),
                new XAttribute("keyboard", Keyboard == KeyboardStyle.Apple ? "apple" : Keyboard.ToString()),
                new XAttribute("mouse", Mouse == MouseStyle.Apple ? "apple" : Mouse.ToString()));
            string tmp = fileName + ".tmp";
            settings.Save(tmp);
            if (File.Exists(fileName)) File.Replace(tmp, fileName, null);
            else File.Move(tmp, fileName);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
