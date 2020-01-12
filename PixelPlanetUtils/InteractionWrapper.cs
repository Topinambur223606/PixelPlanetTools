using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Timers;
using WebSocketSharp;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;

using XY = System.ValueTuple<byte, byte>;
using Timer = System.Timers.Timer;

namespace PixelPlanetUtils
{
    public class InteractionWrapper : IDisposable
    {
        public static bool MirrorMode
        {
            get => mirrorMode;
            set
            {
                mirrorMode = value;
                BaseUrl = mirrorMode ? mirrorUrl : mainUrl;
            }
        }

        static InteractionWrapper()
        {
            MirrorMode = false;
        }

        private const string mainUrl = "pixelplanet.fun";
        private const string mirrorUrl = "fuckyouarkeros.fun";
        private static bool mirrorMode = false;
        private static int apiConnectionFails = 0;

        public static string BaseUrl { get; private set; }

        private static string BaseHttpAdress => $"https://{BaseUrl}";
        private static string WebSocketUrl => $"wss://{BaseUrl}/ws";

        private const byte subscribeOpcode = 0xA1;
        private const byte unsubscribeOpcode = 0xA2;
        private const byte subscribeManyOpcode = 0xA3;
        private const byte pixelUpdatedOpcode = 0xC1;
        private const byte cooldownOpcode = 0xC2;
        private const byte registerCanvasOpcode = 0xA0;

        private readonly WebProxy proxy;
        private WebSocket webSocket;
        private readonly Timer preemptiveWebsocketReplacingTimer = new Timer(29 * 60 * 1000);
        private readonly ManualResetEvent websocketResetEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent listeningResetEvent;
        private readonly HashSet<XY> trackedChunks = new HashSet<XY>();

        private readonly bool listeningMode;
        private bool multipleServerFails = false;
        private DateTime disconnectionTime;
        private bool isConnectingNow = false;
        private volatile bool disposed = false;
        private bool initialConnection = true;
        private Task reconnectingDelayTask = Task.CompletedTask;
        public event EventHandler<PixelChangedEventArgs> OnPixelChanged;
        public event EventHandler<ConnectionRestoredEventArgs> OnConnectionRestored;
        public event EventHandler OnConnectionLost;

        private readonly Action<string, MessageGroup> logger;

        public InteractionWrapper(Action<string, MessageGroup> logger, WebProxy proxy) : this(logger, proxy, false)
        { }
        public InteractionWrapper(Action<string, MessageGroup> logger, bool listeningMode) : this(logger, null, listeningMode)
        { }
        public InteractionWrapper(Action<string, MessageGroup> logger, WebProxy proxy, bool listeningMode)
        {
            this.logger = logger;
            if (this.listeningMode = listeningMode)
            {
                listeningResetEvent = new ManualResetEvent(false);
            }
            this.proxy = proxy;
            preemptiveWebsocketReplacingTimer.Elapsed += PreemptiveWebsocketReplacingTimer_Elapsed;

            while (true)
            {
                try
                {
                    logger?.Invoke("Connecting to API...", MessageGroup.TechState);
                    using (HttpWebResponse response = SendRequest("api/me"))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            throw new Exception($"Error: {response.StatusDescription}");
                        }
                        logger?.Invoke("API is reachable", MessageGroup.TechInfo);
                        break;
                    }
                }
                catch (WebException ex)
                {
                    if (++apiConnectionFails == 5)
                    {
                        MirrorMode = true;
                        logger?.Invoke("Connecting to mirror", MessageGroup.TechInfo);  
                    }
                    using (HttpWebResponse response = ex.Response as HttpWebResponse)
                    {
                        if (response == null)
                        {
                            logger?.Invoke("Cannot connect: internet connection is slow or not available", MessageGroup.Error);
                            Thread.Sleep(1000);
                            continue;
                        }
                        switch (response.StatusCode)
                        {
                            case HttpStatusCode.Forbidden:
                                throw new PausingException("this IP is blocked by CloudFlare from accessing PixelPlanet");
                            case HttpStatusCode.BadGateway:
                                throw new Exception("cannot connect, site is overloaded");
                            default:
                                throw new Exception(response.StatusDescription);
                        }
                    }
                }
            }

