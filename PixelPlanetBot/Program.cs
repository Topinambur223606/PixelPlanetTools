using CommandLine;
using PixelPlanetBot.Options;
using PixelPlanetUtils;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Accounts;
using PixelPlanetUtils.Options;
using PixelPlanetUtils.Updates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaskInvalidSizeException = PixelPlanetUtils.ImageProcessing.MaskInvalidSizeException;
using Pixel = System.ValueTuple<short, short, PixelPlanetUtils.Canvas.EarthPixelColor>;

namespace PixelPlanetBot
{
    static partial class Program
    {
        private static Thread statsThread;
        private static object waitingGriefLock;
        private static AutoResetEvent gotGriefed;
        private static bool repeatingFails = false;
        private static CancellationTokenSource captchaCts;
        private static ManualResetEvent mapUpdatedResetEvent;
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();

        private static List<Cookie> cookies;
        private static Logger logger;
        private static RunOptions runOptions;
        private static SessionsOptions accountsOptions;
        private static AppOptions appOptions;
        private static ChunkCache cache;
        private static ushort width, height;
        private static EarthPixelColor[,] imagePixels;
        private static ushort[,] brightnessOrderMask;
        private static IEnumerable<Pixel> pixelsToBuild;

        private static volatile int builtInLastMinute = 0;
        private static volatile int griefedInLastMinute = 0;
        private static bool checkUpdates;
        private static ProxySettings proxySettings;
        private static readonly Queue<int> doneInPast = new Queue<int>();
        private static readonly Queue<int> builtInPast = new Queue<int>();
        private static readonly Queue<int> griefedInPast = new Queue<int>();
        private static readonly HashSet<Pixel> placed = new HashSet<Pixel>();

