using PixelPlanetUtils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
	
//TODO border mode
//TODO reminding

    using Pixel = ValueTuple<short, short, PixelColor>;

    static partial class Program
    {
        private static readonly string appFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetBot");
        private static readonly string guidFilePathTemplate = Path.Combine(appFolder, "fingerprint.bin");

        private static Thread statsThread;
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();
        private static bool defendMode;
        private static CaptchaNotificationMode notificationMode;
        private static ChunkCache cache;
        private static bool repeatingFails = false;

        private static AutoResetEvent gotGriefed;
        private static ManualResetEvent mapDownloadedResetEvent;

        private static object waitingGriefLock;

        private static PixelColor[,] imagePixels;
        private static IEnumerable<Pixel> pixelsToBuild;
        private static short leftX, topY;
        private static string fingerprint;

        private static readonly HashSet<Pixel> placed = new HashSet<Pixel>();

        private static volatile int builtInLastMinute = 0;
        private static volatile int griefedInLastMinute = 0;
        private static readonly Queue<int> builtInPast = new Queue<int>();
        private static readonly Queue<int> griefedInPast = new Queue<int>();
        private static readonly Queue<int> doneInPast = new Queue<int>();
        private static Logger logger;


        private static void Main(string[] args)
        {
            try
            {
                if (args.Length == 1)
                {
                    try
                    {
                        SaveFingerprint(Guid.Parse(args[0]));
                        Console.WriteLine("Fingerprint is saved, now you can relauch bot with needed parameters");
                    }
                    catch
                    {
                        Console.WriteLine("You should pass correct 128-bit fingerprint (GUID)");
                    }
                    return;
                }
                ushort width, height;
                PlacingOrderMode order = PlacingOrderMode.Random;
                notificationMode = CaptchaNotificationMode.Browser;
                try
                {
                    try
                    {
                        leftX = short.Parse(args[0]);
                        topY = short.Parse(args[1]);
                        fingerprint = GetFingerprint();
                        string logFilePath = null;
                        if (args.Length > 3)
                        {
                            notificationMode = CaptchaNotificationMode.None;
                            string upper = args[3].ToUpper();
                            if (upper.Contains('B'))
                            {
                                notificationMode |= CaptchaNotificationMode.Browser;
                            }
                            if (upper.Contains('S'))
                            {
                                notificationMode |= CaptchaNotificationMode.Sound;
                            }
                            if (args.Length > 4)
                            {
                                defendMode = args[4].ToUpper() == "Y";

                                if (args.Length > 5)
                                {
                                    switch (args[5].ToUpper())
                                    {
                                        case "R":
                                            order = PlacingOrderMode.FromRight;
                                            break;
                                        case "L":
                                            order = PlacingOrderMode.FromLeft;
                                            break;
                                        case "T":
                                            order = PlacingOrderMode.FromTop;
                                            break;
                                        case "B":
                                            order = PlacingOrderMode.FromBottom;
                                            break;
                                    }

                                    if (args.Length > 6)
                                    {
                                        try
                                        {
                                            File.OpenWrite(args[6]).Dispose();
                                            logFilePath = args[6];
                                        }
                                        catch
                                        { }
                                    }
                                }
                            }
                        }
                        logger = new Logger(finishCTS.Token, logFilePath);
                        imagePixels = ImageProcessing.PixelColorsByUri(args[2], logger.LogLine);
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
                        throw new Exception("Entire image should be inside the map");
                    }
                    catch (WebException)
                    {
                        throw new Exception("Cannot download image");
                    }
                    catch (ArgumentException)
                    {
                        throw new Exception("Cannot convert image");
                    }
                    catch (IOException)
                    {
                        throw new Exception("Fingerprint is not saved, pass it from browser as only parameter to app to save before usage");
                    }
                    catch
                    {
                        throw new Exception("Parameters: <leftX> <topY> <imageURI> [notificationMode: N/B/S/BS = B] [defendMode: Y/N = N] [buildFrom L/R/T/B/RND = RND] [logFileName = none]");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    finishCTS.Cancel();
                    return;
                }
                IEnumerable<int> allY = Enumerable.Range(0, height);
                IEnumerable<int> allX = Enumerable.Range(0, width);
                Pixel[] nonEmptyPixels = allX.
                    SelectMany(X => allY.Select(Y =>
                        (X: (short)(X + leftX), Y: (short)(Y + topY), C: imagePixels[X, Y]))).
                    Where(xy => xy.C != PixelColor.None).ToArray();
                switch (order)
                {
                    case PlacingOrderMode.FromLeft:
                        pixelsToBuild = nonEmptyPixels.OrderBy(xy => xy.Item1).ToList();
                        break;
                    case PlacingOrderMode.FromRight:
                        pixelsToBuild = nonEmptyPixels.OrderByDescending(xy => xy.Item1).ToList();
                        break;
                    case PlacingOrderMode.FromTop:
                        pixelsToBuild = nonEmptyPixels.OrderBy(xy => xy.Item2).ToList();
                        break;
                    case PlacingOrderMode.FromBottom:
                        pixelsToBuild = nonEmptyPixels.OrderByDescending(xy => xy.Item2).ToList();
                        break;
                    default:
                        Random rnd = new Random();
                        for (int i = 0; i < nonEmptyPixels.Length; i++)
                        {
                            int r = rnd.Next(i, nonEmptyPixels.Length);
                            Pixel tmp = nonEmptyPixels[r];
                            nonEmptyPixels[r] = nonEmptyPixels[i];
                            nonEmptyPixels[i] = tmp;
                        }
                        pixelsToBuild = nonEmptyPixels;
                        break;
                }
                cache = new ChunkCache(pixelsToBuild, logger.LogLine);
                mapDownloadedResetEvent = new ManualResetEvent(false);
                cache.OnMapDownloaded += (o, e) => mapDownloadedResetEvent.Set();
                if (defendMode)
                {
                    gotGriefed = new AutoResetEvent(false);
                    cache.OnMapDownloaded += (o, e) => gotGriefed.Set();
                    waitingGriefLock = new object();
                }
                statsThread = new Thread(StatsCollectionThreadBody);
                statsThread.Start();
                do
                {
                    try
                    {
                        using (InteractionWrapper wrapper = new InteractionWrapper(fingerprint, logger.LogLine))
                        {
                            wrapper.OnConnectionLost += (o, e) => mapDownloadedResetEvent.Reset();
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
                                    (short x, short y, PixelColor color) = pixel;
                                    PixelColor actualColor = cache.GetPixelColor(x, y);
                                    if (!IsCorrectPixelColor(actualColor, color))
                                    {
                                        wasChanged = true;
                                        bool success;
                                        placed.Add(pixel);
                                        do
                                        {
                                            byte placingPixelFails = 0;
                                            mapDownloadedResetEvent.WaitOne();
                                            success = wrapper.PlacePixel(x, y, color, out double cd, out double totalCd, out string error);
                                            if (success)
                                            {
                                                string prefix = cd == 4 ? "P" : "Rep";
                                                logger.LogPixel($"{prefix}laced pixel:", MessageGroup.Pixel, x, y, color);
                                                Thread.Sleep(TimeSpan.FromSeconds(totalCd < 53 ? 1 : cd));
                                            }
                                            else
                                            {
                                                if (cd == 0.0)
                                                {
                                                    logger.LogLine("Please go to browser and place pixel, then return and press any key", MessageGroup.Captcha);
                                                    CancellationTokenSource captchaCts = null;
                                                    if (notificationMode.HasFlag(CaptchaNotificationMode.Sound))
                                                    {
                                                        captchaCts = new CancellationTokenSource();
                                                        Task.Run(() =>
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
                                                                    Task.Delay(TimeSpan.FromMinutes(1), token);
                                                                }
                                                                catch (TaskCanceledException)
                                                                {
                                                                    return;
                                                                }
                                                            }
                                                        });
                                                    }
                                                    if (notificationMode.HasFlag(CaptchaNotificationMode.Browser))
                                                    {
                                                        Process.Start($"{InteractionWrapper.BaseHttpAdress}/#{x},{y},30");
                                                    }
                                                    Thread.Sleep(100);
                                                    logger.ConsoleLoggingResetEvent.Reset();
                                                    while (Console.KeyAvailable)
                                                    {
                                                        Console.ReadKey(true);
                                                    }
                                                    Console.ReadKey(true);
                                                    logger.ConsoleLoggingResetEvent.Set();
                                                    captchaCts?.Cancel();
                                                    captchaCts?.Dispose();
                                                }
                                                else
                                                {
                                                    logger.LogLine($"Failed to place pixel: {error}", MessageGroup.PixelFail);
                                                    if (++placingPixelFails == 3)
                                                    {
                                                        throw new Exception("Cannot place pixel 3 times");
                                                    }

                                                }
                                                Thread.Sleep(TimeSpan.FromSeconds(cd));
                                            }

                                        } while (!success);
                                    }
                                }
                                if (defendMode)
                                {
                                    if (!wasChanged)
                                    {

                                        logger.LogLine("Image is intact, waiting...", MessageGroup.ImageDone);
                                        lock (waitingGriefLock)
                                        {
                                            gotGriefed.Reset();
                                            gotGriefed.WaitOne();
                                        }
                                        Thread.Sleep(new Random().Next(500, 3000));
                                    }
                                }
                            }
                            while (defendMode || wasChanged);
                            logger.LogLine("Building is finished, exiting...", MessageGroup.ImageDone);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogLine($"Unhandled exception: {ex.Message}", MessageGroup.Error);
                        int delay = repeatingFails ? 30 : 10;
                        repeatingFails = true;
                        logger.LogLine($"Reconnecting in {delay} seconds...", MessageGroup.TechState);
                        Thread.Sleep(TimeSpan.FromSeconds(delay));
                        continue;
                    }
                } while (true);
            }
            finally
            {
                finishCTS.Cancel();
                gotGriefed?.Dispose();
                mapDownloadedResetEvent?.Dispose();
                logger?.Dispose();
                Thread.Sleep(1000);
                finishCTS.Dispose();
                if (statsThread?.IsAlive ?? false)
                {
                    statsThread.Interrupt(); //fallback, should never work 
                }
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
                logger.LogPixel($"Received pixel update:", msgGroup, x, y, e.Color);
            }
            else
            {
                builtInLastMinute++;
            }
        }

        private static void StatsCollectionThreadBody()
        {
            try
            {
                mapDownloadedResetEvent.WaitOne();
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
                    if (finishCTS.IsCancellationRequested)
                    {
                        return;
                    }
                    if (defendMode)
                    {
                        logger.LogLine($"Image integrity is {percent:F1}%, {total - done} corrupted pixels", MessageGroup.Info);
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
                        logger.LogLine(info, MessageGroup.Info);
                    }
                    if (griefedPerMinute > 1)
                    {
                        logger.LogLine($"Image is under attack at the moment, {done} pixels are good now", MessageGroup.Info);
                        logger.LogLine($"Building {builtPerMinute:F1} px/min, getting griefed {griefedPerMinute:F1} px/min", MessageGroup.Info);
                    }
                    taskToWait = Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token);
                } while (true);
            }
            catch (ThreadInterruptedException)
            {
                return;
            }
        }

        private static string GetFingerprint()
        {
            Guid guid = Guid.Empty;
            byte[] bytes = File.ReadAllBytes(guidFilePathTemplate);
            guid = new Guid(bytes);
            return guid.ToString("N");
        }

        private static void SaveFingerprint(Guid guid)
        {
            Directory.CreateDirectory(appFolder);
            File.WriteAllBytes(guidFilePathTemplate, guid.ToByteArray());
        }
    }
}
