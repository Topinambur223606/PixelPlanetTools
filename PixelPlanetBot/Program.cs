using CommandLine;
using PixelPlanetUtils;
using PixelPlanetUtils.CanvasInteraction;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    static partial class Program
    {
        private static Thread statsThread;
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();
        private static bool defenseMode;
        private static CaptchaNotificationMode notificationMode;
        private static PlacingOrderMode placingOrder;
        private static ChunkCache cache;
        private static bool repeatingFails = false;

        private static AutoResetEvent gotGriefed;
        private static ManualResetEvent mapUpdatedResetEvent;

        private static object waitingGriefLock;

        private static string imagePath;
        private static PixelColor[,] imagePixels;
        private static IEnumerable<Pixel> pixelsToBuild;
        private static short leftX, topY;
        private static ushort width, height;
        private static WebProxy proxy;
        private static bool disableUpdates;

        private static readonly HashSet<Pixel> placed = new HashSet<Pixel>();

        private static volatile int builtInLastMinute = 0;
        private static volatile int griefedInLastMinute = 0;
        private static readonly Queue<int> builtInPast = new Queue<int>();
        private static readonly Queue<int> griefedInPast = new Queue<int>();
        private static readonly Queue<int> doneInPast = new Queue<int>();
        private static Logger logger;
        private static string logFilePath = null;

        private static void Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args))
                {
                    return;
                }

                if (!disableUpdates)
                {
                    if (CheckForUpdates())
                    {
                        return;
                    }
                }

                

                logger = new Logger(finishCTS.Token, logFilePath);
                HttpWrapper.Logger = logger;
                try
                {
                    imagePixels = ImageProcessing.PixelColorsByUri(imagePath, logger);
                }
                catch (WebException)
                {
                    logger.LogError("Cannot download image");
                    return;
                }
                catch (ArgumentException)
                {
                    logger.LogError("Cannot convert image");
                    return;
                }

                try
                {
                    checked
                    {
                        width = (ushort)imagePixels.GetLength(0);
                        height = (ushort)imagePixels.GetLength(1);
                        short check;
                        check = (short)(leftX + width);
                        check = (short)(topY + height);
                    }

                }
                catch (OverflowException)
                {
                    logger.LogError("Entire image should be inside the map");
                    return;
                }



                logger.LogTechState("Calculating pixel placing order...");
                CalculatePixelOrder();
                logger.LogTechInfo("Pixel placing order is calculated");

                cache = new ChunkCache(pixelsToBuild, logger);
                mapUpdatedResetEvent = new ManualResetEvent(false);
                cache.OnMapUpdated += (o, e) => mapUpdatedResetEvent.Set();
                if (defenseMode)
                {
                    gotGriefed = new AutoResetEvent(false);
                    cache.OnMapUpdated += (o, e) => gotGriefed.Set();
                    waitingGriefLock = new object();
                }
                statsThread = new Thread(StatsCollectionThreadBody);
                statsThread.Start();
                do
                {
                    try
                    {
                        HttpWrapper.ConnectToApi();
                        MainWorkingBody();
                        return;
                    }
                    catch (PausingException ex)
                    {
                        logger.LogError($"Unhandled exception: {ex.Message}");
                        logger.LogInfo($"Check that problem is resolved and press any key to continue");
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        Console.ReadKey(true);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Unhandled exception: {ex.Message}");
                        int delay = repeatingFails ? 30 : 10;
                        repeatingFails = true;
                        logger.LogTechState($"Reconnecting in {delay} seconds...");
                        Thread.Sleep(TimeSpan.FromSeconds(delay));
                        continue;
                    }
                } while (true);
            }
            finally
            {
                logger?.Log("Exiting...", MessageGroup.Info);
                finishCTS.Cancel();
                Thread.Sleep(500);
                gotGriefed?.Dispose();
                mapUpdatedResetEvent?.Dispose();
                logger?.Dispose();
                finishCTS.Dispose();
                Environment.Exit(0);
            }
        }

        private static void MainWorkingBody()
        {
            using (WebsocketWrapper wrapper = new WebsocketWrapper(logger, false))
            {
                wrapper.OnConnectionLost += (o, e) => mapUpdatedResetEvent.Reset();
                cache.Wrapper = wrapper;
                cache.DownloadChunks();
                wrapper.OnPixelChanged += LogPixelChanged;
                placed.Clear();
                bool wasChanged;
                do
                {
                    wasChanged = false;
                    repeatingFails = false;
                    foreach (Pixel pixel in pixelsToBuild)
                    {
                        mapUpdatedResetEvent.WaitOne();
                        (short x, short y, PixelColor color) = pixel;
                        PixelColor actualColor = cache.GetPixelColor(x, y);
                        if (!IsCorrectPixelColor(actualColor, color))
                        {
                            wasChanged = true;
                            bool success;
                            placed.Add(pixel);
                            do
                            {
                                wrapper.WaitWebsocketConnected();
                                success = HttpWrapper.PlacePixel(x, y, color, out double cd, out double totalCd, out string error);
                                if (success)
                                {
                                    logger.LogPixel($"{(cd == 4 ? "P" : "Rep")}laced pixel:", DateTime.Now, MessageGroup.Pixel, x, y, color);
                                    Thread.Sleep(TimeSpan.FromSeconds(totalCd < 53 ? 1 : cd));
                                }
                                else
                                {
                                    if (error == "captcha")
                                    {
                                        ProcessCaptcha();
                                    }
                                    else
                                    {
                                        logger.Log($"Failed to place pixel: {error}", MessageGroup.PixelFail);
                                    }
                                    Thread.Sleep(TimeSpan.FromSeconds(cd));
                                }
                            } while (!success);
                        }
                    }
                    if (defenseMode)
                    {
                        if (!wasChanged)
                        {

                            logger.Log("Image is intact, waiting...", MessageGroup.Info);
                            lock (waitingGriefLock)
                            {
                                gotGriefed.Reset();
                                gotGriefed.WaitOne();
                            }
                            Thread.Sleep(new Random().Next(500, 3000));
                        }
                    }
                } while (defenseMode || wasChanged);
                logger.Log("Building is finished", MessageGroup.Info);
                return;
            }
        }

        private static void ProcessCaptcha()
        {
            logger.LogAndPause("Please go to browser and place pixel, then return and press any key", MessageGroup.Captcha);
            CancellationTokenSource captchaCts = null;
            if (notificationMode.HasFlag(CaptchaNotificationMode.Sound))
            {
                captchaCts = new CancellationTokenSource();
                new Thread(() =>
                {
                    CancellationToken token = captchaCts.Token;
                    while (!token.IsCancellationRequested)
                    {
                        for (int j = 0; j < 7; j++)
                        {
                            Console.Beep(1000, 100);
                        }
                        try
                        {
                            Task.Delay(TimeSpan.FromMinutes(1), token).Wait();
                        }
                        catch (AggregateException e) when (e.InnerException is TaskCanceledException)
                        {
                            return;
                        }
                        catch (TaskCanceledException)
                        {
                            return;
                        }
                    }
                }).Start();
            }
            if (notificationMode.HasFlag(CaptchaNotificationMode.Browser))
            {
                Process.Start($"https://{UrlManager.BaseUrl}");
            }
            while (Console.KeyAvailable)
            {
                Console.ReadKey(true);
            }
            Console.ReadKey(true);
            logger.ResumeLogging();
            captchaCts?.Cancel();
            captchaCts?.Dispose();
        }

        private static void CalculatePixelOrder()
        {
            IEnumerable<Pixel> relativePixelsToBuild;
            IEnumerable<int> allY = Enumerable.Range(0, height);
            IEnumerable<int> allX = Enumerable.Range(0, width);
            Pixel[] nonEmptyPixels = allX.
                SelectMany(X => allY.Select(Y =>
                    ((short)X, (short)Y, C: imagePixels[X, Y]))).
                Where(xyc => xyc.C != PixelColor.None).ToArray();
            switch (placingOrder)
            {
                case PlacingOrderMode.Left:
                    relativePixelsToBuild = nonEmptyPixels.OrderBy(xy => xy.Item1).ThenBy(e => Guid.NewGuid());
                    break;
                case PlacingOrderMode.Right:
                    relativePixelsToBuild = nonEmptyPixels.OrderByDescending(xy => xy.Item1).ThenBy(e => Guid.NewGuid());
                    break;
                case PlacingOrderMode.Top:
                    relativePixelsToBuild = nonEmptyPixels.OrderBy(xy => xy.Item2).ThenBy(e => Guid.NewGuid());
                    break;
                case PlacingOrderMode.Bottom:
                    relativePixelsToBuild = nonEmptyPixels.OrderByDescending(xy => xy.Item2).ThenBy(e => Guid.NewGuid());
                    break;
                case PlacingOrderMode.Outline:
                    Random rnd = new Random();
                    relativePixelsToBuild = nonEmptyPixels.AsParallel().OrderByDescending(p =>
                    {
                        const int radius = 3;
                        double score = rnd.NextDouble() * 125D;
                        (short x, short y, PixelColor c) = p;
                        for (int i = -radius; i <= radius; i++)
                        {
                            for (int j = -radius; j <= radius; j++)
                            {
                                int ox = x + i;
                                int oy = y + j;
                                double dist = Math.Sqrt(i * i + j * j);
                                if (ox >= 0 && oy >= 0 && ox < width && oy < height)
                                {
                                    PixelColor c2 = imagePixels[x + i, y + j];
                                    if (c2 == PixelColor.None)
                                    {
                                        score += ImageProcessing.NoneColorDistance / dist;
                                    }
                                    else if (c != c2)
                                    {

                                        score += ImageProcessing.RgbCubeDistance(c, c2) / dist;
                                    }
                                }
                                else
                                {
                                    score += ImageProcessing.NoneColorDistance / dist;
                                }
                            }
                        }
                        return score;
                    });
                    break;
                default:
                    Random rand = new Random();
                    for (int i = 0; i < nonEmptyPixels.Length; i++)
                    {
                        int r = rand.Next(i, nonEmptyPixels.Length);
                        Pixel tmp = nonEmptyPixels[r];
                        nonEmptyPixels[r] = nonEmptyPixels[i];
                        nonEmptyPixels[i] = tmp;
                    }
                    relativePixelsToBuild = nonEmptyPixels;
                    break;
            }
            pixelsToBuild = relativePixelsToBuild.Select(p => ((short)(p.Item1 + leftX), (short)(p.Item2 + topY), p.Item3)).ToList();
        }

        private static bool ParseArguments(string[] args)
        {
            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<Options>(args)
                    .WithNotParsed(e => success = false)
                    .WithParsed(o =>
                    {
                        leftX = o.LeftX;
                        topY = o.TopY;

                        notificationMode = o.CaptchaNotificationMode;
                        defenseMode = o.DefenseMode;
                        placingOrder = o.PlacingOrderMode;
                        if (!string.IsNullOrWhiteSpace(o.Proxy))
                        {
                            proxy = new WebProxy(o.Proxy);
                            HttpWrapper.Proxy = proxy;
                            if (notificationMode.HasFlag(CaptchaNotificationMode.Browser))
                            {
                                Console.WriteLine($"Warning: proxy usage in browser notification mode is detected{Environment.NewLine}" +
                                    "Ensure that same proxy settings are set in your default browser");
                            }
                        }
                        logFilePath = o.LogFilePath;
                        imagePath = o.ImagePath;
                        disableUpdates = o.DisableUpdates;
                        if (o.UseMirror)
                        {
                            if (o.ServerUrl != null)
                            {
                                Console.WriteLine("Invalid args: mirror usage and custom server address are specified");
                                success = false;
                                return;
                            }
                            UrlManager.MirrorMode = true;
                        }
                        if (o.ServerUrl != null)
                        {
                            UrlManager.BaseUrl = o.ServerUrl;
                        }
                    });
                return success;
            }
        }

        private static bool IsCorrectPixelColor(PixelColor actualColor, PixelColor desiredColor)
        {
            return (actualColor == desiredColor) ||
                    (actualColor == PixelColor.UnsetOcean && desiredColor == PixelColor.SkyBlue) ||
                    (actualColor == PixelColor.UnsetLand && desiredColor == PixelColor.White);
        }

        private static void LogPixelChanged(object sender, PixelChangedEventArgs e)
        {
            MessageGroup msgGroup;
            short x = PixelMap.ConvertToAbsolute(e.Chunk.Item1, e.Pixel.Item1);
            short y = PixelMap.ConvertToAbsolute(e.Chunk.Item2, e.Pixel.Item2);

            if (!placed.Remove((x, y, e.Color)))
            {
                try
                {
                    PixelColor desiredColor = imagePixels[x - leftX, y - topY];
                    if (desiredColor == PixelColor.None)
                    {
                        msgGroup = MessageGroup.PixelInfo;
                    }
                    else
                    {
                        if (desiredColor == e.Color)
                        {
                            msgGroup = MessageGroup.Assist;
                            builtInLastMinute++;
                        }
                        else
                        {
                            msgGroup = MessageGroup.Attack;
                            griefedInLastMinute++;
                            gotGriefed?.Set();
                        }
                    }
                }
                catch
                {
                    msgGroup = MessageGroup.PixelInfo;
                }
                logger.LogPixel($"Received pixel update:", e.DateTime, msgGroup, x, y, e.Color);
            }
            else
            {
                builtInLastMinute++;
            }
        }

        private static bool CheckForUpdates()
        {
            using (UpdateChecker checker = new UpdateChecker())
            {
                if (checker.NeedsToCheckUpdates())
                {
                    Console.WriteLine("Checking for updates...");
                    if (checker.UpdateIsAvailable(out string version, out bool isCompatible))
                    {
                        Console.WriteLine($"Update is available: {version} (current version is {App.Version})");
                        if (isCompatible)
                        {
                            Console.WriteLine("New version is backwards compatible, it will be relaunched with same arguments");
                        }
                        else
                        {
                            Console.WriteLine("Argument list or order was changed, bot should be relaunched manually after update");
                        }
                        Console.WriteLine("Press Enter to update, anything else to skip");
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            checker.StartUpdate();
                            return true;
                        }
                    }
                    else
                    {
                        if (version == null)
                        {
                            Console.WriteLine("Cannot check for updates");
                        }
                    }
                }
            }
            return false;
        }

        private static void StatsCollectionThreadBody()
        {
            mapUpdatedResetEvent.WaitOne();
            Task taskToWait = Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token);
            int done = pixelsToBuild.
                  Where(p => IsCorrectPixelColor(cache.GetPixelColor(p.Item1, p.Item2), p.Item3)).
                  Count();
            doneInPast.Enqueue(done);
            do
            {
                if (finishCTS.IsCancellationRequested)
                {
                    return;
                }
                try
                {
                    taskToWait.Wait();
                }
                catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
                {
                    return;
                }
                if (finishCTS.IsCancellationRequested)
                {
                    return;
                }
                builtInPast.Enqueue(builtInLastMinute);
                builtInLastMinute = 0;
                if (builtInPast.Count > 5)
                {
                    builtInPast.Dequeue();
                }

                griefedInPast.Enqueue(griefedInLastMinute);
                griefedInLastMinute = 0;
                if (griefedInPast.Count > 5)
                {
                    griefedInPast.Dequeue();
                }

                double griefedPerMinute = griefedInPast.Average();
                double builtPerMinute = builtInPast.Average();

                done = pixelsToBuild.
                   Where(p => IsCorrectPixelColor(cache.GetPixelColor(p.Item1, p.Item2), p.Item3)).
                   Count();

                double buildSpeed = (done - doneInPast.First()) / ((double)doneInPast.Count);

                doneInPast.Enqueue(done);
                if (doneInPast.Count > 5)
                {
                    doneInPast.Dequeue();
                }

                int total = pixelsToBuild.Count();
                double percent = Math.Floor(done * 1000.0 / total) / 10.0;
                DateTime time = DateTime.Now;
                if (finishCTS.IsCancellationRequested)
                {
                    return;
                }
                if (defenseMode)
                {
                    logger.Log($"Image integrity is {percent:F1}%, {total - done} corrupted pixels", MessageGroup.Info, time);
                    lock (waitingGriefLock)
                    { }
                }
                else
                {
                    string info = $"Image is {percent:F1}% complete, ";
                    if (buildSpeed > 0)
                    {
                        int minsLeft = (int)Math.Ceiling((total - done) / buildSpeed);
                        int hrsLeft = minsLeft / 60;
                        info += $"will be built approximately in {hrsLeft}h {minsLeft % 60}min";
                    }
                    else
                    {
                        info += $"no progress in last 5 minutes";
                    }
                    logger.Log(info, MessageGroup.Info, time);
                }
                if (griefedPerMinute > 1)
                {
                    logger.Log($"Image is under attack at the moment, {done} pixels are good now", MessageGroup.Info, time);
                    logger.Log($"Building {builtPerMinute:F1} px/min, getting griefed {griefedPerMinute:F1} px/min", MessageGroup.Info, time);
                }
                taskToWait = Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token);
            } while (true);
        }
    }
}