        private static void Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args))
                {
                    return;
                }

                logger = new Logger(appOptions?.LogFilePath, finishCTS.Token)
                {
                    ShowDebugLogs = appOptions?.ShowDebugLogs ?? false
                };
                logger.LogDebug("Command line: " + Environment.CommandLine);

                if (checkUpdates || !appOptions.DisableUpdates)
                {
                    if (UpdateChecker.IsStartingUpdate(logger, checkUpdates) || checkUpdates)
                    {
                        return;
                    }
                }

                if (runOptions != null)
                {
                    RunPixelActivity();
                }
                else if (accountsOptions != null)
                {
                    RunAccountActivity();
                }
            }
            catch (Exception ex)
            {
                string msg = $"Unhandled app level exception: {ex.GetBaseException().Message}";
                if (logger != null)
                {
                    logger.LogError(msg);
                }
                else
                {
                    Console.WriteLine(msg);
                }
            }
            finally
            {
                if (logger != null)
                {
                    logger.LogInfo("Exiting...");
                    logger.LogInfo($"Logs were saved to {logger.LogFilePath}");
                }
                finishCTS.Cancel();
                if (logger != null)
                {
                    Thread.Sleep(500);
                }
                gotGriefed?.Dispose();
                mapUpdatedResetEvent?.Dispose();
                logger?.Dispose();
                finishCTS.Dispose();
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private static void RunAccountActivity()
        {
            if (accountsOptions.Add)
            {
                logger.LogTechState("Logging in...");
                Task<string> task = AccountManager.Login(accountsOptions.UserName, accountsOptions.Password, accountsOptions.SessionName);
                try
                {
                    task.Wait();
                }
                catch (AggregateException a)
                {
                    throw a.InnerException;
                }
                logger.LogTechInfo($"Successfully logged in with session name \"{task.Result}\"");
            }

            if (accountsOptions.Remove)
            {
                logger.LogTechState("Logging out...");
                Task task = AccountManager.Logout(accountsOptions.SessionName);
                try
                {
                    task.Wait();
                }
                catch (AggregateException a)
                {
                    throw a.InnerException;
                }
                logger.LogTechInfo("Successfully logged out");
            }

            if (accountsOptions.PrintSessionList)
            {
                List<string> sessions = AccountManager.GetSessions();
                Console.ForegroundColor = ConsoleColor.White;
                if (sessions.Count > 0)
                {
                    Console.WriteLine("All sessions:");
                    foreach (string session in sessions.OrderBy(s => s.ToLower()))
                    {
                        Console.WriteLine(session);
                    }
                }
                else
                {
                    Console.WriteLine("No sessions found");
                }
            }
        }

        private static void RunPixelActivity()
        {
            try
            {
                if (runOptions.SessionName != null)
                {
                    logger.LogTechState("Loading session...");
                    cookies = AccountManager.GetSessionCookies(runOptions.SessionName);
                    logger.LogTechInfo("Session loaded");
                    logger.LogTechState("Checking session...");
                    AccountManager.CheckSession(cookies).Wait();
                    logger.LogTechInfo("Session is alive");
                }

                imagePixels = ImageProcessing.PixelColorsByUri(runOptions.ImagePath, logger);
                try
                {
                    checked
                    {
                        width = (ushort)imagePixels.GetLength(0);
                        height = (ushort)imagePixels.GetLength(1);
                        short check;
                        check = (short)(runOptions.LeftX + width);
                        check = (short)(runOptions.TopY + height);
                    }

                }
                catch (OverflowException ex)
                {
                    logger.LogError("Entire image should be inside the map");
                    logger.LogDebug($"Main(): error checking boundaries - {ex.Message}");
                    return;
                }

                if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.Mask))
                {
                    brightnessOrderMask = ImageProcessing.GetBrightnessOrderMask(runOptions.BrightnessMaskImagePath, logger, width, height);
                }
            }
            catch (WebException ex)
            {
                logger.LogError("Cannot download image");
                logger.LogDebug($"Main(): error downloading image - {ex.Message}");
                return;
            }
            catch (ArgumentException ex)
            {
                logger.LogError("Cannot convert image");
                logger.LogDebug($"Main(): error converting image - {ex.Message}");
                return;
            }
            catch (MaskInvalidSizeException)
            {
                logger.LogError("Image and mask sizes are not equal");
                return;
            }

            logger.LogTechState("Calculating pixel placing order...");
            if (!CalculatePixelOrder())
            {
                return;
            }
            logger.LogTechInfo("Pixel placing order is calculated");

            cache = new ChunkCache(pixelsToBuild, logger);
            mapUpdatedResetEvent = new ManualResetEvent(false);
            cache.OnMapUpdated += (o, e) =>
            {
                logger.LogDebug("Cache.OnMapUpdated handler: Map updated event received");
                mapUpdatedResetEvent.Set();
            };
            if (runOptions.DefenseMode)
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
                    MainWorkingBody();
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError($"Unhandled exception: {ex.GetBaseException().Message}");
                    int delay = repeatingFails ? 30 : 10;
                    repeatingFails = true;
                    logger.LogTechState($"Reconnecting in {delay} seconds...");
                    Thread.Sleep(TimeSpan.FromSeconds(delay));
                    continue;
                }
            } while (true);
        }

        private static bool ParseArguments(IEnumerable<string> args)
        {
            bool ProcessBotOptions(BotOptions o)
            {
                if (!string.IsNullOrWhiteSpace(o.ProxyAddress))
                {
                    int protocolLength = o.ProxyAddress.IndexOf("://");
                    if (!o.ProxyAddress.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (protocolLength > -1)
                        {
                            o.ProxyAddress = "http" + o.ProxyAddress.Substring(protocolLength);
                        }
                        else
                        {
                            o.ProxyAddress = "http://" + o.ProxyAddress;
                        }
                    }
                    if (!Uri.IsWellFormedUriString(o.ProxyAddress, UriKind.Absolute))
                    {
                        Console.WriteLine("Invalid proxy address");
                        return false;
                    }

                    proxySettings = new ProxySettings
                    {
                        Address = o.ProxyAddress,
                        Username = o.ProxyUsername,
                        Password = o.ProxyPassword
                    };
                }
                if (o.UseMirror)
                {
                    UrlManager.MirrorMode = o.UseMirror;
                }
                if (o.ServerUrl != null)
                {
                    UrlManager.BaseUrl = o.ServerUrl;
                }
                return true;
            }

            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<RunOptions, CheckUpdatesOption, SessionsOptions>(args)
                    .WithNotParsed(e => success = false)
                    .WithParsed<CheckUpdatesOption>(o => checkUpdates = true)
                    .WithParsed<RunOptions>(o =>
                    {
                        appOptions = runOptions = o;
                        if (!ProcessBotOptions(o))
                        {
                            return;
                        }
                        if (o.PlacingOrderMode.HasFlag(PlacingOrderMode.Mask) && o.BrightnessMaskImagePath == null)
                        {
                            Console.WriteLine("Mask path not specified");
                            success = false;
                            return;
                        }
                        if (proxySettings != null && o.NotificationMode.HasFlag(CaptchaNotificationMode.Browser))
                        {
                            Console.WriteLine($"Warning: proxy usage in browser notification mode is detected{Environment.NewLine}" +
                                "Ensure that same proxy settings are set in your default browser");
                        }

                    })
                    .WithParsed<SessionsOptions>(o =>
                    {
                        appOptions = accountsOptions = o;
                        if (!ProcessBotOptions(o))
                        {
                            return;
                        }
                        if (!(o.Add || o.Remove || o.PrintSessionList))
                        {
                            o.PrintSessionList = true;
                            return;
                        }
                        if (o.Add && (o.UserName == null || o.Password == null))
                        {
                            success = false;
                            Console.WriteLine("Both username and password should be specified to log in");
                            return;
                        }
                        if (o.Remove && o.SessionName == null)
                        {
                            success = false;
                            Console.WriteLine("Session name to remove should be specified");
                            return;
                        }
                    });
                return success;
            }
        }

        private static double OutlineCriteria(Pixel p)
        {
            const int radius = 3;
            double score = ThreadSafeRandom.NextDouble() * 125D;
            (short x, short y, EarthPixelColor c) = p;
            for (int i = -radius; i <= radius; i++)
            {
                for (int j = -radius; j <= radius; j++)
                {
                    int ox = x + i;
                    int oy = y + j;
                    double dist = Math.Sqrt(i * i + j * j);
                    if (ox >= 0 && oy >= 0 && ox < width && oy < height)
                    {
                        EarthPixelColor c2 = imagePixels[ox, oy];
                        if (c2 == EarthPixelColor.None)
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
        }

        private static bool CalculatePixelOrder()
        {
            IEnumerable<Pixel> relativePixelsToBuild;
            IEnumerable<int> allY = Enumerable.Range(0, height);
            IEnumerable<int> allX = Enumerable.Range(0, width);
            IList<Pixel> nonEmptyPixels = allX.
                SelectMany(X => allY.Select(Y =>
                    ((short)X, (short)Y, C: imagePixels[X, Y]))).
                Where(xyc => xyc.C != EarthPixelColor.None).ToList();

            try
            {

                if (runOptions.PlacingOrderMode == PlacingOrderMode.Outline)
                {
                    relativePixelsToBuild = nonEmptyPixels.AsParallel().OrderByDescending(OutlineCriteria);
                }
                else if (runOptions.PlacingOrderMode == PlacingOrderMode.Random)
                {
                    Random rnd = new Random();
                    for (int i = 0; i < nonEmptyPixels.Count; i++)
                    {
                        int r = rnd.Next(i, nonEmptyPixels.Count);
                        Pixel tmp = nonEmptyPixels[r];
                        nonEmptyPixels[r] = nonEmptyPixels[i];
                        nonEmptyPixels[i] = tmp;
                    }
                    relativePixelsToBuild = nonEmptyPixels;
                }
                else
                {
                    OrderedParallelQuery<Pixel> sortedParallel;
                    ParallelQuery<Pixel> nonEmptyParallel = nonEmptyPixels.AsParallel();

                    if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.Left))
                    {
                        sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item1);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.Right))
                    {
                        sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item1);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.Top))
                    {
                        sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item2);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.Bottom))
                    {
                        sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item2);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.Color))
                    {
                        sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item3);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ColorDesc))
                    {
                        sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item3);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ColorRnd))
                    {
                        Dictionary<EarthPixelColor, Guid> colorOrder =
                            Enum.GetValues(typeof(EarthPixelColor))
                                .Cast<EarthPixelColor>()
                                .ToDictionary(c => c, c => Guid.NewGuid());

                        sortedParallel = nonEmptyParallel.OrderBy(xy => colorOrder[xy.Item3]);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.Mask))
                    {
                        sortedParallel = nonEmptyParallel.OrderByDescending(xy => brightnessOrderMask[xy.Item1, xy.Item2]);
                    }
                    else
                    {
                        throw new Exception($"{runOptions.PlacingOrderMode} is not valid placing order mode");
                    }

                    if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ThenLeft))
                    {
                        sortedParallel = sortedParallel.ThenBy(xy => xy.Item1);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ThenRight))
                    {
                        sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item1);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ThenTop))
                    {
                        sortedParallel = sortedParallel.ThenBy(xy => xy.Item2);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ThenBottom))
                    {
                        sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item2);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ThenColor))
                    {
                        sortedParallel = sortedParallel.ThenBy(xy => xy.Item3);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ThenColorDesc))
                    {
                        sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item3);
                    }
                    else if (runOptions.PlacingOrderMode.HasFlag(PlacingOrderMode.ThenColorRnd))
                    {
                        Dictionary<EarthPixelColor, Guid> colorOrder =
                            Enum.GetValues(typeof(EarthPixelColor))
                                .Cast<EarthPixelColor>()
                                .ToDictionary(c => c, c => Guid.NewGuid());

                        sortedParallel = sortedParallel.ThenBy(xy => colorOrder[xy.Item3]);
                    }
                    else
                    {
                        sortedParallel = sortedParallel.ThenBy(e => Guid.NewGuid());
                    }
                    relativePixelsToBuild = sortedParallel;
                }
                pixelsToBuild = relativePixelsToBuild
                    .Select(p => ((short)(p.Item1 + runOptions.LeftX), (short)(p.Item2 + runOptions.TopY), p.Item3)).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError($"Unhandled exception while calculating pixel order: {ex.Message}");
                return false;
            }
            return true;
        }

        private static void MainWorkingBody()
        {
            using (WebsocketWrapper wrapper = new WebsocketWrapper(logger, false, proxySettings, cookies))
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
                        (short x, short y, EarthPixelColor color) = pixel;
                        EarthPixelColor actualColor = cache.GetPixelColor(x, y);
                        if (!IsCorrectPixelColor(actualColor, color))
                        {
                            logger.LogDebug($"MainWorkingBody(): {pixel} - wrong color ({actualColor})");
                            wasChanged = true;
                            bool success;
                            placed.Add(pixel);
                            do
                            {
                                wrapper.WaitWebsocketConnected();
                                wrapper.PlacePixel(x, y, color);
                                PixelReturnData response = wrapper.GetPlacePixelResponse();

                                if (response == null)
                                {
                                    success = false;
                                    continue;
                                }

                                success = response.ReturnCode == ReturnCode.Success;
                                if (success)
                                {
                                    bool placed = actualColor == EarthPixelColor.UnsetLand || actualColor == EarthPixelColor.UnsetOcean;
                                    logger.LogPixel($"{(placed ? "P" : "Rep")}laced pixel:", DateTime.Now, MessageGroup.Pixel, x, y, color);
                                    if (response.Wait > 53000 || response.CoolDownSeconds > 7)
                                    {
                                        Thread.Sleep(TimeSpan.FromSeconds(response.CoolDownSeconds));
                                    }
                                    else
                                    {
                                        Thread.Sleep(TimeSpan.FromSeconds(1));
                                    }
                                }
                                else
                                {
                                    logger.LogDebug($"MainWorkingBody(): pixel placing handled error {response.ReturnCode}");
                                    if (response.ReturnCode == ReturnCode.Captcha)
                                    {
                                        if (runOptions.CaptchaTimeout > 0)
                                        {
                                            ProcessCaptchaTimeout();
                                        }
                                        else
                                        {
                                            ProcessCaptcha();
                                        }
                                    }
                                    else
                                    {
                                        logger.LogPixelFail(response.ReturnCode);
                                        if (response.ReturnCode != ReturnCode.IpOverused)
                                        {
                                            return;
                                        }
                                    }
                                    logger.LogDebug($"MainWorkingBody(): sleep {response.Wait} milliseconds");
                                    Thread.Sleep(TimeSpan.FromMilliseconds(response.Wait));
                                }
                            } while (!success);
                        }
                    }
                    if (runOptions.DefenseMode)
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
                            Thread.Sleep(ThreadSafeRandom.Next(500, 3000));
                        }
                    }
                } while (runOptions.DefenseMode || wasChanged);
                logger.Log("Building is finished", MessageGroup.Info);
            }
        }

        private static void ProcessCaptcha()
        {
            logger.LogAndPause("Please go to browser and place pixel, then return and press any key", MessageGroup.Captcha);
            captchaCts = null;
            if (runOptions.NotificationMode.HasFlag(CaptchaNotificationMode.Sound))
            {
                logger.LogDebug("ProcessCaptcha(): starting beep thread");
                captchaCts = new CancellationTokenSource();
                new Thread(BeepThreadBody).Start();
            }
            if (runOptions.NotificationMode.HasFlag(CaptchaNotificationMode.Browser))
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

        private static void ProcessCaptchaTimeout()
        {
            logger.Log("Got captcha, waiting...", MessageGroup.Captcha);
            if (runOptions.NotificationMode.HasFlag(CaptchaNotificationMode.Sound))
            {
                Beep();
            }
            if (runOptions.NotificationMode.HasFlag(CaptchaNotificationMode.Browser))
            {
                logger.LogDebug("ProcessCaptchaTimeout(): starting browser");
                Process.Start(UrlManager.BaseHttpAdress);
            }
            Thread.Sleep(TimeSpan.FromSeconds(runOptions.CaptchaTimeout));
            logger.LogTechInfo("Captcha timeout");
        }

        private static void Beep()
        {
            for (int j = 0; j < 7; j++)
            {
                Console.Beep(1000, 100);
            }
        }

        private static void BeepThreadBody()
        {
            logger.LogDebug("BeepThreadBody() started");
            CancellationToken token = captchaCts.Token;
            while (!token.IsCancellationRequested)
            {
                logger.LogDebug("BeepThreadBody(): beeping");
                Beep();
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

            try
            {

                logger.LogDebug("StatsCollectionThreadBody() started");
                mapUpdatedResetEvent.WaitOne();
                logger.LogDebug("StatsCollectionThreadBody(): map updated, stats collection started");
                Task taskToWait = GetDelayTask();
                int total = pixelsToBuild.Count();
                int done = CountDone();
                logger.LogDebug($"StatsCollectionThreadBody(): {done} pixels are done at start");
                doneInPast.Enqueue(done);
                do
                {
                    if (finishCTS.IsCancellationRequested)
                    {
                        logger.LogDebug($"StatsCollectionThreadBody(): cancellation requested (S1), finishing");
                        return;
                    }
                    try
                    {
                        logger.LogDebug($"StatsCollectionThreadBody(): task waiting");
                        taskToWait.Wait();
                    }
                    catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
                    {
                        logger.LogDebug($"StatsCollectionThreadBody(): cancellation requested (S2), finishing");
                        return;
                    }
                    if (finishCTS.IsCancellationRequested)
                    {
                        logger.LogDebug($"StatsCollectionThreadBody(): cancellation requested (S3), finishing");
                        return;
                    }

                    AddToQueue(builtInPast, builtInLastMinute);
                    builtInLastMinute = 0;
                    AddToQueue(griefedInPast, griefedInLastMinute);
                    griefedInLastMinute = 0;
                    done = CountDone(); //time consuming => cancellation check again later
                    double buildSpeed = (done - doneInPast.First()) / ((double)doneInPast.Count);
                    AddToQueue(doneInPast, done);
                    logger.LogDebug($"StatsCollectionThreadBody(): last minute: {builtInPast.Last()} built, {griefedInPast.Last()} griefed; {done} total done");

                    double griefedPerMinute = griefedInPast.Average();
                    double builtPerMinute = builtInPast.Average();
                    double percent = Math.Floor(done * 1000D / total) / 10D;
                    DateTime time = DateTime.Now;

                    if (finishCTS.IsCancellationRequested)
                    {
                        logger.LogDebug($"StatsCollectionThreadBody(): cancellation requested (S4), finishing");
                        return;
                    }
                    if (runOptions.DefenseMode)
                    {
                        logger.Log($"Image integrity is {percent:F1}%, {total - done} corrupted pixels", MessageGroup.Info, time);
                        logger.LogDebug($"StatsCollectionThreadBody(): acquiring grief lock");
                        lock (waitingGriefLock)
                        { }
                        logger.LogDebug($"StatsCollectionThreadBody(): grief lock released");
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
                    logger.LogDebug("StatsCollectionThreadBody(): cycle ended");
                } while (true);
            }
            catch (Exception ex)
            {
                logger.LogError($"Stats collection thread: unhandled exception - {ex.Message}");
            }
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
                    EarthPixelColor desiredColor = imagePixels[x - runOptions.LeftX, y - runOptions.TopY];
                    if (desiredColor == EarthPixelColor.None)
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

        private static bool IsCorrectPixelColor(EarthPixelColor actualColor, EarthPixelColor desiredColor)
        {
            return (actualColor == desiredColor) ||
                    (actualColor == EarthPixelColor.UnsetOcean && desiredColor == EarthPixelColor.SkyBlue) ||
                    (actualColor == EarthPixelColor.UnsetLand && desiredColor == EarthPixelColor.White);
        }
    }
}