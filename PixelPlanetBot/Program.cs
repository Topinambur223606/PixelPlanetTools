using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
    static class Program
    {
        const string fileName = "guid.bin";

        static Guid userGuid;
        
        static string Fingerprint => userGuid.ToString("N");

        static readonly Color[] colors = new Color[24]
        {
            Color.White,
            Color.FromArgb(228, 228, 228),
            Color.FromArgb(136,136,136),
            Color.FromArgb(78,78,78),
            Color.Black,
            Color.FromArgb(244,179,174),
            Color.FromArgb(255,167,209),
            Color.FromArgb(255,101,101),
            Color.FromArgb(229,0,0),
            Color.FromArgb(254,164,96),
            Color.FromArgb(229,149,0),
            Color.FromArgb(160,106,66),
            Color.FromArgb(245,223,176),
            Color.FromArgb(229,217,0),
            Color.FromArgb(148,224,68),
            Color.FromArgb(2,190,1),
            Color.FromArgb(0,101,19),
            Color.FromArgb(202,227,255),
            Color.FromArgb(0,211,221),
            Color.FromArgb(0,131,199),
            Color.FromArgb(0,0,234),
            Color.FromArgb(25,25,115),
            Color.FromArgb(207,110,228),
            Color.FromArgb(130,0,128)
        };


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
                pixelsTask = ToPixelWorldColors(image);
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

                ConcurrentQueue<Task> tasks = new ConcurrentQueue<Task>();
                Task lastTask = Task.CompletedTask;
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
                                Task task = lastTask.ContinueWith(t =>
                                {
                                    var actualColor = cache.GetPixel(x, y);
                                    if (color != actualColor)
                                    {
                                        changed = true;
                                        double cd = wrapper.PlacePixel(x, y, color);
                                        Task.Delay(TimeSpan.FromSeconds(cd)).Wait();
                                    }
                                    tasks.TryDequeue(out _);
                                });
                                tasks.Enqueue(task);
                                lastTask = task;
                            }
                        }
                    }
                    lastTask.Wait();
                    if (continuous)
                    {
                        Console.WriteLine("Building iteration finished");
                        if (!changed)
                        {
                            Console.WriteLine("No changes was made, waiting 10s before next check");
                            Task.Delay(TimeSpan.FromSeconds(10)).Wait();
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
            if (File.Exists(fileName))
            {
                byte[] bytes = File.ReadAllBytes(fileName);
                if (bytes.Length == 16)
                {
                    userGuid = new Guid(bytes);
                    return;
                }
            }
            userGuid = Guid.NewGuid();
            File.WriteAllBytes(fileName, userGuid.ToByteArray());
        }
        private static PixelColor ClosestAvailable(Color color)
        {
            if (color.A == 0)
            {
                return PixelColor.None;
            }
            int index = 0, d = 260000;
            foreach ((int i, int) diff in colors.Select((c, i) =>
                {
                    int dr = c.R - color.R,
                    dg = c.G - color.G,
                    db = c.B - color.B;
                    return (i, dr * dr + dg * dg + db * db);
                }))
            {
                if (diff.Item2 < d)
                {
                    (index, d) = diff;
                }
            }
            return (PixelColor)(index + 2);
        }

        private static Task<PixelColor[,]> ToPixelWorldColors(Bitmap image)
        {
            int w = image.Width;
            int h = image.Height;
            PixelColor[,] res = new PixelColor[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    res[x, y] = ClosestAvailable(image.GetPixel(x, y));
                }
            }
            return Task.FromResult(res);
        }
    }
}
