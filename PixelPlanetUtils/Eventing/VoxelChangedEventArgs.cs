using System;
using XY = System.ValueTuple<byte, byte>;
using XYZ = System.ValueTuple<byte, byte, byte>;

namespace PixelPlanetUtils.Eventing
{
    public class VoxelChangedEventArgs : EventArgs
    {
        public DateTime DateTime { get; set; }

        public XY Chunk { get; set; }

        public XYZ Pixel { get; set; }

        public byte Color { get; set; }
    }
}
