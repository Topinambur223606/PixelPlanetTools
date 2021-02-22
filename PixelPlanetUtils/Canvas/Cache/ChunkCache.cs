using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Websocket;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.Canvas.Cache
{
    public abstract class ChunkCache
    {
        protected static readonly TimeSpan maxOffline = TimeSpan.FromMinutes(1);

        protected CanvasType canvas;
        internal WebsocketWrapper wrapper;
        internal Logger logger;
        internal List<XY> chunks;
        protected bool interactiveMode;

        public event EventHandler OnMapUpdated;

        public WebsocketWrapper Wrapper
        {
            get
            {
                return wrapper;
            }
            set
            {
                wrapper = value;
                wrapper.RegisterChunks(chunks);
                if (interactiveMode)
                {
                    wrapper.OnMapChanged += Wrapper_OnMapChanged;
                    wrapper.OnConnectionRestored += Wrapper_OnConnectionRestored;
                }
            }
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
                        logger.LogDebug($"DownloadChunks(): chunk {chunkXY}");
                        byte[] data = GetChunkData(chunkXY, (byte)canvas);
                        BinaryConversion.DropPixelProtectionInfo(data);
                        SaveChunk(chunkXY, data);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug($"DownloadChunks(): error - {ex}");
                        if (++fails == maxFails)
                        {
                            fails = 0;
                            Thread.Sleep(TimeSpan.FromSeconds(30));
                            break;
                        }
                    }
                } while (!success);
            });
            logger.LogTechInfo("Chunk data is downloaded");
            OnMapUpdated?.Invoke(this, null);
        }

        private void Wrapper_OnConnectionRestored(object sender, ConnectionRestoredEventArgs e)
        {
            logger.LogDebug($"Wrapper_OnConnectionRestored(): {e.OfflinePeriod.TotalSeconds:F2} seconds offline");
            if (e.OfflinePeriod > maxOffline)
            {
                DownloadChunks();
            }
            else
            {
                OnMapUpdated?.Invoke(this, null);
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

        protected abstract void SaveChunk(XY chunkXY, byte[] data);

        protected abstract void Wrapper_OnMapChanged(object sender, MapChangedEventArgs e);
    }
}
