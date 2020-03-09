using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.CanvasInteraction
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    public class ChunkCache
    {
        private static readonly TimeSpan maxOffline = TimeSpan.FromMinutes(1);
        private readonly Dictionary<XY, PixelColor[,]> CachedChunks = new Dictionary<XY, PixelColor[,]>();
        private WebsocketWrapper wrapper;
        private readonly bool interactiveMode;
        private readonly List<XY> chunks;

        public event EventHandler OnMapUpdated;
        private readonly Logger logger;

        public WebsocketWrapper Wrapper
        {
            get
            {
                return wrapper;
            }
            set
            {
                logger.LogDebug("ChunkCache.Wrapper set started");
                wrapper = value;
                wrapper.SubscribeToUpdates(chunks);
                if (interactiveMode)
                {
                    logger.LogDebug("ChunkCache.Wrapper event subscription");
                    wrapper.OnPixelChanged += Wrapper_OnPixelChanged;
                    wrapper.OnConnectionRestored += Wrapper_OnConnectionRestored;
                }
            }
        }

        public void DownloadChunks()
        {
            logger.LogTechState("Downloading chunk data...");
            const int maxFails = 5;
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
                            logger.LogDebug($"DownloadChunks(): downloading chunk {chunkXY}");
                            CachedChunks[chunkXY] = HttpWrapper.GetChunk(chunkXY);
                            success = true;
                        }
                        catch
                        {
                            logger.LogError("Cannot download chunk data, waiting 5s before next attempt");
                            if (++fails == maxFails)
                            {
                                logger.LogDebug($"DownloadChunks(): {fails} fails in row, aborting");
                                break;
                            }
                            Thread.Sleep(TimeSpan.FromSeconds(5));
                        }
                    } while (!success);
                }
                if (fails == 5)
                {
                    logger.LogTechState("Waiting 30s before next attempt");
                    Thread.Sleep(TimeSpan.FromSeconds(30));
                }
            } while (fails == 5);
            logger.LogTechInfo("Chunk data is downloaded");
            OnMapUpdated?.Invoke(this, null);
        }

        private void Wrapper_OnConnectionRestored(object sender, ConnectionRestoredEventArgs e)
        {
            if (e.OfflinePeriod > maxOffline)
            {
                logger.LogDebug($"Wrapper_OnConnectionRestored(): too long offline, downloading chunks again");
                DownloadChunks();
            }
            else
            {
                logger.LogDebug($"Wrapper_OnConnectionRestored(): {e.OfflinePeriod.TotalSeconds.ToString("F2")} seconds offline");
                OnMapUpdated(this, null);
            }
        }

        public ChunkCache(IEnumerable<Pixel> pixels, Logger logger)
        {
            interactiveMode = true;
            this.logger = logger;
            logger.LogTechState("Calculating list of chunks...");
            chunks = pixels.Select(p =>
            {
                PixelMap.ConvertToRelative(p.Item1, out byte chunkX, out _);
                PixelMap.ConvertToRelative(p.Item2, out byte chunkY, out _);
                return (chunkX, chunkY);
            }).Distinct().ToList();
            logger.LogTechInfo("Chunk list is calculated");
        }

        public ChunkCache(short x1, short y1, short x2, short y2, Logger logger)
        {
            interactiveMode = false;
            this.logger = logger;
            logger.LogTechState("Calculating list of chunks...");
            PixelMap.ConvertToRelative(x1, out byte chunkX1, out _);
            PixelMap.ConvertToRelative(y1, out byte chunkY1, out _);
            PixelMap.ConvertToRelative(x2, out byte chunkX2, out _);
            PixelMap.ConvertToRelative(y2, out byte chunkY2, out _);
            chunks = new List<XY>();
            for (ushort i = chunkX1; i <= chunkX2; i++)
            {
                for (ushort j = chunkY1; j <= chunkY2; j++)
                {
                    chunks.Add(((byte)i, (byte)j));
                }
            }
            logger.LogTechInfo("Chunk list is calculated");
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
            if (CachedChunks.TryGetValue(e.Chunk, out PixelColor[,] chunkMap))
            {
                logger.LogDebug("Wrapper_OnPixelChanged(): writing update to map");
                (byte rX, byte rY) = e.Pixel;
                chunkMap[rX, rY] = e.Color;
            }
            else
            {
                logger.LogDebug("Wrapper_OnPixelChanged(): pixel is not in loaded area");
            }
        }
    }
}
