using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.Canvas
{
    using Pixel = ValueTuple<short, short, EarthPixelColor>;

    public class ChunkCache
    {
        private static readonly TimeSpan maxOffline = TimeSpan.FromMinutes(1);

        private WebsocketWrapper wrapper;
        private readonly Logger logger;

        private readonly List<XY> chunks;

        internal ConcurrentDictionary<XY, EarthPixelColor[,]> CachedChunks { get; } = new ConcurrentDictionary<XY, EarthPixelColor[,]>();

        private readonly bool interactiveMode;

        public event EventHandler OnMapUpdated;

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
                wrapper.RegisterChunks(chunks);
                if (interactiveMode)
                {
                    logger.LogDebug("ChunkCache.Wrapper event subscription");
                    wrapper.OnPixelChanged += Wrapper_OnPixelChanged;
                    wrapper.OnConnectionRestored += Wrapper_OnConnectionRestored;
                }
            }
        }

        public ChunkCache(IEnumerable<Pixel> pixels, Logger logger)
        {
            interactiveMode = true;
            this.logger = logger;
            logger.LogTechState("Calculating list of chunks...");
            chunks = pixels.AsParallel().Select(p =>
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
            for (ushort x = chunkX1; x <= chunkX2; x++)
            {
                for (ushort y = chunkY1; y <= chunkY2; y++)
                {
                    chunks.Add(((byte)x, (byte)y));
                }
            }
            logger.LogTechInfo("Chunk list is calculated");
        }

        public void DownloadChunks()
        {
            logger.LogTechState("Downloading chunk data...");
            const int maxFails = 5;
            Parallel.ForEach(chunks, chunkXY =>
            {
                int fails;
                bool success = false;
                do
                {
                    fails = 0;
                    try
                    {
                        logger.LogDebug($"DownloadChunks(): downloading chunk {chunkXY}");
                        byte[] data = GetChunkData(chunkXY, 0);
                        BinaryConversion.DropPixelProtectionInfo(data);
                        CachedChunks[chunkXY] = BinaryConversion.ToColorRectangle(data, PixelMap.ChunkSideSize, PixelMap.ChunkSideSize);
                        logger.LogDebug($"DownloadChunks(): downloaded chunk  {chunkXY}");
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug($"DownloadChunks(): error - {ex.Message}");
                        if (++fails == maxFails)
                        {
                            fails = 0;
                            logger.LogDebug($"DownloadChunks(): {maxFails} fails in row, pause 30s");
                            Thread.Sleep(TimeSpan.FromSeconds(30));
                            break;
                        }
                    }
                } while (!success);
            });
            logger.LogTechInfo("Chunk data is downloaded");
            OnMapUpdated?.Invoke(this, null);
        }

        public EarthPixelColor GetPixelColor(short x, short y)
        {
            PixelMap.ConvertToRelative(x, out byte chunkX, out byte relativeX);
            PixelMap.ConvertToRelative(y, out byte chunkY, out byte relativeY);
            try
            {
                EarthPixelColor[,] chunkMap = CachedChunks[(chunkX, chunkY)];
                return chunkMap[relativeY, relativeX];
            }
            catch
            {
                logger.LogDebug($"GetPixelColor(): exception while retrieving ({x};{y}) = ({chunkX};{chunkY}):({relativeX};{relativeY})");
                throw;
            }
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
                logger.LogDebug($"Wrapper_OnConnectionRestored(): {e.OfflinePeriod.TotalSeconds:F2} seconds offline");
                OnMapUpdated(this, null);
            }
        }

        private void Wrapper_OnPixelChanged(object sender, PixelChangedEventArgs e)
        {
            if (CachedChunks.TryGetValue(e.Chunk, out EarthPixelColor[,] chunkMap))
            {
                logger.LogDebug("Wrapper_OnPixelChanged(): writing update to map");
                (byte rX, byte rY) = e.Pixel;
                chunkMap[rY, rX] = e.Color;
            }
            else
            {
                logger.LogDebug("Wrapper_OnPixelChanged(): pixel is not in loaded area");
            }
        }

        private static byte[] GetChunkData(XY chunk, byte canvas)
        {
            string url = UrlManager.ChunkUrl(canvas, chunk.Item1, chunk.Item2);
            using (WebClient wc = new WebClient())
            {
                return wc.DownloadData(url);
            }
        }
    }
}
