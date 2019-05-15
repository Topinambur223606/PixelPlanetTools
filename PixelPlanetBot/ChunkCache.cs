using System;
using System.Collections.Generic;
using System.Linq;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetBot
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    class ChunkCache
    {
        private readonly Dictionary<XY, PixelColor[,]> CachedChunks = new Dictionary<XY, PixelColor[,]>();
        private InteractionWrapper wrapper;

        public InteractionWrapper Wrapper
        {
            get
            {
                return wrapper;
            }
            set
            {
                wrapper = value;
                wrapper.OnPixelChanged += Wrapper_OnPixelChanged;
                foreach (XY chunkXY in chunks)
                {
                    if (!CachedChunks.ContainsKey(chunkXY))
                    {
                        CachedChunks[chunkXY] = wrapper.GetChunk(chunkXY);
                    }
                    wrapper.SubscribeToUpdates(chunkXY);
                }
            }
        }

        private readonly List<XY> chunks;


        public ChunkCache(IEnumerable<Pixel> pixels)
        {
            chunks = pixels.Select(p =>
            {
                PixelMap.ConvertToRelative(p.Item1, out byte chunkX, out _);
                PixelMap.ConvertToRelative(p.Item2, out byte chunkY, out _);
                return (chunkX, chunkY);
            }).Distinct().ToList();
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
