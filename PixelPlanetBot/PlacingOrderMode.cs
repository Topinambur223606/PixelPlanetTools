using System;

namespace PixelPlanetBot
{
    [Flags]
    enum PlacingOrderMode
    {
        Random =        0b10_0000_000_0000,
        Outline =       0b01_0000_000_0000,

        Left =          0b00_1000_000_0000,
        Right =         0b00_0100_000_0000,
        Top =           0b00_0010_000_0000,
        Bottom =        0b00_0001_000_0000,
        
        Color =         0b00_0000_100_0000,
        ColorDsc =      0b00_0000_010_0000,
        ColorRnd =      0b00_0000_001_0000,

        ThenLeft =      0b00_0000_000_1000,
        ThenRight =     0b00_0000_000_0100,
        ThenTop =       0b00_0000_000_0010,
        ThenBottom =    0b00_0000_000_0001,

        LeftTop = Left | ThenTop,
        LeftBottom = Left | ThenBottom,

        RightTop = Right | ThenTop,
        RightBottom = Right | ThenBottom,

        TopLeft = Top | ThenLeft,
        TopRight = Top | ThenRight,

        BottomLeft = Bottom | ThenLeft,
        BottomRight = Bottom | ThenRight,

        ColorTop = Color | ThenTop,
        ColorBottom = Color | ThenBottom,
        ColorLeft = Color | ThenLeft,
        ColorRight = Color | ThenRight,

        ColorDscTop = ColorDsc | ThenTop,
        ColorDscBottom = ColorDsc | ThenBottom,
        ColorDscLeft = ColorDsc | ThenLeft,
        ColorDscRight = ColorDsc | ThenRight,

        ColorRndTop = ColorRnd | ThenTop,
        ColorRndBottom = ColorRnd | ThenBottom,
        ColorRndLeft = ColorRnd | ThenLeft,
        ColorRndRight = ColorRnd | ThenRight
    }
}
