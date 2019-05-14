using System;
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
    static partial class Program
    {
        //TODO proxy, 1 guid per proxy, save with address hash and last usage timedate, clear old

        static readonly string appFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetBot");

        static readonly string filePath = Path.Combine(appFolder, "guid.bin");

        private static PixelColor[,] Pixels;

        private static short leftX, topY;

        private static string fingerprint;

        private static HashSet<Pixel> placed = new HashSet<Pixel>();

        public static bool DefendMode { get; set; } = false;

        public static object lockObj = new object();

        private static AutoResetEvent gotGriefed = new AutoResetEvent(false);

        private static byte failsInRow = 0;

        public static void LogLineToConsole(string msg, ConsoleColor color = ConsoleColor.DarkGray)
        {
            lock (lockObj)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(msg);
            }
        }

        public static void LogPixelToConsole(string msg, int x, int y, PixelColor color, ConsoleColor consoleColor)
        {
            string text = $"{msg.PadRight(22, ' ')} {color.ToString().PadRight(12, ' ')} at ({x.ToString().PadLeft(6, ' ')};{y.ToString().PadLeft(6, ' ')})";
            LogLineToConsole(text, consoleColor);
        }

        static void Main(string[] args)
        {
            ushort width, height;
            PlacingOrderMode order = PlacingOrderMode.Random;
            try
            {
                leftX = short.Parse(args[0]);
                topY = short.Parse(args[1]);
                if (args.Length > 3)
                {
                    DefendMode = args[3].ToLower() == "y";
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
                using (WebClient wc = new WebClient())
                {
                    byte[] data = wc.DownloadData(args[2]);
                    MemoryStream ms = new MemoryStream(data);
                    image = new Bitmap(ms);
                }
                Pixels = ImageProcessing.ToPixelWorldColors(image);
                checked
                {
                    width = (ushort)image.Width;
                    height = (ushort)image.Height;
                    short check;
                    check = (short)(leftX + width);
                    check = (short)(topY + height);
                }

            }
            catch
            {
                Console.WriteLine("Parameters: [left x: -32768..32767] [top y: -32768..32767] [image URL] [defend mode: Y/N = N]" + Environment.NewLine +
                    "Image should fit into map");
                return;
            }
            fingerprint = GetFingerprint();
            IEnumerable<int> allY = Enumerable.Range(0, height);
            IEnumerable<int> allX = Enumerable.Range(0, width);
            Pixel[] nonEmptyPixels = allX.
                SelectMany(X => allY.Select(Y =>
                    (X: (short)(X + leftX), Y: (short)(Y + topY), C: Pixels[X, Y]))).
                Where(xy => xy.C != PixelColor.None).ToArray();
            IEnumerable<Pixel> pixelsToCheck;
            switch (order)
            {
                case PlacingOrderMode.FromLeft:
                    pixelsToCheck = nonEmptyPixels.OrderBy(xy => xy.Item1).ToList();
                    break;
                case PlacingOrderMode.FromRight:
                    pixelsToCheck = nonEmptyPixels.OrderByDescending(xy => xy.Item1).ToList();
                    break;
                case PlacingOrderMode.FromTop:
                    pixelsToCheck = nonEmptyPixels.OrderBy(xy => xy.Item2).ToList();
                    break;
                case PlacingOrderMode.FromBottom:
                    pixelsToCheck = nonEmptyPixels.OrderByDescending(xy => xy.Item2).ToList();
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
                    pixelsToCheck = nonEmptyPixels;
                    break;
            }
            do
            {
                placed.Clear();
                try
                {
                    using (InteractionWrapper wrapper = new InteractionWrapper(fingerprint))
                    {
                        wrapper.OnPixelChanged += Wrapper_OnPixelChanged;
                        ChunkCache cache = new ChunkCache(leftX, topY, width, height, wrapper);
                        bool wasChanged;
                        do
                        {
                            wasChanged = false;
                            failsInRow = 0;
                            foreach (Pixel pixel in pixelsToCheck)
                            {
                                (short x, short y, PixelColor color) = pixel;
                                PixelColor actualColor = cache.GetPixel(x, y);
                                if (color != actualColor)
                                {
                                    wasChanged = true;
                                    bool success;
                                    placed.Add(pixel);
                                    do
                                    {
                                        success = wrapper.PlacePixel(x, y, color, out double cd);
                                        Task.Delay(TimeSpan.FromSeconds(cd)).Wait();
                                    } while (!success);
                                }
                            }
                            if (DefendMode)
                            {
                                if (wasChanged)
                                {
                                    LogLineToConsole("Building iteration finished", ConsoleColor.Green);
                                }
                                else
                                {
                                    LogLineToConsole("No changes were made, waiting...", ConsoleColor.Green);
                                    gotGriefed.WaitOne();
                                }
                            }
                            else
                            {
                                LogLineToConsole("Building finished", ConsoleColor.Green);
                            }
                        }
                        while (DefendMode);
                    }
                }
                catch (Exception ex)
                {
                    LogLineToConsole($"Unhandled exception:" + Environment.NewLine + ex.Message, ConsoleColor.Red);
                    if (++failsInRow < 3)
                    {
                        LogLineToConsole("Reconnecting in 1 min...", ConsoleColor.Yellow);
                        Task.Delay(TimeSpan.FromMinutes(1D)).Wait();
                        continue;
                    }
                    else
                    {
                        LogLineToConsole("Cannot reconnect 3 times in a row, process is restarting...", ConsoleColor.Red);
                        string fullPath = Process.GetCurrentProcess().MainModule.FileName;
                        args[2] = $"\"{args[2]}\"";
                        Process.Start(fullPath, string.Join(" ", args));
                    }
                }
                Environment.Exit(0);
            } while (true);
        }

        private static void Wrapper_OnPixelChanged(object sender, PixelChangedEventArgs e)
        {
            ConsoleColor msgColor;
            short x = PixelMap.ConvertToAbsolute(e.Chunk.Item1, e.Pixel.Item1);
            short y = PixelMap.ConvertToAbsolute(e.Chunk.Item2, e.Pixel.Item2);

            if (!placed.Remove((x, y, e.Color)))
            {
                switch (EstimateUpdate(x, y, e.Color))
                {
                    case PixelUpdateStatus.Desired:
                        msgColor = ConsoleColor.Green;
                        break;
                    case PixelUpdateStatus.Wrong:
                        msgColor = ConsoleColor.Red;
                        gotGriefed.Set();
                        break;
                    default:
                        msgColor = ConsoleColor.DarkGray;
                        break;
                }
                LogPixelToConsole($"Received pixel update:", x, y, e.Color, msgColor);
            }
        }

        private static PixelUpdateStatus EstimateUpdate(short x, short y, PixelColor color)
        {
            try
            {
                PixelColor desiredColor = Pixels[x - leftX, y - topY];
                if (desiredColor ==  PixelColor.None)
                {
                    return PixelUpdateStatus.Outer;
                }
                else
                {
                    if (desiredColor == color)
                    {
                        return PixelUpdateStatus.Desired;
                    }
                    else
                    {
                        return PixelUpdateStatus.Wrong;
                    }
                }
            }
            catch
            {
                return PixelUpdateStatus.Outer;
            }
        }


        private static string GetFingerprint()
        {
            Guid guid = Guid.Empty;
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                guid = new Guid(bytes);
            }
            catch
            {
                Directory.CreateDirectory(appFolder);
                guid = Guid.NewGuid();
                File.WriteAllBytes(filePath, guid.ToByteArray());
            }
            return guid.ToString("N");
        }
    }
}