            webSocket = new WebSocket(WebSocketUrl);
            webSocket.Log.Output = (d, s) => { };
            webSocket.OnOpen += WebSocket_OnOpen;
            webSocket.OnMessage += WebSocket_OnMessage;
            webSocket.OnClose += WebSocket_OnClose;
            ConnectWebSocket();
        }

        private void PreemptiveWebsocketReplacingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            preemptiveWebsocketReplacingTimer.Stop();
            WebSocket newWebSocket = new WebSocket(WebSocketUrl);
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
            websocketResetEvent.WaitOne();
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
            websocketResetEvent.WaitOne();
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
            websocketResetEvent.WaitOne();
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

        public bool PlacePixel(int x, int y, PixelColor color, out double coolDown, out double totalCoolDown, out string error)
        {
            if (listeningMode)
            {
                throw new InvalidOperationException();
            }
            websocketResetEvent.WaitOne();
            if (disposed)
            {
                coolDown = -1;
                totalCoolDown = -1;
                error = "Connection is disposed";
                return false;
            }
            var data = new
            {
                cn = byte.MinValue,
                clr = (byte)color,
                x,
                y,
                a = x + y + 8
            };
            try
            {
                using (HttpWebResponse response = SendRequest("api/pixel", data))
                {
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            {
                                using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                                {
                                    string responseString = sr.ReadToEnd();
                                    JObject json = JObject.Parse(responseString);
                                    if (bool.TryParse(json["success"].ToString(), out bool success) && success)
                                    {
                                        coolDown = double.Parse(json["coolDownSeconds"].ToString());
                                        totalCoolDown = double.Parse(json["waitSeconds"].ToString());
                                        error = string.Empty;
                                        multipleServerFails = false;
                                        return true;
                                    }
                                    else
                                    {
                                        if (json["errors"].Count() > 0)
                                        {
                                            string errors = string.Concat(json["errors"].Select(e => $"{Environment.NewLine}\"{e}\""));
                                            throw new PausingException($"Server responded with errors:{errors}");
                                        }
                                        else
                                        {
                                            coolDown = totalCoolDown = double.Parse(json["waitSeconds"].ToString());
                                            error = "IP is overused";
                                            multipleServerFails = false;
                                            return false;
                                        }
                                    }
                                }
                            }
                        default:
                            throw new Exception($"Error: {response.StatusDescription}");
                    }
                }
            }
            catch (WebException ex)
            {
                using (HttpWebResponse response = ex.Response as HttpWebResponse)
                {
                    if (response == null)
                    {
                        error = "internet connection is slow or not available";
                        totalCoolDown = coolDown = 1;
                        return false;
                    }
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.Forbidden:
                            throw new PausingException("Action was forbidden by pixelworld; admins could have prevented you from placing pixel or area is protected");
                        case HttpStatusCode.BadGateway:
                            totalCoolDown = coolDown = multipleServerFails ? 30 : 10;
                            multipleServerFails = true;
                            error = $"site is overloaded, delay {coolDown}s before next attempt";
                            return false;
                        case (HttpStatusCode)422:
                            error = "captcha";
                            totalCoolDown = coolDown = 0.0;
                            return false;
                        default:
                            throw new Exception(response.StatusDescription);
                    }
                }
            }
        }

        public PixelColor[,] GetChunk(XY chunk)
        {
            string url = $"{BaseHttpAdress}/chunks/0/{chunk.Item1}/{chunk.Item2}.bmp";
            using (WebClient wc = new WebClient())
            {
                byte[] pixelData = wc.DownloadData(url);
                PixelColor[,] map = new PixelColor[PixelMap.ChunkSize, PixelMap.ChunkSize];
                if (pixelData.Length == 0)
                {
                    return map;
                }
                int i = 0;
                for (int y = 0; y < PixelMap.ChunkSize; y++)
                {
                    for (int x = 0; x < PixelMap.ChunkSize; x++)
                    {
                        map[x, y] = (PixelColor)pixelData[i++];
                    }
                }
                return map;
            }
        }

        private HttpWebResponse SendRequest(string relativeUrl, object data = null, int timeout = 5000)
        {
            HttpWebRequest request = WebRequest.CreateHttp($"{BaseHttpAdress}/{relativeUrl}");
            request.Timeout = timeout;
            request.Proxy = proxy;
            request.Headers["Origin"] = request.Referer = BaseHttpAdress;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:69.0) Gecko/20100101 Firefox/69.0";
            if (data != null)
            {
                request.Method = "POST";
                request.ContentType = "application/json";
                using (Stream requestStream = request.GetRequestStream())
                {
                    using (StreamWriter streamWriter = new StreamWriter(requestStream))
                    {
                        string jsonText = JsonConvert.SerializeObject(data);
                        streamWriter.Write(jsonText);
                    }
                }
            }
            try
            {
                Task<WebResponse> responseTask = request.GetResponseAsync();
                Task.WhenAny(responseTask, Task.Delay(timeout)).Wait();
                if (responseTask.IsCompleted)
                {
                    return responseTask.Result as HttpWebResponse;
                }
                else
                {
                    responseTask.ContinueWith(t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion)
                        {
                            t.Result.Close();
                        }
                    });
                    throw new WebException();
                }
            }
            catch (AggregateException ex)
            {
                throw ex.InnerException;
            }
        }

        private void ConnectWebSocket()
        {
            if (!webSocket.IsAlive)
            {
                logger?.Invoke("Connecting via websocket...", MessageGroup.TechState);
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
            logger?.Invoke("Websocket connection closed, trying to reconnect...", MessageGroup.Error);
            reconnectingDelayTask.Wait();
            ConnectWebSocket();
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            webSocket.OnError -= WebSocket_OnError;
            logger?.Invoke($"Error on websocket: {e.Message}", MessageGroup.Error);
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
                byte chunkX = buffer[2];
                byte chunkY = buffer[4];
                byte relativeY = buffer[5];
                byte relativeX = buffer[6];
                byte color = buffer[7];
                PixelChangedEventArgs args = new PixelChangedEventArgs
                {
                    Chunk = (chunkX, chunkY),
                    Pixel = (relativeX, relativeY),
                    Color = (PixelColor)color,
                    DateTime = DateTime.Now
                };
                OnPixelChanged?.Invoke(this, args);
            }
            else if (buffer[0] == cooldownOpcode)
            {
                if (buffer[2] > 0)
                {
                    logger?.Invoke($"Current cooldown: {buffer[2]}", MessageGroup.Info);
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
            logger?.Invoke("Listening for changes via websocket", MessageGroup.TechInfo);
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
