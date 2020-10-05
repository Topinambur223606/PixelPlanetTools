using System;

namespace PixelPlanetBot
{
    [Flags]
    enum PlacingOrderMode
    {
        Random =        0b10_0000_0000,
        Outline =       0b01_0000_0000,

        Left =          0b00_1000_0000,
        Right =         0b00_0100_0000,
        Top =           0b00_0010_0000,
        Bottom =        0b00_0001_0000,

        ThenLeft =      0b00_0000_1000,
        ThenRight =     0b00_0000_0100,
        ThenTop =       0b00_0000_0010,
        ThenBottom =    0b00_0000_0001,

        LeftTop = Left | ThenTop,
        LeftBottom = Left | ThenBottom,
        RightTop = Right | ThenTop,
        RightBottom = Right | ThenBottom,
        TopLeft = Top | ThenLeft,
        TopRight = Top | ThenRight,
        BottomLeft = Bottom | ThenLeft,
        BottomRight = Bottom | ThenRight
    }
}
