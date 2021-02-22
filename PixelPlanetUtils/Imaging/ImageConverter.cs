using PixelPlanetUtils.Imaging.Exceptions;
using PixelPlanetUtils.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace PixelPlanetUtils.Imaging
{
    public static class ImageConverter
    {
        public async static Task<byte[,]> PixelColorsByUri(string imageUri, Palette palette, Logger logger)
        {
            logger.LogTechState("Downloading image...");
            using (WebClient wc = new WebClient())
            {
                byte[] data = await wc.DownloadDataTaskAsync(imageUri);
                using (Image<Rgba32> image = Image.Load(data))
                {
                    logger.LogTechInfo("Image is downloaded");
                    logger.LogTechState("Converting image...");
                    int w = image.Width;
                    int h = image.Height;
                    byte[,] res = new byte[w, h];
                    ConcurrentDictionary<Rgba32, byte> knownColors = new ConcurrentDictionary<Rgba32, byte>();
                    Parallel.For(0, w, x =>
                    {
                        for (int y = 0; y < h; y++)
                        {
                            Rgba32 rgb = image[x, y];
                            if (!knownColors.TryGetValue(rgb, out byte colorCode))
                            {
                                colorCode = palette.ClosestAvailable(rgb);
                                knownColors.TryAdd(rgb, colorCode);
                            }
                            res[x, y] = colorCode;
                        }
                    });
                    logger.LogTechInfo("Image is converted");
                    return res;
                }
            }
        }

        public async static Task<byte[,,]> VoxelColorsByUri(string csvUri, Palette palette, Logger logger)
        {
            logger.LogTechState("Downloading document...");
            using (WebClient wc = new WebClient())
            {
                byte[] data = await wc.DownloadDataTaskAsync(csvUri);
                logger.LogTechInfo("Document is downloaded");
                logger.LogTechState("Converting...");
                using (var stream = new MemoryStream(data))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        var dim = reader.ReadLine().Split(',').Select(int.Parse).ToList();
                        int sx = dim[0], sy = dim[2], sz = dim[1];
                        byte[,,] res = new byte[sx, sy, sz];
                        Dictionary<string, byte> knownColors = new Dictionary<string, byte>();
                        for (int z = sz - 1; z >= 0; z--)
                        {
                            for (int y = 0; y < sy; y++)
                            {
                                var rowHex = reader.ReadLine().Split(',');
                                for (int x = 0; x < sx; x++)
                                {
                                    var hex = rowHex[x];
                                    if (hex.EndsWith("FF"))
                                    {
                                        if (!knownColors.TryGetValue(hex, out byte colorCode))
                                        {
                                            var color = Rgba32.ParseHex(hex);
                                            knownColors[hex] = colorCode = palette.ClosestAvailable(color);
                                        }
                                        res[x, y, z] = colorCode;
                                    }
                                }
                            }
                            reader.ReadLine();
                        }
                        return res;
                    }
                }
            }
        }

        public async static Task<ushort[,]> GetBrightnessOrderMask(string maskUri, Logger logger, int width, int height)
        {
            logger.LogTechState("Downloading brightness order mask...");
            using (WebClient wc = new WebClient())
            {
                byte[] data = await wc.DownloadDataTaskAsync(maskUri);
                using (Image<L16> image = Image.Load<L16>(data))
                {
                    logger.LogTechInfo("Mask is downloaded");
                    logger.LogTechState("Converting mask...");
                    int w = image.Width;
                    int h = image.Height;

                    if (width != w || height != h)
                    {
                        throw new MaskInvalidSizeException();
                    }

                    ushort[,] res = new ushort[w, h];
                    Parallel.For(0, w, x =>
                    {
                        for (int y = 0; y < h; y++)
                        {
                            res[x, y] = image[x, y].PackedValue;
                        }
                    });
                    logger.LogTechInfo("Mask is converted");
                    return res;
                }
            }
        }
    }
}
