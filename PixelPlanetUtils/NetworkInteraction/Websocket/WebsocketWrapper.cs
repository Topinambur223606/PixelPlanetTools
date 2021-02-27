using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.Sessions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using Logger = PixelPlanetUtils.Logging.Logger;
using RelPixel = System.ValueTuple<byte, byte, byte>;
using WebsocketCookie = WebSocketSharp.Net.Cookie;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.NetworkInteraction.Websocket
{
    public class WebsocketWrapper : IDisposable
    {
        private readonly Logger logger;

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
        public event EventHandler<MapChangedEventArgs> OnMapChanged;
        internal event EventHandler<ConnectionRestoredEventArgs> OnConnectionRestored;
        public event EventHandler OnConnectionLost;

        private readonly bool listeningMode;
        private readonly CanvasType canvas;

        private static WebsocketCookie ConvertCookie(KeyValuePair<string, string> cookie)
        {
            return new WebsocketCookie(cookie.Key, cookie.Value);
        }

        public WebsocketWrapper(Logger logger, bool listeningMode, ProxySettings proxySettings, Session session, CanvasType canvas)
        {
            this.canvas = canvas;
            if (this.listeningMode = listeningMode)
            {
                listeningResetEvent = new ManualResetEvent(false);
            }
            this.logger = logger;
            webSocket = new WebSocket(UrlManager.WebSocketUrl);
            webSocket.Log.Output = LogWebsocketOutput;
            webSocket.Origin = UrlManager.BaseHttpAdress;
            webSocket.UserAgent = HttpHeaderValues.UserAgent;
            if (session != null)
            {
                foreach (WebsocketCookie cookie in session.Cookies.Select(ConvertCookie))
                {
                    webSocket.SetCookie(cookie);
                }
            }
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
            logger.LogDebug("WaitWebsocketConnected()");
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

        public void PlacePixel(short x, short y, byte color)
        {
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"PlacePixel(): already disposed");
                return;
            }
            PixelMap.AbsoluteToRelative(x, out byte chunkX, out byte relativeX);
            PixelMap.AbsoluteToRelative(y, out byte chunkY, out byte relativeY);
            byte[] offsetBytes = BitConverter.GetBytes(PixelMap.RelativeToOffset(relativeX, relativeY));

            byte[] data = new byte[7]
            {
                (byte)Opcode.PixelUpdate,
                chunkX,
                chunkY,
                offsetBytes[2],
                offsetBytes[1],
                offsetBytes[0],
                color
            };
            logger.LogDebug($"PlacePixel(): Sending data {DataToString(data)}");
            webSocket.Send(data);
        }

        public void PlacePixels(XY chunk, List<RelPixel> relPixels)
        {
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"PlacePixels(): already disposed");
                return;
            }
            byte[] data = new byte[3 + 4 * relPixels.Count];
            data[0] = (byte)Opcode.PixelUpdate;
            data[1] = chunk.Item1;
            data[2] = chunk.Item2;
            int i = 3;
            foreach (RelPixel p in relPixels)
            {
                byte[] offsetBytes = BitConverter.GetBytes(PixelMap.RelativeToOffset(p.Item1, p.Item2));
                data[i++] = offsetBytes[2];
                data[i++] = offsetBytes[1];
                data[i++] = offsetBytes[0];
                data[i++] = p.Item3;
            }
            logger.LogDebug($"PlacePixels(): Sending data {DataToString(data)}");
            webSocket.Send(data);
        }

        public void PlaceVoxel(short x, short y, byte z, byte color)
        {
            WaitWebsocketConnected();
            if (disposed)
            {
                logger.LogDebug($"PlaceVoxel(): already disposed");
                return;
            }
            VoxelMap.AbsoluteToRelative(x, out byte chunkX, out byte relativeX);
            VoxelMap.AbsoluteToRelative(y, out byte chunkY, out byte relativeY);
            byte[] offsetBytes = BitConverter.GetBytes(VoxelMap.RelativeToOffset(relativeX, relativeY, z));

            byte[] data = new byte[7]
            {
                (byte)Opcode.PixelUpdate,
                chunkX,
                chunkY,
                offsetBytes[2],
                offsetBytes[1],
                offsetBytes[0],
                color
            };
            logger.LogDebug($"PlaceVoxel(): Sending data {DataToString(data)}");
            webSocket.Send(data);
        }

        public PixelReturnData GetPlaceResponse(int timeout = 5000)
        {
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
                logger.LogDebug($"StopListening(): not listening mode");
                throw new InvalidOperationException();
            }
            listeningResetEvent.Set();
        }

        private void RegisterChunk(XY chunk)
        {
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

        private void SubscribeToCanvas(CanvasType canvas)
        {
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
                logger.LogDebug("ConnectWebSocket(): socket is alive");
            }
        }

        private async void WebSocket_OnClose(object sender, CloseEventArgs e)
        {
            if (!isConnectingNow)
            {
                disconnectionTime = DateTime.Now;
                isConnectingNow = true;
            }
            OnConnectionLost?.Invoke(this, null);
            websocketResetEvent.Reset();
            logger.LogError("Websocket connection closed, trying to reconnect...");
            await reconnectingDelayTask;
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
                MapChangedEventArgs args = new MapChangedEventArgs
                {
                    DateTime = DateTime.Now,
                    Chunk = (data[1], data[2]),
                    Changes = new List<MapChange>()
                };
                for (int i = 3; i < data.Length; i += 4)
                {
                    byte color = data[i + 3];
                    uint offset;
                    byte[] offsetBytes = new byte[] { data[i + 2], data[i + 1], data[i], 0 };
                    offset = BitConverter.ToUInt32(offsetBytes, 0);
                    args.Changes.Add(new MapChange
                    {
                        Color = color,
                        Offset = offset
                    });
                }
                OnMapChanged?.Invoke(this, args);
            }
            else if (data[0] == (byte)Opcode.PixelReturn)
            {
                byte returnCode = data[1];
                byte[] wait, coolDown;
                wait = new byte[4] { data[5], data[4], data[3], data[2] };
                coolDown = new byte[2] { data[7], data[6] };
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
                Array.Reverse(data, 1, 4);
                var cooldown = BitConverter.ToUInt32(data, 1);
                if (cooldown > 0U)
                {
                    logger.LogInfo($"Current cooldown is {TimeSpan.FromMilliseconds(cooldown):mm\\:ss\\.f}");
                }
            }
        }

        private void WebSocket_OnOpen(object sender, EventArgs e)
        {
            webSocket.OnError += WebSocket_OnError;
            websocketResetEvent.Set();
            SubscribeToCanvas(canvas);
            if (trackedChunks.Count > 0)
            {
                RegisterChunks(trackedChunks);
            }
            logger.LogTechInfo("Listening for changes via websocket");
            if (!initialConnection)
            {
                isConnectingNow = false;
                logger.LogDebug("WebSocket_OnOpen(): connection restored");
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
                OnMapChanged = null;
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
