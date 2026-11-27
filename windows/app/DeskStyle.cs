public enum CatStyle
{
    Black, Ginger, JapaneseBobtail, PhotoTabby,
    OfficeColleague, Hairclip, Ribbon, Headband, Glasses, Hoodie, Beret, Cap, Boss, Shiba,
    Dachshund, Corgi, BlackShiba, Bichon
}

internal static class CharacterStyles
{
    public static bool IsPerson(this CatStyle style) { return KeycatCore.kc_is_person((int)style) != 0; }
}
public enum KeyboardStyle { Classic, Round, Neon, Wood, Apple }
public enum MouseStyle { Classic, Pebble, Gaming, Trackball, Apple }

internal static class DeskLayout
{
    // Forward on the desk is down-screen, leaving space between the cat's feet and the keys.
    public const double Forward = KeycatCore.KC_DESK_FORWARD;
}
