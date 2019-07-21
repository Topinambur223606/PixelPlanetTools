using System;
using System.Net;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelPlanetUtils
{
    public static class ImageProcessing
    {
        private static readonly Rgba32[] colors = new Rgba32[30]
        {
           Rgba32.White,
           new Rgba32(228, 228, 228),
           new Rgba32(196, 196, 196),
           new Rgba32(136, 136, 136),
           new Rgba32(78, 78, 78),
           Rgba32.Black,
           new Rgba32(244, 179, 174),
           new Rgba32(255, 167, 209),
           new Rgba32(255,  84, 178),
           new Rgba32(255, 101, 101),
           new Rgba32(229, 0, 0),
           new Rgba32(154, 0, 0),
           new Rgba32(254, 164, 96),
           new Rgba32(229, 149, 0),
           new Rgba32(160, 106, 66),
           new Rgba32(96, 64, 40),
           new Rgba32(245, 223, 176),
           new Rgba32(255, 248, 137),
           new Rgba32(229, 217, 0),
           new Rgba32(148, 224, 68),
           new Rgba32(2, 190, 1),
           new Rgba32(104, 131, 56),
           new Rgba32(0, 101, 19),
           new Rgba32(202, 227, 255),
           new Rgba32(0, 211, 221),
           new Rgba32(0, 131, 199),
           new Rgba32(0, 0, 234),
           new Rgba32(25, 25, 115),
           new Rgba32(207, 110, 228),
           new Rgba32(130, 0, 128)
        };

        public static Rgba32 ToRgba32(this PixelColor color)
        {
            if (color == PixelColor.None)
            {
                return Rgba32.Transparent;
            }
            if (color == PixelColor.UnsetOcean)
            {
                color = PixelColor.SkyBlue;
            }
            if (color == PixelColor.UnsetLand)
            {
                color = PixelColor.White;
            }
            return colors[(byte)color - 2];
        }

        private static PixelColor ClosestAvailable(Rgba32 color)
        {
            if (color.A == 0)
            {
                return PixelColor.None;
            }
            int index = 0, bestD = 260000;
            for (int i = 0; i < colors.Length; i++)
            {
                Rgba32 c = colors[i];
                int dr = c.R - color.R,
                dg = c.G - color.G,
                db = c.B - color.B;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD)
                {
                    index = i;
                    bestD = d;
                }
            }
            return (PixelColor)(index + 2);
        }

        public static PixelColor[,] PixelColorsByUri(string imageUri, Action<string, MessageGroup> logger)
        {
            logger?.Invoke("Downloading image...", MessageGroup.TechState);
            using (WebClient wc = new WebClient())
            {
                using (Image<Rgba32> image = Image.Load(wc.DownloadData(imageUri)))
                {
                    logger?.Invoke("Image is downloaded", MessageGroup.TechInfo);
                    logger?.Invoke("Converting image...", MessageGroup.TechState);
                    int w = image.Width;
                    int h = image.Height;
                    PixelColor[,] res = new PixelColor[w, h];
                    Parallel.For(0, w, x =>
                    {
                        for (int y = 0; y < h; y++)
                        {
                            res[x, y] = ClosestAvailable(image[x, y]);
                        }
                    });
                    logger?.Invoke("Image is converted", MessageGroup.TechInfo);
                    return res;
                }
            }
        }
    }
}
