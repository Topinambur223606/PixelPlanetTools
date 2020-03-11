using PixelPlanetUtils.Canvas;
using System;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.Eventing
{
    public class PixelChangedEventArgs : EventArgs
    {
        public DateTime DateTime { get; set; }

        public XY Chunk { get; set; }

        public XY Pixel { get; set; }

        public EarthPixelColor Color { get; set; }
    }
}
