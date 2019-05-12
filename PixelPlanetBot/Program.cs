using System;
using System.Collections;
using System.Collections.Concurrent;
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
        static readonly string appFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetBot");

        static readonly string filePath = Path.Combine(appFolder, "guid.bin");

        static Guid userGuid;
        
        static string Fingerprint => userGuid.ToString("N");

        //TODO proxy, 1 guid per proxy, save with address hash and last usage timedate, clear old

        static void Main(string[] args)
        {
            Bitmap image = null;
            short leftX, topY;
            bool continuous = false;
            PlacingOrderMode order = PlacingOrderMode.Random;
            Task<PixelColor[,]> pixelsTask;
            try
            {
                leftX = short.Parse(args[0]);
                topY = short.Parse(args[1]);
                using (WebClient wc = new WebClient())
                {
                    byte[] data = wc.DownloadData(args[2]);
                    MemoryStream ms = new MemoryStream(data);
                    image = new Bitmap(ms);
                }
                if (args.Length > 3)
                {
                    continuous = args[3].ToLower() == "y";
                }
                if (args.Length > 4)
                {
                    switch (args[4].ToLower())
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
                pixelsTask = ImageProcessing.ToPixelWorldColors(image);
            }
            catch
            {
                Console.WriteLine("Parameters: [left x: -32768..32767] [top y: -32768..32767] [image URL] [defend mode: Y/N = N]");
                return;
            }
            try
            {
                SetUserGuid();
                InteractionWrapper wrapper = new InteractionWrapper(Fingerprint);
                ushort w, h;
                try
                {
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
                    throw new Exception("Out of the range, check image size and coordinates");
                }
                ChunkCache cache = new ChunkCache(leftX, topY, w, h, wrapper);
                PixelColor[,] pixels = pixelsTask.Result;


                do
                {
                    bool changed = false;
                    IEnumerable<int> allY = Enumerable.Range(0, h);
                    IEnumerable<int> allX = Enumerable.Range(0, w);
                    Pixel[] nonEmptyPixels = allX.
                        SelectMany(X => allY.Select(Y =>
                            (X: (short)(X + leftX), Y: (short)(Y + topY), C: pixels[X, Y]))).
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

                    foreach ((short x, short y, PixelColor color) in pixelsToCheck)
                    {
                        PixelColor actualColor = cache.GetPixel(x, y);
                        if (color != actualColor)
                        {
                            changed = true;
                            double cd = wrapper.PlacePixel(x, y, color);
                            Task.Delay(TimeSpan.FromSeconds(cd)).Wait();
                        }
                    }
                    if (continuous)
                    {
                        if (changed)
                        {
                            Console.WriteLine("Building iteration finished");
                        }
                        else
                        {
                            Console.WriteLine("No changes was made, waiting 1 min before next check");
                            Task.Delay(TimeSpan.FromMinutes(1D)).Wait();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Building finished");
                    }
                }
                while (continuous);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return;
            }

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
