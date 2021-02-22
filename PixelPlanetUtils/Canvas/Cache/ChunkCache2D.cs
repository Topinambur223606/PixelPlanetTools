using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Pixel = System.ValueTuple<short, short, byte>;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.Canvas.Cache
{
    public class ChunkCache2D : ChunkCache
    {
        internal ConcurrentDictionary<XY, byte[,]> CachedChunks { get; } = new ConcurrentDictionary<XY, byte[,]>();

        public ChunkCache2D(IEnumerable<Pixel> pixels, Logger logger, CanvasType canvas)
        {
            this.canvas = canvas;
            interactiveMode = true;
            this.logger = logger;
            logger.LogTechState("Calculating list of chunks...");
            chunks = pixels.AsParallel().Select(p =>
            {
                PixelMap.AbsoluteToRelative(p.Item1, out byte chunkX, out _);
                PixelMap.AbsoluteToRelative(p.Item2, out byte chunkY, out _);
                return (chunkX, chunkY);
            }).Distinct().ToList();
            logger.LogTechInfo("Chunk list is calculated");
        }

        public ChunkCache2D(short x1, short y1, short x2, short y2, Logger logger, CanvasType canvas)
        {
            this.canvas = canvas;
            interactiveMode = false;
            this.logger = logger;
            logger.LogTechState("Calculating list of chunks...");
            PixelMap.AbsoluteToRelative(x1, out byte chunkX1, out _);
            PixelMap.AbsoluteToRelative(y1, out byte chunkY1, out _);
            PixelMap.AbsoluteToRelative(x2, out byte chunkX2, out _);
            PixelMap.AbsoluteToRelative(y2, out byte chunkY2, out _);
            chunks = new List<XY>();
            for (ushort x = chunkX1; x <= chunkX2; x++)
            {
                for (ushort y = chunkY1; y <= chunkY2; y++)
                {
                    chunks.Add(((byte)x, (byte)y));
                }
            }
            logger.LogTechInfo("Chunk list is calculated");
        }

        public byte GetPixelColor(short x, short y)
        {
            PixelMap.AbsoluteToRelative(x, out byte chunkX, out byte relativeX);
            PixelMap.AbsoluteToRelative(y, out byte chunkY, out byte relativeY);
            try
            {
                byte[,] chunkMap = CachedChunks[(chunkX, chunkY)];
                return chunkMap[relativeY, relativeX];
            }
            catch
            {
                logger.LogDebug($"GetPixelColor(): exception while retrieving ({x};{y}) = ({chunkX};{chunkY}):({relativeX};{relativeY})");
                throw;
            }
        }

        protected override void Wrapper_OnMapChanged(object sender, MapChangedEventArgs e)
        {
            if (CachedChunks.TryGetValue(e.Chunk, out byte[,] chunkMap))
            {
                foreach (MapChange c in e.Changes)
                {
                    PixelMap.OffsetToRelative(c.Offset, out byte rX, out byte rY);
                    chunkMap[rY, rX] = c.Color;
                }
            }
            else
            {
                logger.LogDebug("Wrapper_OnPixelChanged(): updates not in loaded area");
            }
        }

        protected override void SaveChunk(XY chunkXY, byte[] data)
        {
            CachedChunks[chunkXY] = BinaryConversion.ToColorRectangle(data, PixelMap.ChunkSize, PixelMap.ChunkSize);
        }
    }
}
