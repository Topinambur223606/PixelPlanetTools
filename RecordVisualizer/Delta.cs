using PixelPlanetUtils;
using System;
using System.Collections.Generic;

namespace RecordVisualizer
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    class Delta
    {
        public DateTime DateTime { get; set; }
        public List<Pixel> Pixels { get; set; }
    }
}
