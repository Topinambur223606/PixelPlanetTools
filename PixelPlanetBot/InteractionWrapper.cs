using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Timers;
using WebSocketSharp;
using XY = System.ValueTuple<byte, byte>;
using Timer = System.Timers.Timer;
using Newtonsoft.Json;

namespace PixelPlanetBot
{
    class InteractionWrapper : IDisposable
    {
        private const byte subscribeOpcode = 0xA1;
        private const byte unsubscribeOpcode = 0xA2;
        private const byte subscribeManyOpcode = 0xA3;
        private const byte pixelUpdatedOpcode = 0xC1;

        public const string BaseHttpAdress = "https://pixelplanet.fun";
        private const string webSocketUrlTemplate = "wss://pixelplanet.fun/ws?fingerprint={0}";

        private readonly string fingerprint;

        private readonly object interactionLock = new object();
        private readonly AutoResetEvent websocketIsOpen = new AutoResetEvent(false);
        private readonly AutoResetEvent websocketIsClosed = new AutoResetEvent(true);

        private WebSocket webSocket;
        private readonly string wsUrl;
        private readonly Timer wsConnectionDelayTimer = new Timer(5000D);
        private HashSet<XY> TrackedChunks = new HashSet<XY>();

        private bool multipleServerFails = false;
        private volatile bool disposed = false;
        private bool initialConnection = true;
        public event EventHandler<PixelChangedEventArgs> OnPixelChanged;
        public event EventHandler OnConnectionRestored;

        public InteractionWrapper(string fingerprint)
        {
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
            Thread waitThread = new Thread(WaitWebSocketThreadBody);
            Program.BackgroundThreads.Add(waitThread);
            waitThread.Start();
            webSocket = new WebSocket(wsUrl);
            webSocket.Log.Output = (d, s) => { };
            webSocket.OnOpen += WebSocket_OnOpen;
            webSocket.OnMessage += WebSocket_OnMessage;
            webSocket.OnClose += WebSocket_OnClose;
            Connect();
        }

        //idk where it can be used
        public void UnsubscribeFromUpdates(XY chunk)
        {
            lock (interactionLock)
            {
                if (disposed)
                {
                    return;
                }
            }

            if (TrackedChunks.Remove(chunk))
            {
                byte[] data = new byte[3]
                {
                    unsubscribeOpcode,
                    chunk.Item1,
                    chunk.Item2
                };
                webSocket.Send(data);
            }
        }

        //now unused
        public void SubscribeToChunkUpdates(XY chunk)
        {
            lock (interactionLock)
            {
                if (disposed)
                {
                    return;
                }
            }
            TrackedChunks.Add(chunk);
            byte[] data = new byte[3]
            {
                    subscribeOpcode,
                    chunk.Item1,
                    chunk.Item2
            };
            webSocket.Send(data);
        }

        public void SubscribeToUpdates(IEnumerable<XY> chunks)
        {
            lock (interactionLock)
            {
                if (disposed)
                {
                    return;
                }
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

        public bool PlacePixel(int x, int y, PixelColor color, out double coolDown, out string error)
        {
            lock (interactionLock)
            {
                if (disposed)
                {
                    coolDown = -1;
                    error = "Connection is disposed";
                    return false;
                }
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
                                            coolDown = double.Parse(json["waitSeconds"].ToString());
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
                            coolDown = multipleServerFails ? 30 : 10;
                            multipleServerFails = true;
                            error = $"Site is overloaded, delay {coolDown}s before next attempt";
                            return false;
                        case (HttpStatusCode)422:
                            error = null;
                            coolDown = 0;
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
                Program.LogLine("Connecting via websocket...", MessageGroup.State, ConsoleColor.Yellow);
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
            websocketIsClosed.Set();
            Program.LogLine("Websocket connection closed, trying to reconnect...", MessageGroup.Error, ConsoleColor.Red);
            wsConnectionDelayTimer.Start();
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            webSocket.OnError -= WebSocket_OnError;
            Program.LogLine($"Error on websocket: {e.Message}", MessageGroup.Error, ConsoleColor.Red);
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
                    Color = (PixelColor)color
                };
                OnPixelChanged?.Invoke(this, args);
            }
        }

        private void WebSocket_OnOpen(object sender, EventArgs e)
        {
            webSocket.OnError += WebSocket_OnError;
            websocketIsOpen.Set();
            websocketIsClosed.Reset();
            if (TrackedChunks.Count > 0)
            {
                SubscribeToUpdates(TrackedChunks);
            }
            Program.LogLine("Listening for changes via websocket", MessageGroup.State, ConsoleColor.Blue);
            if (!initialConnection)
            {
                OnConnectionRestored?.Invoke(this, null);
            }
            initialConnection = false;
        }

        private void WaitWebSocketThreadBody()
        {
            while (!disposed)
            {
                try
                {
                    websocketIsClosed.WaitOne();
                }
                catch (ThreadInterruptedException)
                {
                    return;
                }
                if (disposed)
                {
                    return;
                }
                lock (interactionLock)
                {
                    if (disposed)
                    {
                        return;
                    }
                    try
                    {
                        websocketIsOpen.WaitOne();
                    }
                    catch (ThreadInterruptedException)
                    {
                        return;
                    }
                }
            }
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
                websocketIsClosed.Set();
                websocketIsClosed.Dispose();
                websocketIsOpen.Set();
                websocketIsOpen.Dispose();
            }
        }
    }
}
