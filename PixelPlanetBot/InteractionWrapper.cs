using System;
using System.Collections.Generic;
using System.Net;
using WebSocketSharp;
using System.Timers;
using System.IO;
using System.Web.Script.Serialization;
using XY = System.ValueTuple<byte, byte>;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
    class InteractionWrapper : IDisposable
    {
        private const byte trackChunkOpcode = 0xA1;

        private const byte pixelUpdatedOpcode = 0xC1;

        private const string baseHttpAdress = "https://pixelplanet.fun";

        private const string webSocketUrlTemplate = "wss://pixelplanet.fun/ws?fingerprint={0}";

        private readonly string wsUrl;

        private readonly string fingerprint;

        private bool disposed = false;

        private WebSocket webSocket;

        private readonly Timer connectionDelayTimer;

        private HashSet<XY> TrackedChunks { get; set; }

        public event EventHandler<PixelChangedEventArgs> OnPixelChanged;

        public InteractionWrapper(string fingerprint)
        {
            connectionDelayTimer = new Timer(5000D);
            this.fingerprint = fingerprint;
            wsUrl = string.Format(webSocketUrlTemplate, fingerprint);
            connectionDelayTimer.Elapsed += ConnectionDelayTimer_Elapsed;
            TrackedChunks = new HashSet<XY>();
            HttpWebRequest request = BuildJsonRequest("api/me", new { fingerprint });
            HttpWebResponse response = request.GetResponse() as HttpWebResponse;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Cannot connect");
            }
            Connect();
        }

        public void TrackChunk(XY chunk)
        {
            TrackedChunks.Add(chunk);
            byte[] data = new byte[3]
            {
                trackChunkOpcode,
                chunk.Item1,
                chunk.Item2
            };
            while (webSocket?.ReadyState != WebSocketState.Open)
            {
                connectionDelayTimer.Start();
                Program.LogLineToConsole("Waiting for reconnect...", ConsoleColor.Yellow);
                Task.Delay(6000).Wait();
            }
            webSocket.Send(data);
        }

        public bool PlacePixel(int x, int y, PixelColor color, out double coolDown)
        {
            var data = new
            {
                x,
                y,
                a = x + y - 8,
                color = (byte)color,
                fingerprint,
                token = "null"
            };
            HttpWebRequest request = BuildJsonRequest("api/pixel", data);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
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
                                int cd = int.Parse(json["coolDownSeconds"].ToString());
                                string prefix = cd == 4 ? "P" : "Rep";
                                Program.LogPixelToConsole($"{prefix}laced pixel:", x, y, color, ConsoleColor.Green);
                                coolDown = cd;
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
                                    if (double.TryParse(json["waitSeconds"].ToString(), out double cd))
                                    {
                                        Program.LogLineToConsole($"Failed to place pixel, IP is overused; cooldown is {cd}", ConsoleColor.Red);
                                        coolDown = cd;
                                        return false;
                                    }
                                    else
                                    {
                                        throw new Exception("Unknown error reported from server");
                                    }
                                }
                            }
                        }
                    }
                case HttpStatusCode.Forbidden:
                    throw new Exception("Action was forbidden by pixelworld itself");
                default:
                    throw new Exception(response.StatusDescription);
            }
        }

        public PixelColor[,] GetChunk(XY chunk)
        {
            string url = $"{baseHttpAdress}/chunks/{chunk.Item1}/{chunk.Item2}.bin";
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

        private static HttpWebRequest BuildJsonRequest(string relativeUrl, object data)
        {
            HttpWebRequest request = WebRequest.CreateHttp($"{baseHttpAdress}/{relativeUrl}");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers["Origin"] = request.Referer = baseHttpAdress;
            request.UserAgent = "Mozilla / 5.0(X11; Linux x86_64; rv: 57.0) Gecko / 20100101 Firefox / 57.0";
            using (StreamWriter streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string text = serializer.Serialize(data);
                streamWriter.Write(text);
            }
            return request;
        }

        private void Connect()
        {
            Program.LogLineToConsole("Connecting...", ConsoleColor.Yellow);
            if (webSocket != null)
            {
                Disconnect();
            }
            webSocket = new WebSocket(wsUrl);
            webSocket.Log.Output = (d, s) => { };
            webSocket.OnOpen += WebSocket_OnOpen;
            webSocket.OnMessage += WebSocket_OnMessage;
            webSocket.OnError += WebSocket_OnError;
            webSocket.OnClose += WebSocket_OnClose;
            webSocket.Connect();
        }

        private void Disconnect()
        {
            webSocket.OnOpen -= WebSocket_OnOpen;
            webSocket.OnMessage -= WebSocket_OnMessage;
            webSocket.OnError -= WebSocket_OnError;
            webSocket.OnClose -= WebSocket_OnClose;
            (webSocket as IDisposable).Dispose();
        }

        private void ConnectionDelayTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (webSocket.ReadyState != WebSocketState.Connecting &&
                webSocket.ReadyState != WebSocketState.Open)
            {
                Connect();
            }
            else
            {
                connectionDelayTimer.Stop();
            }
        }

        private void WebSocket_OnClose(object sender, CloseEventArgs e)
        {
            Program.LogLineToConsole("Connection closed, trying to reconnect...", ConsoleColor.Red);
            connectionDelayTimer.Start();
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            Program.LogLineToConsole("Error on WebSocket: \n" + e.Message, ConsoleColor.Red);
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
            Program.LogLineToConsole("Starting listening for changes via websocket", ConsoleColor.Green);
            foreach (XY chunk in TrackedChunks)
            {
                TrackChunk(chunk);
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Disconnect();
                connectionDelayTimer.Dispose();
                OnPixelChanged = null;
            }
            disposed = true;
        }
    }
}
