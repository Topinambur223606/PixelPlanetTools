using PixelPlanetUtils.Canvas;
using System;
using System.Collections.Generic;

namespace RecordVisualizer
{
    using Pixel = ValueTuple<short, short, EarthPixelColor>;

    class Delta
    {
        public DateTime DateTime { get; set; }
        public List<Pixel> Pixels { get; set; }
    }
}
