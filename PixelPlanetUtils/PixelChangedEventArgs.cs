using System;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils
{
    public class PixelChangedEventArgs : EventArgs
    {
        public DateTime DateTime { get; set; }

        public XY Chunk { get; set; }

        public XY Pixel { get; set; }

        public PixelColor Color { get; set; }
    }


}
