using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using WebSocketSharp;
using Timer = System.Timers.Timer;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.NetworkInteraction
{
    public class WebsocketWrapper : IDisposable
    {
        private const byte subscribeOpcode = 0xA1;
        private const byte unsubscribeOpcode = 0xA2;
        private const byte subscribeManyOpcode = 0xA3;
        private const byte pixelUpdatedOpcode = 0xC1;
        private const byte cooldownOpcode = 0xC2;
        private const byte registerCanvasOpcode = 0xA0;

        private readonly Logging.Logger logger;

        private WebSocket webSocket;
        private readonly Timer preemptiveWebsocketReplacingTimer = new Timer(29 * 60 * 1000);
        private readonly ManualResetEvent websocketResetEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent listeningResetEvent;
        private readonly HashSet<XY> trackedChunks = new HashSet<XY>();

        private DateTime disconnectionTime;
        private bool isConnectingNow = false;
        private volatile bool disposed = false;
        private bool initialConnection = true;
        private Task reconnectingDelayTask = Task.CompletedTask;
        public event EventHandler<PixelChangedEventArgs> OnPixelChanged;
        public event EventHandler<ConnectionRestoredEventArgs> OnConnectionRestored;
        public event EventHandler OnConnectionLost;

        private readonly bool listeningMode;

        public WebsocketWrapper(Logging.Logger logger, bool listeningMode)
        {
            if (this.listeningMode = listeningMode)
            {
                listeningResetEvent = new ManualResetEvent(false);
            }
            this.logger = logger;
            preemptiveWebsocketReplacingTimer.Elapsed += PreemptiveWebsocketReplacingTimer_Elapsed;
            webSocket = new WebSocket(UrlManager.WebSocketUrl);
            webSocket.Log.Output = LogWebsocketOutput;
            webSocket.OnOpen += WebSocket_OnOpen;
            webSocket.OnMessage += WebSocket_OnMessage;
            webSocket.OnClose += WebSocket_OnClose;
            ConnectWebSocket();
        }
        void LogWebsocketOutput(LogData d, string s)
        {
            logger.LogDebug($"Websocket message: {s}, {d.Message}");
        }

        public void WaitWebsocketConnected()
        {
            logger.LogDebug("WaitWebsocketConnected(): waiting websocket connection");
            websocketResetEvent.WaitOne();
        }

        private void PreemptiveWebsocketReplacingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            logger.LogDebug("PreemptiveWebsocketReplacingTimer_Elapsed() started");
            preemptiveWebsocketReplacingTimer.Stop();
            WebSocket newWebSocket = new WebSocket(UrlManager.WebSocketUrl);
            newWebSocket.Log.Output = LogWebsocketOutput;
            newWebSocket.Connect();
            logger.LogDebug("PreemptiveWebsocketReplacingTimer_Elapsed(): new websocket connected");
            preemptiveWebsocketReplacingTimer.Start();
            WebSocket oldWebSocket = webSocket;
            webSocket = newWebSocket;
            SubscribeToCanvas();
            if (trackedChunks.Count == 1)
            {
                SubscribeToUpdates(trackedChunks.Single());
            }
            else
            {
                SubscribeToUpdatesMany(trackedChunks);
            }
            logger.LogDebug("PreemptiveWebsocketReplacingTimer_Elapsed(): event resubscribing");
            newWebSocket.OnOpen += WebSocket_OnOpen;
            oldWebSocket.OnOpen -= WebSocket_OnOpen;
            newWebSocket.OnMessage += WebSocket_OnMessage;
            oldWebSocket.OnMessage -= WebSocket_OnMessage;
            newWebSocket.OnClose += WebSocket_OnClose;
            oldWebSocket.OnClose -= WebSocket_OnClose;
            newWebSocket.OnError += WebSocket_OnError;
            oldWebSocket.OnError -= WebSocket_OnError;
            (oldWebSocket as IDisposable).Dispose();
        }

        public void SubscribeToUpdates(IEnumerable<XY> chunks)
        {
            if (trackedChunks.Count == 0 && chunks.Skip(1).Any())
            {
                SubscribeToUpdatesMany(chunks);
            }
            else
            {
                foreach (XY chunk in chunks)
                {
                    SubscribeToUpdates(chunk);
                }
            }
        }

        private void SubscribeToUpdates(XY chunk)
        {
            logger.LogDebug($"SubscribeToUpdates(): chunk {chunk}");
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"SubscribeToUpdates(): already disposed");
                return;
            }
            trackedChunks.Add(chunk);
            byte[] data = new byte[3]
            {
                subscribeOpcode,
                chunk.Item1,
                chunk.Item2
            };
            webSocket.Send(data);
        }

        private void SubscribeToUpdatesMany(IEnumerable<XY> chunks)
        {
            logger.LogDebug($"SubscribeToUpdatesMany(): chunks {string.Join(" ", chunks)}");
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"SubscribeToUpdatesMany(): already disposed");
                return;
            }
            trackedChunks.UnionWith(chunks);
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter sw = new BinaryWriter(ms))
                {
                    sw.Write(subscribeManyOpcode);
                    sw.Write(byte.MinValue);
                    foreach ((byte, byte) chunk in chunks)
                    {
                        sw.Write(chunk.Item2);
                        sw.Write(chunk.Item1);
                    }
                }
                byte[] data = ms.ToArray();
                webSocket.Send(data);
            }
        }

        private void SubscribeToCanvas(Canvas canvas = Canvas.Earth)
        {
            logger.LogDebug($"SubscribeToCanvas(): canvas {canvas}");
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"SubscribeToCanvas(): already disposed");
                return;
            }
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter sw = new BinaryWriter(ms))
                {
                    sw.Write(registerCanvasOpcode);
                    sw.Write((byte)canvas);
                }
                byte[] data = ms.ToArray();
                webSocket.Send(data);
            }
        }

        public void StartListening()
        {
            logger.LogDebug($"StartListening(): started");
            if (!listeningMode)
            {
                logger.LogDebug($"StartListening(): not listening mode");
                throw new InvalidOperationException();
            }
            listeningResetEvent.Reset();
            listeningResetEvent.WaitOne();
            logger.LogDebug($"StartListening(): ended");
        }

        public void StopListening()
        {
            if (!listeningMode)
            {
                logger.LogDebug($"StopListening(): not listening mode");
                throw new InvalidOperationException();
            }
            listeningResetEvent.Set();
        }

        private void ConnectWebSocket()
        {
            if (!webSocket.IsAlive)
            {
                logger.LogTechState("Connecting via websocket...");
                reconnectingDelayTask = Task.Delay(3000);
                webSocket.Connect();
            }
            else
            {
                logger.LogDebug("ConnectWebSocket(): socket is already alive");
            }
        }

        private void WebSocket_OnClose(object sender, CloseEventArgs e)
        {
            logger.LogDebug("WebSocket_OnClose(): start");
            preemptiveWebsocketReplacingTimer.Stop();
            if (!isConnectingNow)
            {
                disconnectionTime = DateTime.Now;
                isConnectingNow = true;
            }
            logger.LogDebug("WebSocket_OnClose(): invoking OnConnectionLost");
            OnConnectionLost?.Invoke(this, null);
            websocketResetEvent.Reset();
            logger.LogError("Websocket connection closed, trying to reconnect...");
            reconnectingDelayTask.Wait();
            ConnectWebSocket();
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            webSocket.OnError -= WebSocket_OnError;
            logger.LogError($"Error on websocket: {e.Message}");
            webSocket.Close();
        }

        private void WebSocket_OnMessage(object sender, MessageEventArgs e)
        {
            byte[] buffer = e.RawData;
            if (buffer.Length == 0)
            {
                return;
            }
            if (buffer[0] == pixelUpdatedOpcode)
            {
                logger.LogDebug($"WebSocket_OnMessage(): got pixel update {string.Join(" ", e.RawData.Select(b => b.ToString("X2")))}");
                byte chunkX = buffer[1];
                byte chunkY = buffer[2];
                byte relativeY = buffer[4];
                byte relativeX = buffer[5];
                byte color = buffer[6];
                PixelChangedEventArgs args = new PixelChangedEventArgs
                {
                    Chunk = (chunkX, chunkY),
                    Pixel = (relativeX, relativeY),
                    Color = (PixelColor)color,
                    DateTime = DateTime.Now
                };
                logger.LogDebug($"WebSocket_OnMessage(): pixel update: {args.Color} at {args.Chunk}:{args.Pixel}");
                OnPixelChanged?.Invoke(this, args);
            }
            else if (!listeningMode && buffer[0] == cooldownOpcode)
            {
                logger.LogDebug($"WebSocket_OnMessage(): got cooldown {string.Join(" ", e.RawData.Select(b => b.ToString("X2")))}");
                if (buffer[2] > 0)
                {
                    logger.LogInfo($"Current cooldown: {buffer[2]}");
                }
            }
            else
            {
                logger.LogDebug($"WebSocket_OnMessage(): opcode {buffer[0]}, length {buffer.Length}, ignoring");
            }
        }

        private void WebSocket_OnOpen(object sender, EventArgs e)
        {
            logger.LogDebug($"WebSocket_OnOpen(): start");
            webSocket.OnError += WebSocket_OnError;
            websocketResetEvent.Set();
            SubscribeToCanvas();
            if (trackedChunks.Count > 0)
            {
                SubscribeToUpdates(trackedChunks);
            }
            logger.LogTechInfo("Listening for changes via websocket");
            if (!initialConnection)
            {
                isConnectingNow = false;
                logger.LogDebug("WebSocket_OnOpen(): invoking OnConnectionResored");
                OnConnectionRestored?.Invoke(this, new ConnectionRestoredEventArgs(disconnectionTime));
            }
            initialConnection = false;
            logger.LogDebug($"WebSocket_OnOpen(): preemptive reconnection timer reset");
            preemptiveWebsocketReplacingTimer.Stop();
            preemptiveWebsocketReplacingTimer.Start();
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                webSocket.OnOpen -= WebSocket_OnOpen;
                webSocket.OnMessage -= WebSocket_OnMessage;
                webSocket.OnError -= WebSocket_OnError;
                webSocket.OnClose -= WebSocket_OnClose;
                (webSocket as IDisposable).Dispose();
                preemptiveWebsocketReplacingTimer.Dispose();
                OnPixelChanged = null;
                websocketResetEvent.Dispose();
                if (listeningMode)
                {
                    listeningResetEvent.Dispose();
                }
            }
        }
    }
}
