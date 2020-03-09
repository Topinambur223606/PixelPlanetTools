using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PixelPlanetUtils.CanvasInteraction;
using PixelPlanetUtils.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.NetworkInteraction
{
    public static class HttpWrapper
    {
        private static bool multiplePlacingFails = false;

        public static WebProxy Proxy { get; set; }
        public static Logger Logger { get; set; }

        public static void ConnectToApi()
        {
            while (true)
            {
                try
                {
                    Logger.Log("Connecting to API...", MessageGroup.TechState);
                    using (HttpWebResponse response = SendRequest("api/me"))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            throw new Exception($"Error: {response.StatusDescription}");
                        }
                        Logger.Log("API is reachable", MessageGroup.TechInfo);
                        break;
                    }
                }
                catch (WebException ex)
                {
                    using (HttpWebResponse response = ex.Response as HttpWebResponse)
                    {
                        if (response == null)
                        {
                            Logger.Log("Cannot connect: internet connection is slow or not available", MessageGroup.Error);
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
        }

        public static bool PlacePixel(int x, int y, PixelColor color, out double coolDown, out double totalCoolDown, out string error)
        {
            PixelPlacingData data = new PixelPlacingData
            {
                Canvas = byte.MinValue,
                Color = color,
                AbsoluteX = x,
                AbsoluteY = y
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
                                        multiplePlacingFails = false;
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
                                            multiplePlacingFails = false;
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
                            totalCoolDown = coolDown = multiplePlacingFails ? 30 : 10;
                            multiplePlacingFails = true;
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

        public static PixelColor[,] GetChunk(XY chunk)
        {
            string url = $"{UrlManager.BaseHttpAdress}/chunks/0/{chunk.Item1}/{chunk.Item2}.bmp";
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

        private static HttpWebResponse SendRequest(string relativeUrl, object data = null, int timeout = 5000)
        {
            HttpWebRequest request = WebRequest.CreateHttp($"{UrlManager.BaseHttpAdress}/{relativeUrl}");
            request.Timeout = timeout;
            request.Proxy = Proxy;
            request.Headers["Origin"] = request.Referer = UrlManager.BaseHttpAdress;
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
    }
}
