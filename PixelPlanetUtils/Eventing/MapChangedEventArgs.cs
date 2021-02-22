using System;
using System.Collections.Generic;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.Eventing
{
    public class MapChangedEventArgs : EventArgs
    {
        public DateTime DateTime { get; set; }

        public XY Chunk { get; set; }

        public List<MapChange> Changes { get; set; }
    }
}
