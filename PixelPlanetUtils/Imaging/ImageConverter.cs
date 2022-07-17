using PixelPlanetUtils.Imaging.Exceptions;
using PixelPlanetUtils.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
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
                using (Image<Rgba32> image = Image.Load<Rgba32>(data))
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

        public async static Task<byte[,,]> VoxelColorsByCsvUri(string csvUri, Palette palette, Logger logger)
        {
            logger.LogTechState("Downloading document...");
            using (WebClient wc = new WebClient())
            {
                byte[] data = await wc.DownloadDataTaskAsync(csvUri);
                logger.LogTechInfo("Document is downloaded");
                logger.LogTechState("Converting...");
                using (MemoryStream stream = new MemoryStream(data))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        List<int> dim = reader.ReadLine().Split(',').Select(int.Parse).ToList();
                        int sx = dim[0], sy = dim[2], sz = dim[1];
                        byte[,,] res = new byte[sx, sy, sz];
                        Dictionary<string, byte> knownColors = new Dictionary<string, byte>();
                        for (int z = sz - 1; z >= 0; z--)
                        {
                            for (int y = 0; y < sy; y++)
                            {
                                string[] rowHex = reader.ReadLine().Split(',');
                                for (int x = 0; x < sx; x++)
                                {
                                    string hex = rowHex[x];
                                    if (hex.EndsWith("FF"))
                                    {
                                        if (!knownColors.TryGetValue(hex, out byte colorCode))
                                        {
                                            Rgba32 color = Rgba32.ParseHex(hex);
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

        public async static Task<byte[,,]> VoxelColorsByPngUri(string imageUri, Palette palette, Logger logger)
        {
            logger.LogTechState("Downloading image...");
            using (WebClient wc = new WebClient())
            {
                byte[] data = await wc.DownloadDataTaskAsync(imageUri);
                using (Image<Rgba32> image = Image.Load<Rgba32>(data))
                {
                    logger.LogTechInfo("Image is downloaded");
                    logger.LogTechState("Converting image...");
                    Dictionary<string, string> metadata = image.Metadata.GetPngMetadata().TextData.ToDictionary(d => d.Keyword, d => d.Value);
                    if (!(metadata.TryGetValue("SproxelFileVersion", out string version) && version == "1"))
                    {
                        throw new Exception("not the Sproxel exported PNG");
                    }
                    int sx = int.Parse(metadata["VoxelGridDimX"]);
                    //sproxel Y is bot Z and vice versa
                    int sz = int.Parse(metadata["VoxelGridDimY"]);
                    int sy = int.Parse(metadata["VoxelGridDimZ"]);

                    byte[,,] res = new byte[sx, sy, sz];
                    Dictionary<Rgba32, byte> knownColors = new Dictionary<Rgba32, byte>();
                    Parallel.For(0, sz, z =>
                    {
                        int imageY = sz - z - 1;
                        for (int y = 0; y < sy; y++)
                        {
                            for (int x = 0; x < sx; x++)
                            {
                                int imageX = y * sx + x;
                                Rgba32 color = image[imageX, imageY];
                                if (color.A > 0)
                                {
                                    if (!knownColors.TryGetValue(color, out byte colorCode))
                                    {
                                        knownColors[color] = colorCode = palette.ClosestAvailable(color);
                                    }
                                    res[x, y, z] = colorCode;
                                }
                            }
                        }
                    });

                    return res;
                }
            }
        }
    }
}
