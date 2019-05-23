using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetBot
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    internal class ChunkCache
    {
        private readonly Dictionary<XY, PixelColor[,]> CachedChunks = new Dictionary<XY, PixelColor[,]>();
        private InteractionWrapper wrapper;

        private readonly List<XY> chunks;

        public event EventHandler OnMapRedownloaded;

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
                wrapper.OnConnectionRestored += Wrapper_OnConnectionRestored;
                DownloadChunks();
                wrapper.SubscribeToUpdates(chunks);
            }
        }

        private void DownloadChunks()
        {
            Program.LogLine("Downloading chunk data...", MessageGroup.State, ConsoleColor.Yellow);
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
                            Program.LogLine("Cannot download chunk data, waiting 5s before next attempt", MessageGroup.Error, ConsoleColor.Red);
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
                    Program.LogLine("Waiting 30s before next attempt", MessageGroup.State, ConsoleColor.Yellow);
                    Thread.Sleep(TimeSpan.FromSeconds(30));
                }
            } while (fails == 5);
            Program.LogLine("Chunk data is downloaded", MessageGroup.Info, ConsoleColor.Blue);
        }

        private void Wrapper_OnConnectionRestored(object sender, EventArgs e)
        {
            DownloadChunks();
            OnMapRedownloaded?.Invoke(this, null);
        }

        public ChunkCache(IEnumerable<Pixel> pixels)
        {
            chunks = pixels.Select(p =>
            {
                PixelMap.ConvertToRelative(p.Item1, out byte chunkX, out _);
                PixelMap.ConvertToRelative(p.Item2, out byte chunkY, out _);
                return (chunkX, chunkY);
            }).Distinct().ToList();
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
