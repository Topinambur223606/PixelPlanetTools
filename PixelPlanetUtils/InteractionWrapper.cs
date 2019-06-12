using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Timers;
using WebSocketSharp;
using Newtonsoft.Json;
using XY = System.ValueTuple<byte, byte>;
using Timer = System.Timers.Timer;

namespace PixelPlanetUtils
{
    public class InteractionWrapper : IDisposable
    {
        private const byte subscribeOpcode = 0xA1;
        private const byte unsubscribeOpcode = 0xA2;
        private const byte subscribeManyOpcode = 0xA3;
        private const byte pixelUpdatedOpcode = 0xC1;

        public const string BaseHttpAdress = "https://pixelplanet.fun";
        private const string webSocketUrlTemplate = "wss://pixelplanet.fun/ws?fingerprint={0}";

        private readonly string fingerprint;

        private WebSocket webSocket;
        private readonly string wsUrl;
        private readonly Timer wsConnectionDelayTimer = new Timer(5000D);
        private readonly Gate websocketGate = new Gate();
        private readonly Gate listeningGate;
        private HashSet<XY> TrackedChunks = new HashSet<XY>();

        private readonly bool listeningMode;
        private bool multipleServerFails = false;
        private volatile bool disposed = false;
        private bool initialConnection = true;
        public event EventHandler<PixelChangedEventArgs> OnPixelChanged;
        public event EventHandler OnConnectionRestored;
        public event EventHandler OnConnectionLost;

        private readonly Action<string, MessageGroup> logger;

        public InteractionWrapper(string fingerprint, Action<string, MessageGroup> logger, bool listeningMode = false)
        {
            this.logger = logger;
            if (this.listeningMode = listeningMode)
            {
                listeningGate = new Gate();
            }
            this.fingerprint = fingerprint;
            wsUrl = string.Format(webSocketUrlTemplate, fingerprint);
            wsConnectionDelayTimer.Elapsed += ConnectionDelayTimer_Elapsed;
            try
            {
                using (HttpWebResponse response = SendJsonRequest("api/me", new { fingerprint }))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception(response.StatusDescription);
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Cannot connect to API. {e.Message}");
            }
            webSocket = new WebSocket(wsUrl);
            webSocket.Log.Output = (d, s) => { };
            webSocket.OnOpen += WebSocket_OnOpen;
            webSocket.OnMessage += WebSocket_OnMessage;
            webSocket.OnClose += WebSocket_OnClose;
            Connect();
        }

        public void SubscribeToUpdates(IEnumerable<XY> chunks)
        {
            websocketGate.WaitOpened();
            if (disposed)
            {
                return;
            }
            TrackedChunks.UnionWith(chunks);
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

        public void StartListening()
        {
            if (!listeningMode)
            {
                throw new InvalidOperationException();
            }
            listeningGate.Close();
            listeningGate.WaitOpened();
        }

        public void StopListening()
        {
            if (!listeningMode)
            {
                throw new InvalidOperationException();
            }
            listeningGate.Open();
        }

        public bool PlacePixel(int x, int y, PixelColor color, out double coolDown, out double totalCoolDown, out string error)
        {
            if (listeningMode)
            {
                throw new InvalidOperationException();
            }
            websocketGate.WaitOpened();
            if (disposed)
            {
                coolDown = -1;
                totalCoolDown = -1;
                error = "Connection is disposed";
                return false;
            }
            var data = new
            {
                a = x + y + 8,
                color = (byte)color,
                fingerprint,
                token = "null",
                x,
                y
            };
            try
            {
                using (HttpWebResponse response = SendJsonRequest("api/pixel", data))
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
                                            throw new Exception($"Server responded with errors:{errors}");
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
                        throw;
                    }
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.Forbidden:
                            throw new Exception("Action was forbidden by pixelworld; admins could have prevented you from placing pixel or area is protected");
                        case HttpStatusCode.BadGateway:
                            totalCoolDown = coolDown = multipleServerFails ? 30 : 10;
                            multipleServerFails = true;
                            error = $"Site is overloaded, delay {coolDown}s before next attempt";
                            return false;
                        case (HttpStatusCode)422:
                            error = null;
                            totalCoolDown = coolDown = 0;
                            return false;
                        default:
                            throw new Exception(response.StatusDescription);
                    }
                }
            }
        }

        public PixelColor[,] GetChunk(XY chunk)
        {
            string url = $"{BaseHttpAdress}/chunks/{chunk.Item1}/{chunk.Item2}.bin";
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

        private HttpWebResponse SendJsonRequest(string relativeUrl, object data)
        {
            HttpWebRequest request = WebRequest.CreateHttp($"{BaseHttpAdress}/{relativeUrl}");
            request.Method = "POST";
            using (Stream requestStream = request.GetRequestStream())
            {
                using (StreamWriter streamWriter = new StreamWriter(requestStream))
                {
                    string jsonText = JsonConvert.SerializeObject(data);
                    streamWriter.Write(jsonText);
                }
            }
            request.ContentType = "application/json";
            request.Headers["Origin"] = request.Referer = BaseHttpAdress;
            request.UserAgent = "Mozilla / 5.0(X11; Linux x86_64; rv: 57.0) Gecko / 20100101 Firefox / 57.0";
            return request.GetResponse() as HttpWebResponse;
        }

        private void Connect()
        {
            if (!webSocket.IsAlive)
            {
                logger?.Invoke("Connecting via websocket...", MessageGroup.TechState);
                webSocket.Connect();
            }
        }

        private void ConnectionDelayTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (webSocket.ReadyState == WebSocketState.Connecting ||
                webSocket.ReadyState == WebSocketState.Open)
            {
                wsConnectionDelayTimer.Stop();
            }
            else
            {
                Connect();
            }
        }

        private void WebSocket_OnClose(object sender, CloseEventArgs e)
        {
            OnConnectionLost?.Invoke(this, null);
            websocketGate.Close();
            logger?.Invoke("Websocket connection closed, trying to reconnect in 5s", MessageGroup.Error);
            wsConnectionDelayTimer.Start();
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
        }

        private void WebSocket_OnOpen(object sender, EventArgs e)
        {
            webSocket.OnError += WebSocket_OnError;
            websocketGate.Open();
            if (TrackedChunks.Count > 0)
            {
                SubscribeToUpdates(TrackedChunks);
            }
            logger?.Invoke("Listening for changes via websocket", MessageGroup.TechInfo);
            if (!initialConnection)
            {
                OnConnectionRestored?.Invoke(this, null);
            }
            initialConnection = false;
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
                wsConnectionDelayTimer.Dispose();
                OnPixelChanged = null;
                websocketGate.Dispose();
                if (listeningMode) 
                {
                    listeningGate.Dispose();
                }
            }
        }
    }
}
