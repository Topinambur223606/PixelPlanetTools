using System;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetBot
{
    class PixelChangedEventArgs : EventArgs
    {
        public XY Chunk { get; set; }

        public XY Pixel { get; set; }

        public PixelColor Color { get; set; }
    }


}
