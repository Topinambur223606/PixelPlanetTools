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
            webSocket.Log.Output = (d, s) => { };
            webSocket.OnOpen += WebSocket_OnOpen;
            webSocket.OnMessage += WebSocket_OnMessage;
            webSocket.OnClose += WebSocket_OnClose;
            ConnectWebSocket();
        }

        public void WaitWebsocketConnected() => WaitWebsocketConnected();

        private void PreemptiveWebsocketReplacingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            preemptiveWebsocketReplacingTimer.Stop();
            WebSocket newWebSocket = new WebSocket(UrlManager.WebSocketUrl);
            newWebSocket.Log.Output = (d, s) => { };
            newWebSocket.Connect();
            preemptiveWebsocketReplacingTimer.Start();
            WebSocket oldWebSocket = webSocket;
            webSocket = newWebSocket;
            SubscribeToCanvas();
            if (trackedChunks.Count == 1)
            {
                SubscribeToUpdates(trackedChunks.First());
            }
            else
            {
                SubscribeToUpdatesMany(trackedChunks);
            }
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

        private void SubscribeToUpdates(XY chunk)
        {
            WaitWebsocketConnected();
            if (disposed)
            {
                return;
            }
            trackedChunks.Add(chunk);
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter sw = new BinaryWriter(ms))
                {
                    sw.Write(subscribeOpcode);
                    sw.Write(chunk.Item1);
                    sw.Write(chunk.Item2);
                }
                byte[] data = ms.ToArray();
                webSocket.Send(data);
            }
        }

        public void SubscribeToUpdates(IEnumerable<XY> chunks)
        {
            if (chunks.Skip(1).Any())
            {
                if (trackedChunks.Count == 0)
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
            else
            {
                SubscribeToUpdates(chunks.First());
            }
        }

        private void SubscribeToUpdatesMany(IEnumerable<XY> chunks)
        {
            WaitWebsocketConnected();
            if (disposed)
            {
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

        private void SubscribeToCanvas()
        {
            WaitWebsocketConnected();
            if (disposed)
            {
                return;
            }
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter sw = new BinaryWriter(ms))
                {
                    sw.Write(registerCanvasOpcode);
                    sw.Write(byte.MinValue);
                }
                byte[] data = ms.ToArray();
                webSocket.Send(data);
            }
        }

        public void StartListening()
        {
            if (!listeningMode)
            {
                throw new InvalidOperationException();
            }
            listeningResetEvent.Reset();
            listeningResetEvent.WaitOne();
        }

        public void StopListening()
        {
            if (!listeningMode)
            {
                throw new InvalidOperationException();
            }
            listeningResetEvent.Set();
        }

        private void ConnectWebSocket()
        {
            if (!webSocket.IsAlive)
            {
                logger.Log("Connecting via websocket...", MessageGroup.TechState);
                reconnectingDelayTask = Task.Delay(3000);
                webSocket.Connect();
            }
        }

        private void WebSocket_OnClose(object sender, CloseEventArgs e)
        {
            preemptiveWebsocketReplacingTimer.Stop();
            if (!isConnectingNow)
            {
                disconnectionTime = DateTime.Now;
                isConnectingNow = true;
            }
            OnConnectionLost?.Invoke(this, null);
            websocketResetEvent.Reset();
            logger.Log("Websocket connection closed, trying to reconnect...", MessageGroup.Error);
            reconnectingDelayTask.Wait();
            ConnectWebSocket();
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            webSocket.OnError -= WebSocket_OnError;
            logger.Log($"Error on websocket: {e.Message}", MessageGroup.Error);
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
                OnPixelChanged?.Invoke(this, args);
            }
            else if (!listeningMode && buffer[0] == cooldownOpcode)
            {
                if (buffer[2] > 0)
                {
                    logger.Log($"Current cooldown: {buffer[2]}", MessageGroup.Info);
                }
            }
        }

        private void WebSocket_OnOpen(object sender, EventArgs e)
        {
            webSocket.OnError += WebSocket_OnError;
            websocketResetEvent.Set();
            SubscribeToCanvas();
            if (trackedChunks.Count > 0)
            {
                SubscribeToUpdates(trackedChunks);
            }
            logger.Log("Listening for changes via websocket", MessageGroup.TechInfo);
            if (!initialConnection)
            {
                isConnectingNow = false;
                OnConnectionRestored?.Invoke(this, new ConnectionRestoredEventArgs(disconnectionTime));
            }
            initialConnection = false;
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
