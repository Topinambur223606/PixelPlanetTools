using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
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

        static Guid userGuid;

        static string Fingerprint => userGuid.ToString("N");

        private static PixelColor[,] Pixels;

        private static short leftX, topY;

        public static bool DefendMode { get; set; } = false;

        public static bool EmptyLastIteration { get; set; }

        public static object lockObj = new object();

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

        public static bool BelongsToPicture(short x, short y)
        {
            return Pixels[x - leftX, y - topY] != PixelColor.None;
        }

        static void Main(string[] args)
        {
            ushort w, h;
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
                    w = (ushort)image.Width;
                    h = (ushort)image.Height;
                    short check;
                    check = (short)(leftX + w);
                    check = (short)(topY + h);
                }

            }
            catch
            {
                Console.WriteLine("Parameters: [left x: -32768..32767] [top y: -32768..32767] [image URL] [defend mode: Y/N = N]" + Environment.NewLine +
                    "Image should fit into map");
                return;
            }
            IEnumerable<int> allY = Enumerable.Range(0, h);
            IEnumerable<int> allX = Enumerable.Range(0, w);
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
                try
                {
                    SetUserGuid();
                    InteractionWrapper wrapper = new InteractionWrapper(Fingerprint);
                    ChunkCache cache = new ChunkCache(leftX, topY, w, h, wrapper);
                    do
                    {
                        EmptyLastIteration = true;
                        foreach ((short x, short y, PixelColor color) in pixelsToCheck)
                        {
                            PixelColor actualColor = cache.GetPixel(x, y);
                            if (color != actualColor)
                            {
                                EmptyLastIteration = false;
                                double cd = wrapper.PlacePixel(x, y, color);
                                Task.Delay(TimeSpan.FromSeconds(cd)).Wait();
                            }
                        }
                        if (DefendMode)
                        {
                            if (!EmptyLastIteration)
                            {
                                LogLineToConsole("Building iteration finished");
                            }
                            else
                            {
                                LogLineToConsole("No changes were made, waiting 1 min before next check", ConsoleColor.Green);
                                Task.Delay(TimeSpan.FromMinutes(1D)).Wait();
                            }
                        }
                        else
                        {
                            LogLineToConsole("Building finished", ConsoleColor.Green);
                        }
                    }
                    while (DefendMode);
                }
                catch (Exception ex)
                {
                    LogLineToConsole($"Unhandled exception" + Environment.NewLine + ex.Message, ConsoleColor.Red);
                    LogLineToConsole("Restarting in 3 seconds...", ConsoleColor.Yellow);
                    Task.Delay(TimeSpan.FromSeconds(3D)).Wait();
                    break;
                }
                Environment.Exit(0);
            } while (true);
        }

        private static void SetUserGuid()
        {
            if (File.Exists(filePath))
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 16)
                {
                    userGuid = new Guid(bytes);
                    return;
                }
            }
            else
            {
                Directory.CreateDirectory(appFolder);
                userGuid = Guid.NewGuid();
                File.WriteAllBytes(filePath, userGuid.ToByteArray());
            }
        }
    }
}
