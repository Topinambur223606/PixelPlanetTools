using System;

namespace PixelPlanetBot.Options.Enums
{
    [Flags]
    enum PlacingOrderMode3D
    {
        #region self-sufficient modes

        Random =        0b10__000000_000__000000_000,
        Outline =       0b01__000000_000__000000_000,

        #endregion

        #region "first by" modes

        AscX =          0b00__100000_000__000000_000,
        DescX =         0b00__010000_000__000000_000,
        AscY =          0b00__001000_000__000000_000,
        DescY =         0b00__000100_000__000000_000,
        Top =           0b00__000010_000__000000_000,
        Bottom =        0b00__000001_000__000000_000,
        DescZ = Top,
        AscZ = Bottom,

        Color =         0b00__000000_100__000000_000,
        ColorDesc =     0b00__000000_010__000000_000,
        ColorRnd =      0b00__000000_001__000000_000,

        #endregion

        #region "then by" modes

        ThenAscX =      0b00__000000_000__100000_000,
        ThenDescX =     0b00__000000_000__010000_000,
        ThenAscY =      0b00__000000_000__001000_000,
        ThenDescY =     0b00__000000_000__000100_000,
        ThenTop =       0b00__000000_000__000010_000,
        ThenBottom =    0b00__000000_000__000001_000,
        ThenDescZ = ThenTop,
        ThenAscZ = ThenBottom,

        ThenColor =     0b00__000000_000__000000_100,
        ThenColorDesc = 0b00__000000_000__000000_010,
        ThenColorRnd =  0b00__000000_000__000000_001,

        #endregion

        #region "first"-"then" modes combined

        AscXTop = AscX | ThenTop,
        AscXBottom = AscX | ThenBottom,
        DescXTop = DescX | ThenTop,
        DescXBottom = DescX | ThenBottom,

        AscYTop = AscY | ThenTop,
        AscYBottom = AscY | ThenBottom,
        DescYTop = DescY | ThenTop,
        DescYBottom = DescY | ThenBottom,

        TopAscX = Top | ThenAscX,
        TopDescX = Top | ThenDescX,
        TopAscY = Top | ThenAscY,
        TopDescY = Top | ThenDescY,

        BottomAscX = Bottom | ThenAscX,
        BottomDescX = Bottom | ThenDescX,
        BottomAscY = Bottom | ThenAscY,
        BottomDescY = Bottom | ThenDescY,

        ColorAscX = Color | ThenAscX,
        ColorDescX = Color | ThenDescX,
        ColorAscY = Color | ThenAscY,
        ColorDescY = Color | ThenDescY,
        ColorTop = Color | ThenTop,
        ColorBottom = Color | ThenBottom,

        ColorDescAscX = ColorDesc | ThenAscX,
        ColorDescDescX = ColorDesc | ThenDescX,
        ColorDescAscY = ColorDesc | ThenAscY,
        ColorDescDescY = ColorDesc | ThenDescY,
        ColorDescTop = ColorDesc | ThenTop,
        ColorDescBottom = ColorDesc | ThenBottom,

        ColorRndAscX = ColorRnd | ThenAscX,
        ColorRndDescX = ColorRnd | ThenDescX,
        ColorRndAscY = ColorRnd | ThenAscY,
        ColorRndDescY = ColorRnd | ThenDescY,
        ColorRndTop = ColorRnd | ThenTop,
        ColorRndBottom = ColorRnd | ThenBottom

        #endregion
    }
}
