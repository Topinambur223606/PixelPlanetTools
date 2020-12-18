using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Net;
using System.Threading.Tasks;

namespace PixelPlanetUtils
{
    public static class ImageProcessing
    {
        public const double NoneColorDistance = byte.MaxValue * 2D;

        private static readonly Rgba32[] colors = new Rgba32[30]
        {
           Color.White,
           new Rgba32(228, 228, 228),
           new Rgba32(196, 196, 196),
           new Rgba32(136, 136, 136),
           new Rgba32(78, 78, 78),
           Color.Black,
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

        public static Rgba32 ToRgba32(this EarthPixelColor color)
        {
            if (color == EarthPixelColor.None)
            {
                return Color.Transparent;
            }
            if (color == EarthPixelColor.UnsetOcean)
            {
                color = EarthPixelColor.SkyBlue;
            }
            if (color == EarthPixelColor.UnsetLand)
            {
                color = EarthPixelColor.White;
            }
            return colors[(byte)color - 2];
        }

        public static double RgbCubeDistance(EarthPixelColor c1, EarthPixelColor c2)
        {
            if (c1 == EarthPixelColor.None || c2 == EarthPixelColor.None)
            {
                if (c1 == c2)
                {
                    return 0;
                }
                else
                {
                    return NoneColorDistance;
                }
            }
            Rgba32 rgb1 = c1.ToRgba32();
            Rgba32 rgb2 = c2.ToRgba32();
            int dR = rgb1.R - rgb2.R;
            int dG = rgb1.G - rgb2.G;
            int dB = rgb2.B - rgb2.B;
            return Math.Sqrt(dR * dR + dG * dG + dB * dB);
        }

        public static EarthPixelColor[,] PixelColorsByUri(string imageUri, Logger logger)
        {
            logger.LogTechState("Downloading image...");
            using (WebClient wc = new WebClient())
            {
                logger.LogDebug($"PixelColorsByUri(): URI - {imageUri}");
                byte[] data = wc.DownloadData(imageUri);
                using (Image<Rgba32> image = Image.Load(data))
                {
                    logger.LogTechInfo("Image is downloaded");
                    logger.LogTechState("Converting image...");
                    int w = image.Width;
                    int h = image.Height;
                    EarthPixelColor[,] res = new EarthPixelColor[w, h];
                    Parallel.For(0, w, x =>
                    {
                        for (int y = 0; y < h; y++)
                        {
                            res[x, y] = ClosestAvailable(image[x, y]);
                        }
                    });
                    logger.LogTechInfo("Image is converted");
                    return res;
                }
            }
        }

        private static EarthPixelColor ClosestAvailable(Rgba32 color)
        {
            if (color.A == 0)
            {
                return EarthPixelColor.None;
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
            return (EarthPixelColor)(index + 2);
        }

        public static ushort[,] GetBrightnessOrder(string imageUri, Logger logger)
        {
            logger.LogTechState("Downloading brightness order image...");
            using (WebClient wc = new WebClient())
            {
                logger.LogDebug($"GetBrightnessOrder(): URI - {imageUri}");
                byte[] data = wc.DownloadData(imageUri);
                using (Image<L16> image = Image.Load<L16>(data))
                {
                    logger.LogTechInfo("Image is downloaded");
                    logger.LogTechState("Converting image...");
                    int w = image.Width;
                    int h = image.Height;
                    ushort[,] res = new ushort[w, h];
                    Parallel.For(0, w, x =>
                    {
                        for (int y = 0; y < h; y++)
                        {
                            res[x, y] = image[x, y].PackedValue;
                        }
                    });
                    logger.LogTechInfo("Image is converted");
                    return res;
                }
            }
        }
    }
}
