using System;

namespace PixelPlanetBot
{
    [Flags]
    enum PlacingOrderMode
    {
        #region self-sufficient modes

        Random = 0b10__0_0000_000__0000_000,
        Outline = 0b01__0_0000_000__0000_000,

        #endregion

        #region "first by" modes

        Mask = 0b00__1_0000_000__0000_000,

        Left = 0b00__0_1000_000__0000_000,
        Right = 0b00__0_0100_000__0000_000,
        Top = 0b00__0_0010_000__0000_000,
        Bottom = 0b00__0_0001_000__0000_000,

        Color = 0b00__0_0000_100__0000_000,
        ColorDesc = 0b00__0_0000_010__0000_000,
        ColorRnd = 0b00__0_0000_001__0000_000,

        #endregion

        #region "then by" modes

        ThenLeft = 0b00__0_0000_000__1000_000,
        ThenRight = 0b00__0_0000_000__0100_000,
        ThenTop = 0b00__0_0000_000__0010_000,
        ThenBottom = 0b00__0_0000_000__0001_000,

        ThenColor = 0b00__0_0000_000__0000_100,
        ThenColorDesc = 0b00__0_0000_000__0000_010,
        ThenColorRnd = 0b00__0_0000_000__0000_001,

        #endregion

        #region "first"-"then" modes combined

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

        ColorDescTop = ColorDesc | ThenTop,
        ColorDescBottom = ColorDesc | ThenBottom,
        ColorDescLeft = ColorDesc | ThenLeft,
        ColorDescRight = ColorDesc | ThenRight,

        ColorRndTop = ColorRnd | ThenTop,
        ColorRndBottom = ColorRnd | ThenBottom,
        ColorRndLeft = ColorRnd | ThenLeft,
        ColorRndRight = ColorRnd | ThenRight,

        MaskTop = Mask | ThenTop,
        MaskBottom = Mask | ThenBottom,
        MaskLeft = Mask | ThenLeft,
        MaskRight = Mask | ThenRight,
        MaskColor = Mask | ThenColor,
        MaskColorDesc = Mask | ThenColorDesc,
        MaskColorRnd = Mask | ThenColorRnd

        #endregion
    }
}
