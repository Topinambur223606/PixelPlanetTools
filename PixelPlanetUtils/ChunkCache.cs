using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    public class ChunkCache
    {
        private readonly Dictionary<XY, PixelColor[,]> CachedChunks = new Dictionary<XY, PixelColor[,]>();
        private InteractionWrapper wrapper;
        private readonly bool interactiveMode;
        private readonly List<XY> chunks;

        public event EventHandler OnMapDownloaded;
        private readonly Action<string, MessageGroup> logger;

        public InteractionWrapper Wrapper
        {
            get
            {
                return wrapper;
            }
            set
            {
                wrapper = value;
                wrapper.SubscribeToUpdates(chunks);
                if (interactiveMode)
                {
                    wrapper.OnPixelChanged += Wrapper_OnPixelChanged;
                    wrapper.OnConnectionRestored += Wrapper_OnConnectionRestored;
                }
            }
        }

        public void DownloadChunks()
        {
            logger?.Invoke("Downloading chunk data...", MessageGroup.TechState);
            int fails;
            do
            {
                fails = 0;
                foreach (XY chunkXY in chunks)
                {
                    bool success = false;
                    do
                    {
                        try
                        {
                            CachedChunks[chunkXY] = wrapper.GetChunk(chunkXY);
                            success = true;
                        }
                        catch
                        {
                            logger?.Invoke("Cannot download chunk data, waiting 5s before next attempt", MessageGroup.Error);
                            if (++fails == 5)
                            {
                                break;
                            }
                            Thread.Sleep(TimeSpan.FromSeconds(5));
                        }
                    }
                    while (!success);
                }
                if (fails == 5)
                {
                    logger?.Invoke("Waiting 30s before next attempt", MessageGroup.TechState);
                    Thread.Sleep(TimeSpan.FromSeconds(30));
                }
            } while (fails == 5);
            logger?.Invoke("Chunk data is downloaded", MessageGroup.TechInfo);
            OnMapDownloaded?.Invoke(this, null);
        }

        private void Wrapper_OnConnectionRestored(object sender, EventArgs e)
        {
            DownloadChunks();
        }

        public ChunkCache(IEnumerable<Pixel> pixels, Action<string, MessageGroup> logger, bool interactiveMode = true)
        {
            interactiveMode = true;
            this.logger = logger;
            chunks = pixels.Select(p =>
            {
                PixelMap.ConvertToRelative(p.Item1, out byte chunkX, out _);
                PixelMap.ConvertToRelative(p.Item2, out byte chunkY, out _);
                return (chunkX, chunkY);
            }).Distinct().ToList();
        }

        public ChunkCache(short x1, short y1, short x2, short y2, Action<string, MessageGroup> logger)
        {
            interactiveMode = false;
            this.logger = logger;
            PixelMap.ConvertToRelative(x1, out byte chunkX1, out _);
            PixelMap.ConvertToRelative(y1, out byte chunkY1, out _);
            PixelMap.ConvertToRelative(x2, out byte chunkX2, out _);
            PixelMap.ConvertToRelative(y2, out byte chunkY2, out _);
            chunks = new List<XY>();
            for (byte i = chunkX1; i <= chunkX2; i++)
            {
                for (byte j = chunkY1; j <= chunkY2; j++)
                {
                    chunks.Add((i, j));
                }
            }
        }

        public PixelColor GetPixelColor(short x, short y)
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
