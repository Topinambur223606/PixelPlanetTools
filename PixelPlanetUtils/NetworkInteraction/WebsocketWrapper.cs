using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.NetworkInteraction
{
    public class WebsocketWrapper : IDisposable
    {
        private readonly Logging.Logger logger;

        private readonly WebSocket webSocket;
        private readonly ManualResetEvent websocketResetEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent listeningResetEvent;
        private readonly HashSet<XY> trackedChunks = new HashSet<XY>();

        private readonly ConcurrentQueue<PixelReturnData> pixelReturnData = new ConcurrentQueue<PixelReturnData>();
        private readonly ManualResetEvent pixelReturnResetEvent = new ManualResetEvent(false);

        private DateTime disconnectionTime;
        private bool isConnectingNow = false;
        private volatile bool disposed = false;
        private bool initialConnection = true;
        private Task reconnectingDelayTask = Task.CompletedTask;
        public event EventHandler<PixelChangedEventArgs> OnPixelChanged;
        public event EventHandler<ConnectionRestoredEventArgs> OnConnectionRestored;
        public event EventHandler OnConnectionLost;

        private readonly bool listeningMode;

        public WebsocketWrapper(Logging.Logger logger, bool listeningMode, ProxySettings proxySettings)
        {
            if (this.listeningMode = listeningMode)
            {
                listeningResetEvent = new ManualResetEvent(false);
            }
            this.logger = logger;
            webSocket = new WebSocket(UrlManager.WebSocketUrl);
            webSocket.Log.Output = LogWebsocketOutput;
            if (proxySettings != null)
            {
                webSocket.SetProxy(proxySettings.Address, proxySettings.Username, proxySettings.Password);
            }
            webSocket.OnOpen += WebSocket_OnOpen;
            webSocket.OnMessage += WebSocket_OnMessage;
            webSocket.OnClose += WebSocket_OnClose;
            ConnectWebSocket();
        }

        public void WaitWebsocketConnected()
        {
            logger.LogDebug("WaitWebsocketConnected(): waiting for websocket connection");
            websocketResetEvent.WaitOne();
        }

        public void RegisterChunks(IEnumerable<XY> chunks)
        {
            if (trackedChunks.Count == 0 && chunks.Skip(1).Any())
            {
                RegisterMultipleChunks(chunks);
            }
            else
            {
                foreach (XY chunk in chunks)
                {
                    RegisterChunk(chunk);
                }
            }
        }

        public void PlacePixel(short x, short y, EarthPixelColor color)
        {
            logger.LogDebug($"PlacePixel(): {color} at ({x};{y})");
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"PlacePixel(): already disposed");
                return;
            }
            PixelMap.ConvertToRelative(x, out byte chunkX, out byte relativeX);
            PixelMap.ConvertToRelative(y, out byte chunkY, out byte relativeY);
            byte[] data = new byte[7]
            {
                (byte)Opcode.PixelUpdate,
                chunkX,
                chunkY,
                0,
                relativeY,
                relativeX,
                (byte)color
            };
            logger.LogDebug($"PlacePixel(): Sending data {DataToString(data)}");
            webSocket.Send(data);
        }

        public PixelReturnData GetPlacePixelResponse(int timeout = 5000)
        {
            logger.LogDebug("GetPlacePixelResponse(): waiting for response");
            bool received = pixelReturnResetEvent.WaitOne(timeout);
            if (!received || !pixelReturnData.TryDequeue(out PixelReturnData result))
            {
                logger.LogError("No response after placing pixel");
                return null;
            }
            if (pixelReturnData.Count == 0)
            {
                pixelReturnResetEvent.Reset();
            }
            return result;
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

        private void RegisterChunk(XY chunk)
        {
            logger.LogDebug($"RegisterChunk(): chunk {chunk}");
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"RegisterChunk(): already disposed");
                return;
            }
            trackedChunks.Add(chunk);
            byte[] data = new byte[3]
            {
                (byte)Opcode.RegisterChunk,
                chunk.Item1,
                chunk.Item2
            };
            logger.LogDebug($"RegisterChunk(): sending data {DataToString(data)}");
            webSocket.Send(data);
        }

        private void RegisterMultipleChunks(IEnumerable<XY> chunks)
        {
            logger.LogDebug($"RegisterMultipleChunks(): chunks {string.Join(" ", chunks)}");
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"RegisterMultipleChunks(): already disposed");
                return;
            }
            trackedChunks.UnionWith(chunks);
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter sw = new BinaryWriter(ms))
                {
                    sw.Write((byte)Opcode.RegisterMultipleChunks);
                    sw.Write(byte.MinValue);
                    foreach ((byte, byte) chunk in chunks)
                    {
                        sw.Write(chunk.Item2);
                        sw.Write(chunk.Item1);
                    }
                }
                byte[] data = ms.ToArray();
                logger.LogDebug($"RegisterMultipleChunks(): sending data {DataToString(data)}");
                webSocket.Send(data);
            }
        }

        private void SubscribeToCanvas(CanvasType canvas = CanvasType.Earth)
        {
            logger.LogDebug($"SubscribeToCanvas(): canvas {canvas}");
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"SubscribeToCanvas(): already disposed");
                return;
            }
            byte[] data = new byte[2]
            {
                (byte)Opcode.RegisterCanvas,
                (byte)canvas
            };
            logger.LogDebug($"SubscribeToCanvas(): sending data {DataToString(data)}");
            webSocket.Send(data);
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
            byte[] data = e.RawData;
            if (!e.IsBinary || data.Length == 0)
            {
                return;
            }
            if (data[0] == (byte)Opcode.PixelUpdate)
            {
                logger.LogDebug($"WebSocket_OnMessage(): got pixel update {DataToString(data)}");
                byte chunkX = data[1];
                byte chunkY = data[2];
                byte relativeY = data[4];
                byte relativeX = data[5];
                byte color = data[6];
                PixelChangedEventArgs args = new PixelChangedEventArgs
                {
                    Chunk = (chunkX, chunkY),
                    Pixel = (relativeX, relativeY),
                    Color = (EarthPixelColor)color,
                    DateTime = DateTime.Now
                };
                logger.LogDebug($"WebSocket_OnMessage(): pixel update: {args.Color} at {args.Chunk}:{args.Pixel}");
                OnPixelChanged?.Invoke(this, args);
            }
            else if (data[0] == (byte)Opcode.PixelReturn)
            {
                logger.LogDebug($"WebSocket_OnMessage(): got pixel return {DataToString(data)}");
                byte returnCode = data[1];
                byte[] wait, coolDown;
                if (BitConverter.IsLittleEndian)
                {
                    wait = new byte[4] { data[5], data[4], data[3], data[2] };
                    coolDown = new byte[2] { data[7], data[6] };
                }
                else
                {
                    wait = new byte[4] { data[2], data[3], data[4], data[5] };
                    coolDown = new byte[2] { data[6], data[7] };
                }
                PixelReturnData received = new PixelReturnData
                {
                    ReturnCode = (ReturnCode)returnCode,
                    Wait = BitConverter.ToUInt32(wait, 0),
                    CoolDownSeconds = BitConverter.ToInt16(coolDown, 0)
                };
                pixelReturnData.Enqueue(received);
                pixelReturnResetEvent.Set();
            }
            else if (!listeningMode && data[0] == (byte)Opcode.Cooldown)
            {
                logger.LogDebug($"WebSocket_OnMessage(): got cooldown {DataToString(data)}");
                if (data[1] + data[2] > 0)
                {
                    logger.LogInfo($"Current cooldown: {(data[1] << 8) + data[2]}");
                }
            }
            else
            {
                logger.LogDebug($"WebSocket_OnMessage(): opcode {(Opcode)data[0]}, ignoring");
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
                RegisterChunks(trackedChunks);
            }
            logger.LogTechInfo("Listening for changes via websocket");
            if (!initialConnection)
            {
                isConnectingNow = false;
                logger.LogDebug("WebSocket_OnOpen(): invoking OnConnectionResored");
                OnConnectionRestored?.Invoke(this, new ConnectionRestoredEventArgs(disconnectionTime));
            }
            initialConnection = false;
        }

        private void LogWebsocketOutput(LogData d, string s)
        {
            logger.LogDebug($"Websocket log message: {s}, {d.Message}");
        }

        private static string DataToString(byte[] data)
        {
            int offset = data[0] == (byte)Opcode.RegisterMultipleChunks ? 1 : 0;
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            for (int j = 0; j < data.Length; j++)
            {
                sb.Append(data[j].ToString("X2"));
                if (j % 2 == offset && j < data.Length - 1)
                {
                    sb.Append(' ');
                }
            }
            sb.Append('"');
            return sb.ToString();
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
                OnPixelChanged = null;
                websocketResetEvent.Dispose();
                pixelReturnResetEvent.Dispose();
                if (listeningMode)
                {
                    listeningResetEvent.Dispose();
                }
            }
        }
    }
}
