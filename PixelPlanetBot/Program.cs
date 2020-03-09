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
        private static CancellationTokenSource captchaCts;

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

                logger = new Logger(logFilePath, finishCTS.Token);
                logger.LogDebug("Command line: " + Environment.CommandLine);
                HttpWrapper.Logger = logger;
                try
                {
                    imagePixels = ImageProcessing.PixelColorsByUri(imagePath, logger);
                }
                catch (WebException ex)
                {
                    logger.LogError("Cannot download image");
                    logger.LogDebug($"Error: {ex.Message}");
                    return;
                }
                catch (ArgumentException ex)
                {
                    logger.LogError("Cannot convert image");
                    logger.LogDebug($"Error: {ex.Message}");
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
                catch (OverflowException ex)
                {
                    logger.LogError("Entire image should be inside the map");
                    logger.LogDebug($"Error: {ex.Message}");
                    return;
                }

                logger.LogTechState("Calculating pixel placing order...");
                CalculatePixelOrder();
                logger.LogTechInfo("Pixel placing order is calculated");

                cache = new ChunkCache(pixelsToBuild, logger);
                mapUpdatedResetEvent = new ManualResetEvent(false);
                cache.OnMapUpdated += (o, e) =>
                {
                    logger.LogDebug("Map updated event received");
                    mapUpdatedResetEvent.Set();
                };
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
                logger?.LogInfo("Exiting...");
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
                logger.LogDebug("MainWorkingBody(): downloading chunks");
                cache.DownloadChunks();
                wrapper.OnPixelChanged += LogPixelChanged;
                placed.Clear();
                bool wasChanged;
                do
                {
                    logger.LogDebug("MainWorkingBody(): main body cycle started");
                    wasChanged = false;
                    repeatingFails = false;
                    foreach (Pixel pixel in pixelsToBuild)
                    {
                        mapUpdatedResetEvent.WaitOne();
                        (short x, short y, PixelColor color) = pixel;
                        PixelColor actualColor = cache.GetPixelColor(x, y);
                        if (!IsCorrectPixelColor(actualColor, color))
                        {
                            logger.LogDebug($"MainWorkingBody(): {pixel} - wrong color ({actualColor})");
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
                                    logger.LogDebug($"MainWorkingBody(): pixel placing handled error {error}");
                                    if (error == "captcha")
                                    {
                                        ProcessCaptcha();
                                    }
                                    else
                                    {
                                        logger.Log($"Failed to place pixel: {error}", MessageGroup.PixelFail);
                                    }
                                    logger.LogDebug($"MainWorkingBody(): sleep {cd:F2} seconds");
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
                                logger.LogDebug("MainWorkingBody(): acquiring grief waiting lock");
                                gotGriefed.Reset();
                                gotGriefed.WaitOne();
                            }
                            logger.LogDebug("MainWorkingBody(): got griefed");
                            Thread.Sleep(new Random().Next(500, 3000));
                        }
                    }
                } while (defenseMode || wasChanged);
                logger.Log("Building is finished", MessageGroup.Info);
                return;
            }
        }

        private static void BeepThreadBody()
        {
            logger.LogDebug("BeepThreadBody() started");
            CancellationToken token = captchaCts.Token;
            while (!token.IsCancellationRequested)
            {
                logger.LogDebug("BeepThreadBody(): beeping");
                for (int j = 0; j < 7; j++)
                {
                    Console.Beep(1000, 100);
                }
                try
                {
                    Task.Delay(TimeSpan.FromMinutes(1), token).Wait();
                }
                catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
                {
                    logger.LogDebug("BeepThreadBody(): canceled (1)");
                    return;
                }
                catch (TaskCanceledException)
                {
                    logger.LogDebug("BeepThreadBody(): canceled (2)");
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogDebug($"BeepThreadBody(): unhandled exception {ex.GetBaseException().Message}");
                }
            }
        }

        private static void ProcessCaptcha()
        {
            logger.LogAndPause("Please go to browser and place pixel, then return and press any key", MessageGroup.Captcha);
            captchaCts = null;
            if (notificationMode.HasFlag(CaptchaNotificationMode.Sound))
            {
                logger.LogDebug("ProcessCaptcha(): starting beep thread");
                captchaCts = new CancellationTokenSource();
                new Thread(BeepThreadBody).Start();
            }
            if (notificationMode.HasFlag(CaptchaNotificationMode.Browser))
            {
                logger.LogDebug("ProcessCaptcha(): starting browser");
                Process.Start(UrlManager.BaseHttpAdress);
            }
            while (Console.KeyAvailable)
            {
                Console.ReadKey(true);
            }
            Console.ReadKey(true);
            logger.LogDebug("ProcessCaptcha(): got keypress");
            logger.ResumeLogging();
            captchaCts?.Cancel();
            captchaCts?.Dispose();
        }

        private static void CalculatePixelOrder()
        {
            IEnumerable<Pixel> relativePixelsToBuild;
            IEnumerable<int> allY = Enumerable.Range(0, height);
            IEnumerable<int> allX = Enumerable.Range(0, width);
            IList<Pixel> nonEmptyPixels = allX.
                SelectMany(X => allY.Select(Y =>
                    ((short)X, (short)Y, C: imagePixels[X, Y]))).
                Where(xyc => xyc.C != PixelColor.None).ToList();
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
                    for (int i = 0; i < nonEmptyPixels.Count; i++)
                    {
                        int r = rand.Next(i, nonEmptyPixels.Count);
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
                catch (IndexOutOfRangeException)
                {
                    logger.LogDebug("LogPixelChanged(): pixel update beyond rectangle");
                    msgGroup = MessageGroup.PixelInfo;
                }
                catch (Exception ex)
                {
                    logger.LogDebug($"LogPixelChanged(): unhandled exception - {ex.Message}");
                    msgGroup = MessageGroup.PixelInfo;
                }
                logger.LogPixel($"Received pixel update:", e.DateTime, msgGroup, x, y, e.Color);
            }
            else
            {
                logger.LogDebug($"LogPixelChanged(): self-placed pixel");
                builtInLastMinute++;
            }
        }

        private static bool CheckForUpdates()
        {
            using (UpdateChecker checker = new UpdateChecker(logger))
            {
                if (checker.NeedsToCheckUpdates())
                {
                    logger.Log("Checking for updates...", MessageGroup.Update);
                    if (checker.UpdateIsAvailable(out string version, out bool isCompatible))
                    {
                        logger.Log($"Update is available: {version} (current version is {App.Version})", MessageGroup.Update);
                        if (isCompatible)
                        {
                            logger.Log("New version is backwards compatible, it will be relaunched with same arguments", MessageGroup.Update);
                        }
                        else
                        {
                            logger.Log("Argument list was changed, check it and relaunch bot manually after update", MessageGroup.Update);
                        }
                        logger.Log("Press Enter to update, anything else to skip", MessageGroup.Update);
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            logger.Log("Starting update...", MessageGroup.Update);
                            checker.StartUpdate();
                            return true;
                        }
                    }
                    else
                    {
                        if (version == null)
                        {
                            logger.LogError("Cannot check for updates");
                        }
                    }
                }
            }
            return false;
        }

        private static void StatsCollectionThreadBody()
        {
            int CountDone() => pixelsToBuild.
                   Where(p => IsCorrectPixelColor(cache.GetPixelColor(p.Item1, p.Item2), p.Item3)).
                   Count();

            void AddToQueue(Queue<int> queue, int value)
            {
                const int maxCount = 5;
                queue.Enqueue(value);
                if (queue.Count > maxCount)
                {
                    queue.Dequeue();
                }
            }

            Task GetDelayTask() => Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token);

            logger.LogDebug("Stats collection thread started");
            int total = pixelsToBuild.Count();
            mapUpdatedResetEvent.WaitOne();
            logger.LogDebug("Map updated, stats collection started");
            Task taskToWait = GetDelayTask();
            int done = CountDone();
            logger.LogDebug($"{done} pixels are done at start");
            doneInPast.Enqueue(done);
            do
            {
                if (finishCTS.IsCancellationRequested)
                {
                    logger.LogDebug($"cancellation requested (S1), finishing");
                    return;
                }
                try
                {
                    logger.LogDebug($"waiting");
                    taskToWait.Wait();
                }
                catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
                {
                    logger.LogDebug($"cancellation requested (S2), finishing");
                    return;
                }
                if (finishCTS.IsCancellationRequested)
                {
                    logger.LogDebug($"cancellation requested (S3), finishing");
                    return;
                }

                AddToQueue(builtInPast, builtInLastMinute);
                builtInLastMinute = 0;
                AddToQueue(griefedInPast, griefedInLastMinute);
                griefedInLastMinute = 0;
                done = CountDone(); //time consuming => cancellation check again later
                double buildSpeed = (done - doneInPast.First()) / ((double)doneInPast.Count);
                AddToQueue(doneInPast, done);
                logger.LogDebug($"last minute: {builtInPast} built, {griefedInPast} griefed; {done} total done");

                double griefedPerMinute = griefedInPast.Average();
                double builtPerMinute = builtInPast.Average();
                double percent = Math.Floor(done * 1000D / total) / 10D;
                DateTime time = DateTime.Now;

                if (finishCTS.IsCancellationRequested)
                {
                    logger.LogDebug($"cancellation requested (S4), finishing");
                    return;
                }
                if (defenseMode)
                {
                    logger.Log($"Image integrity is {percent:F1}%, {total - done} corrupted pixels", MessageGroup.Info, time);
                    logger.LogDebug($"grief lock start");
                    lock (waitingGriefLock)
                    { }
                    logger.LogDebug($"grief lock end");
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
                taskToWait = GetDelayTask();
                logger.LogDebug("cycle ended");
            } while (true);
        }
    }
}
