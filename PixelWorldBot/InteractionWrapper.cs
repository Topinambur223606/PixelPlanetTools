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

namespace PixelWorldBot
{
    class InteractionWrapper
    {
        private const byte trackChunkOpcode = 0xA1;

        private const byte pixelUpdatedOpcode = 0xC1;

        private const string baseHttpAdress = "https://pixelplanet.fun";

        private const string webSocketUrlTemplate = "wss://pixelplanet.fun/ws?fingerprint={0}";

        private readonly string wsUrl;

        private readonly string fingerprint;

        private bool isClosing = false;

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

        public void Close()
        {
            isClosing = true;
            Disconnect();
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
                Console.WriteLine("Waiting for reconnect...");
                Task.Delay(6000).Wait();
            }
            webSocket.Send(data);
        }

        public double PlacePixel(int x, int y, PixelColor color)
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
                                Console.WriteLine("Placed pixel: {0} at ({1};{2})", color, x, y);
                                return double.Parse(json["coolDownSeconds"].ToString());
                            }
                            else 
                            {
                                Console.WriteLine("Failed to place pixel");
                                if (json["errors"].Count() > 0)
                                {
                                    string errors = string.Concat(json["errors"].Select(e => $"{Environment.NewLine}\"{e}\""));
                                    throw new Exception($"Server responded with errors:{errors}");
                                }
                                else
                                {
                                    if (double.TryParse(json["waitSeconds"].ToString(), out double cd))
                                    {
                                        return cd;
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
            Console.WriteLine("Connecting...");
            if (webSocket != null)
            {
                Disconnect();
            }
            webSocket = new WebSocket(wsUrl);
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
            if (!isClosing)
            {
                Console.WriteLine("Connection closed, trying to reconnect...");
                connectionDelayTimer.Start();
            }
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            Console.WriteLine("Error on WebSocket: " + e.Message);
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
                Console.WriteLine("Received pixel update: {0} at ({1};{2})", args.Color,
                    PixelMap.ConvertToAbsolute(chunkX, relativeX),
                    PixelMap.ConvertToAbsolute(chunkY, relativeY));
                OnPixelChanged?.Invoke(this, args);
            }
        }

        private void WebSocket_OnOpen(object sender, EventArgs e)
        {
            Console.WriteLine("Starting listening for changes via websocket");
            foreach (XY chunk in TrackedChunks)
            {
                TrackChunk(chunk);
            }
        }
    }
}
