public enum CatStyle { Black, Ginger, JapaneseBobtail, PhotoTabby, OfficeColleague, Hairclip, Ribbon, Headband, Glasses }

internal static class CharacterStyles
{
    public static bool IsPerson(this CatStyle style) { return style >= CatStyle.OfficeColleague && style <= CatStyle.Glasses; }
}
public enum KeyboardStyle { Classic, Round, Neon, Wood }
public enum MouseStyle { Classic, Pebble, Gaming, Trackball }

internal static class DeskLayout
{
    // Forward on the desk is down-screen, leaving space between the cat's feet and the keys.
    public const double Forward = 14;
}
