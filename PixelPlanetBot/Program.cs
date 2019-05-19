using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private static readonly string guidFilePathTemplate = Path.Combine(appFolder, "guid{0}.bin");

        private static bool defendMode;
        private static PixelColor[,] imagePixels;
        private static IEnumerable<Pixel> pixelsToBuild;
        private static short leftX, topY;
        private static ChunkCache cache;

        private static AutoResetEvent gotGriefed;
        private static AutoResetEvent gotChunksDownloaded;

        private static readonly HashSet<Pixel> placed = new HashSet<Pixel>();
        private static readonly ConcurrentQueue<Message> messages = new ConcurrentQueue<Message>();
        private static readonly AutoResetEvent messagesAvailable = new AutoResetEvent(false);

        private static volatile int builtInLastMinute = 0;
        private static readonly Queue<int> builtInPast = new Queue<int>();

        public static ConcurrentBag<Thread> BackgroundThreads { get; } = new ConcurrentBag<Thread>();
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();


        private static bool repeatingFails = false;

        public static void LogLineToConsole(string msg, ConsoleColor color = ConsoleColor.DarkGray)
        {
            string line = string.Format("{0}\t{1}", DateTime.Now.ToString("HH:mm:ss"), msg);
            messages.Enqueue((line, color));
            messagesAvailable.Set();
        }

        public static void LogPixelToConsole(string msg, int x, int y, PixelColor color, ConsoleColor consoleColor)
        {
            string text = $"{msg.PadRight(22, ' ')} {color.ToString().PadRight(12, ' ')} at ({x.ToString().PadLeft(6, ' ')};{y.ToString().PadLeft(6, ' ')})";
            LogLineToConsole(text, consoleColor);
        }

        private static void Main(string[] args)
        {
            try {
                ushort width, height;
                PlacingOrderMode order = PlacingOrderMode.Random;
                try
                {
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
                    Bitmap image;
                    Console.Write("{0}\tDownloading image... ", DateTime.Now.ToString("HH:mm:ss"));
                    using (WebClient wc = new WebClient())
                    {
                        byte[] data = wc.DownloadData(args[2]);
                        MemoryStream ms = new MemoryStream(data);
                        image = new Bitmap(ms);
                    }
                    Console.Write("Done!{0}{1}\tConverting image... ", Environment.NewLine, DateTime.Now.ToString("HH:mm:ss"));
                    imagePixels = ImageProcessing.ToPixelWorldColors(image);
                    Console.WriteLine("Done!");

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
                    Console.WriteLine("All image should be inside the map");
                    return;
                }
                catch (WebException)
                {
                    Console.WriteLine("Cannot download image");
                    return;
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("Cannot convert image");
                    return;
                }
                catch
                {
                    Console.WriteLine("Parameters: <leftX: -32768..32767> <topY: -32768..32767> <imageURL> [defendMode: Y/N = N] [buildFrom L/R/T/B/RND = RND] [proxyIP:proxyPort = nothing]");
                    return;
                }
                Thread logThread = new Thread(ConsoleWriterThreadBody);
                BackgroundThreads.Add(logThread);
                logThread.Start();
                string fingerprint = GetFingerprint();
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
                }
                else
                {
                    gotChunksDownloaded = new AutoResetEvent(false);
                    Thread calcThread = new Thread(CompletionCalculationThreadBody);
                    BackgroundThreads.Add(calcThread);
                    calcThread.Start();
                }
                do
                {
                    try
                    {
                        using (InteractionWrapper wrapper = new InteractionWrapper(fingerprint))
                        {
                            wrapper.OnPixelChanged += LogPixelChanged;
                            cache.Wrapper = wrapper;
                            if (!defendMode)
                            {
                                gotChunksDownloaded.Set();
                            }
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
                                                LogPixelToConsole($"{prefix}laced pixel:", x, y, color, ConsoleColor.Green);
                                            }
                                            else
                                            {
                                                if (++placingPixelFails == 3)
                                                {
                                                    throw new Exception("Cannot place pixel 3 times");
                                                }
                                                LogLineToConsole($"Failed to place pixel: {error}", ConsoleColor.Red);
                                            }
                                            Thread.Sleep(TimeSpan.FromSeconds(cd));
                                        } while (!success);
                                    }
                                }
                                if (defendMode)
                                {
                                    if (!wasChanged)
                                    {
                                        gotGriefed.Reset();
                                        LogLineToConsole("Image is intact, waiting...", ConsoleColor.Green);
                                        gotGriefed.WaitOne();
                                        Thread.Sleep(new Random().Next(500, 3000));
                                    }
                                }
                            }
                            while (defendMode || wasChanged);
                            finishCTS.Cancel();
                            LogLineToConsole("Building is finished, exiting...", ConsoleColor.Green);
                            Thread.Sleep(1000);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogLineToConsole($"Unhandled exception: {ex.Message}", ConsoleColor.Red);
                        int delay = repeatingFails ? 30 : 10;
                        repeatingFails = true;
                        LogLineToConsole($"Reconnecting in {delay} seconds...", ConsoleColor.Yellow);
                        Thread.Sleep(TimeSpan.FromSeconds(delay));
                        continue;
                    }
                } while (true);
            }
            finally
            {
                gotGriefed?.Dispose();
                gotChunksDownloaded?.Dispose();
                finishCTS.Dispose();

                foreach (var thread in BackgroundThreads.Where(t => t.IsAlive))
                {
                    thread.Interrupt(); //fallback, should never work 
                }
            }
        }

        private static bool CorrectPixelColor(PixelColor actualColor, PixelColor desiredColor)
        {
            return (actualColor == desiredColor) ||
                    (actualColor == PixelColor.NothingOcean && desiredColor == PixelColor.LightestBlue) ||
                    (actualColor == PixelColor.NothingLand && desiredColor == PixelColor.White);
        }

        private static void LogPixelChanged(object sender, PixelChangedEventArgs e)
        {
            ConsoleColor msgColor;
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
                    }
                    else
                    {
                        if (desiredColor == e.Color)
                        {
                            msgColor = ConsoleColor.Green;
                            builtInLastMinute++;
                        }
                        else
                        {
                            msgColor = ConsoleColor.Red;
                            gotGriefed?.Set();
                        }
                    }
                }
                catch
                {
                    msgColor = ConsoleColor.DarkGray;
                }
                LogPixelToConsole($"Received pixel update:", x, y, e.Color, msgColor);
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

                LogLineToConsole($"Image is {done * 100.0 / total:F1}% complete, left approximately {hrsLeft}h {minsLeft % 60}min", ConsoleColor.Magenta);
            }
            while (true);
        }

        private static string GetFingerprint(string address = null)
        {
            Guid guid = Guid.Empty;
            string path = string.Format(guidFilePathTemplate, address?.GetHashCode());
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                guid = new Guid(bytes);
            }
            catch
            {
                Directory.CreateDirectory(appFolder);
                guid = Guid.NewGuid();
                File.WriteAllBytes(path, guid.ToByteArray());
            }
            return guid.ToString("N");
        }
    }
}
