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
        private static readonly string guidFilePathTemplate = Path.Combine(appFolder, "fingerprint{0}.bin");

        private static bool defendMode;
        private static PixelColor[,] imagePixels;
        private static IEnumerable<Pixel> pixelsToBuild;
        private static short leftX, topY;
        private static ChunkCache cache;

        private static AutoResetEvent gotGriefed;
        private static AutoResetEvent gotChunksDownloaded = new AutoResetEvent(false);
        private static object waitingGriefLock;

        private static readonly HashSet<Pixel> placed = new HashSet<Pixel>();
        private static readonly ConcurrentQueue<Message> messages = new ConcurrentQueue<Message>();
        private static readonly AutoResetEvent messagesAvailable = new AutoResetEvent(false);
        private static string logFilePath;
        private static bool logToFile = false;


        private static string fingerprint;
        private static volatile int builtInLastMinute = 0;
        private static readonly Queue<int> builtInPast = new Queue<int>();

        public static ConcurrentBag<Thread> BackgroundThreads { get; } = new ConcurrentBag<Thread>();
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();

        private static bool repeatingFails = false;

        public static void LogLine(string msg, MessageGroup group, ConsoleColor color)
        {
            string line = string.Format("{0}\t{1}\t{2}", DateTime.Now.ToString("HH:mm:ss"), $"[{group.ToString().ToUpper()}]".PadRight(8), msg);
            messages.Enqueue((line, color));
            messagesAvailable.Set();
        }

        public static void LogPixel(MessageGroup group, string msg, int x, int y, PixelColor color, ConsoleColor consoleColor)
        {
            string text = $"{msg.PadRight(22)} {color.ToString().PadRight(12)} at ({x.ToString().PadLeft(6)};{y.ToString().PadLeft(6)})";
            LogLine(text, group, consoleColor);
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

                Thread logThread = new Thread(ConsoleWriterThreadBody);
                BackgroundThreads.Add(logThread);
                logThread.Start();
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
                if (defendMode)
                {
                    gotGriefed = new AutoResetEvent(false);
                    cache.OnMapRedownloaded += (o, e) => gotGriefed.Set();
                    waitingGriefLock = new object();
                    Thread integrityThread = new Thread(IntegrityCalculationThreadBody);
                    BackgroundThreads.Add(integrityThread);
                    integrityThread.Start();
                }
                else
                {
                    Thread progressThread = new Thread(CompletionCalculationThreadBody);
                    BackgroundThreads.Add(progressThread);
                    progressThread.Start();
                }
                do
                {
                    try
                    {
                        using (InteractionWrapper wrapper = new InteractionWrapper(fingerprint))
                        {
                            wrapper.OnPixelChanged += LogPixelChanged;
                            cache.Wrapper = wrapper;
                            gotChunksDownloaded.Set();
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
                                            success = wrapper.PlacePixel(x, y, color, out double cd, out string error);
                                            if (success)
                                            {
                                                string prefix = cd == 4 ? "P" : "Rep";
                                                LogPixel(MessageGroup.Pixel, $"{prefix}laced pixel:", x, y, color, ConsoleColor.Green);
                                            }
                                            else
                                            {
                                                if (cd == 0.0)
                                                {
                                                    LogLine("Please go to browser and place pixel, then return and press any key", MessageGroup.Error, ConsoleColor.Red);
                                                    Random rnd = new Random();
                                                    int rx = rnd.Next(short.MinValue, short.MaxValue);
                                                    int ry = rnd.Next(short.MinValue, short.MaxValue);
                                                    Process.Start($"{InteractionWrapper.BaseHttpAdress}/#{rx},{ry},30");
                                                    Console.ReadKey(true);
                                                }
                                                else
                                                {
                                                    LogLine($"Failed to place pixel: {error}", MessageGroup.Pixel, ConsoleColor.Red);
                                                    if (++placingPixelFails == 3)
                                                    {
                                                        throw new Exception("Cannot place pixel 3 times");
                                                    }
                                                }
                                                
                                            }
                                            Thread.Sleep(TimeSpan.FromSeconds(cd));
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
                gotChunksDownloaded.Dispose();
                finishCTS.Dispose();
                gotGriefed?.Dispose();


                foreach (Thread thread in BackgroundThreads.Where(t => t.IsAlive))
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
                            builtInLastMinute--;
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

        //to prevent bot from stopping work when text is selected in shell
        private static void ConsoleWriterThreadBody()
        {
            while (true)
            {
                if (messages.TryDequeue(out Message msg))
                {
                    (string line, ConsoleColor color) = msg;
                    Console.ForegroundColor = color;
                    Console.WriteLine(line);
                    if (logToFile)
                    {
                        using (StreamWriter writer = new StreamWriter(logFilePath, true))
                        {
                            writer.WriteLine(line);
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

        private static void CompletionCalculationThreadBody()
        {
            try
            {
                gotChunksDownloaded.WaitOne();
            }
            catch (ThreadInterruptedException)
            {
                return;
            }
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
                catch (ThreadInterruptedException)
                {
                    return;
                }
                builtInPast.Enqueue(builtInLastMinute);
                builtInLastMinute = 0;
                if (builtInPast.Count > 5)
                {
                    builtInPast.Dequeue();
                }

                double builtPerMinute = builtInPast.Average();
                int done = pixelsToBuild.
                    Where(p => CorrectPixelColor(cache.GetPixelColor(p.Item1, p.Item2), p.Item3)).
                    Count();
                int total = pixelsToBuild.Count();
                int minsLeft = (int)Math.Round((total - done) / builtPerMinute);
                int hrsLeft = minsLeft / 60;

                LogLine($"Image is {done * 100.0 / total:F1}% complete, left approximately {hrsLeft}h {minsLeft % 60}min", MessageGroup.Info, ConsoleColor.Magenta);
            }
            while (true);
        }

        private static void IntegrityCalculationThreadBody()
        {
            try
            {
                gotChunksDownloaded.WaitOne();
            }
            catch (ThreadInterruptedException)
            {
                return;
            }
            do
            {
                try
                {
                    lock (waitingGriefLock)
                    { }
                    Thread.Sleep(TimeSpan.FromMinutes(1));
                }
                catch (ThreadInterruptedException)
                {
                    return;
                }
                int correct = pixelsToBuild.
                    Where(p => CorrectPixelColor(cache.GetPixelColor(p.Item1, p.Item2), p.Item3)).
                    Count();
                int total = pixelsToBuild.Count();
                LogLine($"Image integrity is {correct * 100.0 / total:F1}%, {total - correct} corrupted pixels", MessageGroup.Info, ConsoleColor.Magenta);
            }
            while (true);
        }

        private static string GetFingerprint(string address = null)
        {
            Guid guid = Guid.Empty;
            string path = string.Format(guidFilePathTemplate, address?.GetHashCode());
            byte[] bytes = File.ReadAllBytes(path);
            guid = new Guid(bytes);
            return guid.ToString("N");
        }

        private static void SaveFingerprint(Guid guid, string address = null)
        {
            string path = string.Format(guidFilePathTemplate, address?.GetHashCode());
            Directory.CreateDirectory(appFolder);
            File.WriteAllBytes(path, guid.ToByteArray());
        }
    }
}
