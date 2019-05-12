using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
    static partial class Program
    {
        static readonly string appFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetBot");

        static readonly string filePath = Path.Combine(appFolder, "guid.bin");

        static Guid userGuid;
        
        static string Fingerprint => userGuid.ToString("N");

        //TODO proxy, 1 guid per proxy, save with address hash and last usage timedate, clear old
        //TODO side (U,D,L,R)
		//TODO random pixel order

        static void Main(string[] args)
        {
            Bitmap image = null;
            short leftX, topY;
            bool continuous;
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
                continuous = (args.Length > 3) && (args[3].ToLower() == "y");
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

                    for (int i = 0; i < w; i++)
                    {
                        for (int j = 0; j < h; j++)
                        {
                            PixelColor color = pixels[i, j];
                            short x = (short)(leftX + i),
                                y = (short)(topY + j);
                            if (color != PixelColor.None)
                            {
                                var actualColor = cache.GetPixel(x, y);
                                if (color != actualColor)
                                {
                                    changed = true;
                                    double cd = wrapper.PlacePixel(x, y, color);
                                    Task.Delay(TimeSpan.FromSeconds(cd)).Wait();
                                }
                            }
                        }
                    }
                    if (continuous)
                    {
                        Console.WriteLine("Building iteration finished");
                        if (!changed)
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
