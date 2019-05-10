using System.Collections.Generic;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetBot
{
    class ChunkCache
    {
        readonly Dictionary<XY, PixelColor[,]> CachedChunks;

        readonly InteractionWrapper wrapper;

        readonly byte relativeX1, relativeY1, relativeX2, relativeY2;
        readonly byte chunkX1, chunkY1, chunkX2, chunkY2;

        public ChunkCache(short x, short y, ushort width, ushort height, InteractionWrapper wrapper)
        {
            PixelMap.ConvertToRelative(x, out chunkX1, out relativeX1);
            PixelMap.ConvertToRelative(y, out chunkY1, out relativeY1);
            PixelMap.ConvertToRelative(x + width, out chunkX2, out relativeX2);
            PixelMap.ConvertToRelative(y + height, out chunkY2, out relativeY2);

            CachedChunks = new Dictionary<XY, PixelColor[,]>();
            this.wrapper = wrapper;
            wrapper.OnPixelChanged += Wrapper_OnPixelChanged;

            for (byte i = chunkX1; i <= chunkX2; i++)
            {
                for (byte j = chunkY1; j <= chunkY2; j++)
                {
                    XY chunkXY = (i, j);
                    CachedChunks[chunkXY] = wrapper.GetChunk(chunkXY);
                    wrapper.TrackChunk(chunkXY);
                }
            }
        }

        public PixelColor GetPixel(int x, int y)
        {
            PixelMap.ConvertToRelative(x, out byte chunkX, out byte relativeX);
            PixelMap.ConvertToRelative(y, out byte chunkY, out byte relativeY);
            PixelColor[,] chunkMap = CachedChunks[(chunkX, chunkY)];
            return chunkMap[relativeX, relativeY];
        }

        private void Wrapper_OnPixelChanged(object sender, PixelChangedEventArgs e)
        {
            PixelColor[,] chunkMap = CachedChunks[e.Chunk];
            (byte rX, byte rY) = e.Pixel;
            chunkMap[rX, rY] = e.Color;
        }
    }
}
