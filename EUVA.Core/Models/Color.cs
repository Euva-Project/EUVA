// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Models;

public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static Color FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);
    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(r, g, b, a);
}

public static class Colors
{
    public static Color DarkSlateBlue => Color.FromRgb(72, 61, 139);
    public static Color DarkBlue => Color.FromRgb(0, 0, 139);
    public static Color LightGreen => Color.FromRgb(144, 238, 144);
    public static Color LightBlue => Color.FromRgb(173, 216, 230);
    public static Color LightGray => Color.FromRgb(211, 211, 211);
    public static Color LightYellow => Color.FromRgb(255, 255, 224);
    public static Color OrangeRed => Color.FromRgb(255, 69, 0);
}
