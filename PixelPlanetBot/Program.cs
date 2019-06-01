using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
    using Pixel = ValueTuple<short, short, PixelColor>;
    using Message = ValueTuple<string, ConsoleColor>;

    static partial class Program
    {
        private static readonly string appFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetBot");
        private static readonly string guidFilePathTemplate = Path.Combine(appFolder, "fingerprint.bin");
        private static string logFilePath;
        private static bool logToFile = false;

        private static ConcurrentBag<Thread> backgroundThreads = new ConcurrentBag<Thread>();
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();
        private static bool defendMode;
        private static ChunkCache cache;
        private static bool repeatingFails = false;

        private static AutoResetEvent gotGriefed;
        private static readonly Gate mapDownloadedGate = new Gate(true);
        private static readonly Gate captchaPassedGate = new Gate(true);
        private static readonly AutoResetEvent messagesAvailable = new AutoResetEvent(false);
        private static readonly AutoResetEvent consoleMessagesAvailable = new AutoResetEvent(false);
        private static object waitingGriefLock;

        private static PixelColor[,] imagePixels;
        private static IEnumerable<Pixel> pixelsToBuild;
        private static short leftX, topY;
        private static string fingerprint;

        private static readonly HashSet<Pixel> placed = new HashSet<Pixel>();
        private static readonly ConcurrentQueue<Message> messages = new ConcurrentQueue<Message>();
        private static readonly ConcurrentQueue<Message> consoleMessages = new ConcurrentQueue<Message>();

        private static volatile int builtInLastMinute = 0;
        private static volatile int griefedInLastMinute = 0;
        private static readonly Queue<int> builtInPast = new Queue<int>();
        private static readonly Queue<int> griefedInPast = new Queue<int>();

        public static void LogLine(string msg, MessageGroup group, ConsoleColor color)
        {
            string line = string.Format("{0}  {1}  {2}", DateTime.Now.ToString("HH:mm:ss"), $"[{group.ToString().ToUpper()}]".PadRight(9), msg);
            messages.Enqueue((line, color));
            messagesAvailable.Set();
        }

        public static void LogPixel(MessageGroup group, string msg, int x, int y, PixelColor color, ConsoleColor consoleColor)
        {
            string text = $"{msg.PadRight(22)} {color.ToString().PadRight(13)} at ({x.ToString().PadLeft(6)};{y.ToString().PadLeft(6)})";
            LogLine(text, group, consoleColor);
        }

        public static Thread StartBackgroundThread(ThreadStart threadBody)
        {
            Thread thread = new Thread(threadBody);
            backgroundThreads.Add(thread);
            thread.Start();
            return thread;
        }

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
                StartBackgroundThread(LogWriterThreadBody);
                StartBackgroundThread(ConsoleWriterThreadBody);
                ushort width, height;
                PlacingOrderMode order = PlacingOrderMode.Random;
                try
                {
                    try
                    {
                        try
                        {
                            File.Open(args[5], FileMode.Append, FileAccess.Write).Dispose();
                            logFilePath = args[5];
                            logToFile = true;
                        }
                        catch
                        { }

                        leftX = short.Parse(args[0]);
                        topY = short.Parse(args[1]);
                        if (args.Length > 3)
                        {
                            defendMode = args[3].ToLower() == "y";
                        }
                        if (args.Length > 4)
                        {
                            switch (args[4].ToUpper())
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
                        }

                        fingerprint = GetFingerprint();

                        Bitmap image;
                        LogLine("Downloading image...", MessageGroup.State, ConsoleColor.Yellow);
                        using (WebClient wc = new WebClient())
                        {
                            byte[] data = wc.DownloadData(args[2]);
                            MemoryStream ms = new MemoryStream(data);
                            image = new Bitmap(ms);
                        }
                        LogLine("Image is downloaded", MessageGroup.Info, ConsoleColor.Blue);
                        LogLine("Converting image...", MessageGroup.State, ConsoleColor.Yellow);
                        imagePixels = ImageProcessing.ToPixelWorldColors(image);
                        LogLine("Image is converted", MessageGroup.Info, ConsoleColor.Blue);

                        checked
                        {
                            width = (ushort)image.Width;
                            height = (ushort)image.Height;
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
                        throw new Exception("Parameters: <leftX: -32768..32767> <topY: -32768..32767> <imageURL> [defendMode: Y/N = N] [buildFrom L/R/T/B/RND = RND] [logFileName = none]");// [proxyIP:proxyPort = nothing]
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
                cache = new ChunkCache(pixelsToBuild);
                cache.OnMapDownloaded += (o, e) => mapDownloadedGate.Open();
                if (defendMode)
                {
                    gotGriefed = new AutoResetEvent(false);
                    cache.OnMapDownloaded += (o, e) => gotGriefed.Set();
                    waitingGriefLock = new object();                    
                }
                StartBackgroundThread(StatsCollectionThreadBody);
                do
                {
                    try
                    {
                        using (InteractionWrapper wrapper = new InteractionWrapper(fingerprint))
                        {
                            wrapper.OnPixelChanged += LogPixelChanged;
                            wrapper.OnConnectionLost += (o, e) => mapDownloadedGate.Close();
                            cache.Wrapper = wrapper;
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
                                    if (!CorrectPixelColor(actualColor, color))
                                    {
                                        wasChanged = true;
                                        bool success;
                                        placed.Add(pixel);
                                        do
                                        {
                                            byte placingPixelFails = 0;
                                            mapDownloadedGate.WaitOpened();
                                            success = wrapper.PlacePixel(x, y, color, out double cd, out double totalCd, out string error);
                                            if (success)
                                            {
                                                string prefix = cd == 4 ? "P" : "Rep";
                                                LogPixel(MessageGroup.Pixel, $"{prefix}laced pixel:", x, y, color, ConsoleColor.Green);
                                                Thread.Sleep(TimeSpan.FromSeconds(totalCd < 53 ? 1 : cd));
                                            }
                                            else
                                            {
                                                if (cd == 0.0)
                                                {
                                                    LogLine("Please go to browser and place pixel, then return and press any key", MessageGroup.Captcha, ConsoleColor.Red);
                                                    Process.Start($"{InteractionWrapper.BaseHttpAdress}/#{x},{y},30");
                                                    Thread.Sleep(100);
                                                    captchaPassedGate.Close();
                                                    while (Console.KeyAvailable)
                                                    {
                                                        Console.ReadKey(true);
                                                    }
                                                    Console.ReadKey(true);
                                                    captchaPassedGate.Open();
                                                }
                                                else
                                                {
                                                    LogLine($"Failed to place pixel: {error}", MessageGroup.Pixel, ConsoleColor.Red);
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

                                        LogLine("Image is intact, waiting...", MessageGroup.State, ConsoleColor.Green);
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
                            LogLine("Building is finished, exiting...", MessageGroup.State, ConsoleColor.Green);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogLine($"Unhandled exception: {ex.Message}", MessageGroup.Error, ConsoleColor.Red);
                        int delay = repeatingFails ? 30 : 10;
                        repeatingFails = true;
                        LogLine($"Reconnecting in {delay} seconds...", MessageGroup.State, ConsoleColor.Yellow);
                        Thread.Sleep(TimeSpan.FromSeconds(delay));
                        continue;
                    }
                } while (true);
            }
            finally
            {
                finishCTS.Cancel();
                Thread.Sleep(1000);
                finishCTS.Dispose();
                gotGriefed?.Dispose();
                mapDownloadedGate?.Dispose();
                captchaPassedGate?.Dispose();

                foreach (Thread thread in backgroundThreads.Where(t => t.IsAlive))
                {
                    thread.Interrupt(); //fallback, should never work 
                }
            }
        }

        private static bool CorrectPixelColor(PixelColor actualColor, PixelColor desiredColor)
        {
            return (actualColor == desiredColor) ||
                    (actualColor == PixelColor.UnsetOcean && desiredColor == PixelColor.SkyBlue) ||
                    (actualColor == PixelColor.UnsetLand && desiredColor == PixelColor.White);
        }

        private static void LogPixelChanged(object sender, PixelChangedEventArgs e)
        {
            ConsoleColor msgColor;
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
                        msgColor = ConsoleColor.DarkGray;
                        msgGroup = MessageGroup.Info;
                    }
                    else
                    {
                        if (desiredColor == e.Color)
                        {
                            msgColor = ConsoleColor.Green;
                            msgGroup = MessageGroup.Assist;
                            builtInLastMinute++;
                        }
                        else
                        {
                            msgColor = ConsoleColor.Red;
                            msgGroup = MessageGroup.Attack;
                            griefedInLastMinute++;
                            gotGriefed?.Set();
                        }
                    }
                }
                catch
                {
                    msgColor = ConsoleColor.DarkGray;
                    msgGroup = MessageGroup.Info;
                }
                LogPixel(msgGroup, $"Received pixel update:", x, y, e.Color, msgColor);
            }
            else
            {
                builtInLastMinute++;
            }
        }

        private static void ConsoleWriterThreadBody()
        {
            while (true)
            {
                captchaPassedGate.WaitOpened();
                if (consoleMessages.TryDequeue(out Message msg))
                {
                    (string line, ConsoleColor color) = msg;
                    Console.ForegroundColor = color;
                    Console.WriteLine(line);
                }
                else
                {
                    if (finishCTS.IsCancellationRequested)
                    {
                        return;
                    }
                    try
                    {
                        consoleMessagesAvailable.WaitOne();
                    }
                    catch (ThreadInterruptedException)
                    {
                        return;
                    }
                }
            }
        }

        private static void LogWriterThreadBody()
        {
            while (true)
            {
                if (messages.TryDequeue(out Message msg))
                {
                    consoleMessages.Enqueue(msg);
                    consoleMessagesAvailable.Set();
                    if (logToFile)
                    {
                        using (StreamWriter writer = new StreamWriter(logFilePath, true))
                        {
                            writer.WriteLine(msg.Item1);
                        }
                    }
                }
                else
                {
                    if (finishCTS.IsCancellationRequested)
                    {
                        return;
                    }
                    try
                    {
                        messagesAvailable.WaitOne();
                    }
                    catch (ThreadInterruptedException)
                    {
                        return;
                    }
                }
            }
        }

        private static void StatsCollectionThreadBody()
        {
            try
            {
                mapDownloadedGate.WaitOpened();
                do
                {
                    if (finishCTS.IsCancellationRequested)
                    {
                        return;
                    }
                    try
                    {
                        Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token).Wait();
                    }
                    catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
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

                    double griefedPerMinute = griefedInPast.Where(i => i > 0).Cast<int?>().Average() ?? 0;
                    double builtPerMinute = builtInPast.Where(i => i > 0).Cast<int?>().Average() ?? 0;
                    double buildSpeed = builtPerMinute - griefedPerMinute;
                    int done = pixelsToBuild.
                       Where(p => CorrectPixelColor(cache.GetPixelColor(p.Item1, p.Item2), p.Item3)).
                       Count();
                    int total = pixelsToBuild.Count();
                    double percent = Math.Floor(done * 1000.0 / total) / 10.0;
                    if (finishCTS.IsCancellationRequested)
                    {
                        return;
                    }
                    if (defendMode)
                    {
                        LogLine($"Image integrity is {percent:F1}%, {total - done} corrupted pixels", MessageGroup.Info, ConsoleColor.Magenta);
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
                        LogLine(info, MessageGroup.Info, ConsoleColor.Magenta);
                    }
                    if (griefedPerMinute > 1)
                    {
                        LogLine($"Image is under attack at the moment, {done} pixels are good now", MessageGroup.Info, ConsoleColor.Magenta);
                        LogLine($"Building {builtPerMinute:F1} px/min, getting griefed {griefedPerMinute:F1} px/min", MessageGroup.Info, ConsoleColor.Magenta);
                    }
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
