using CommandLine;
using PixelPlanetUtils;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.Updates;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RecordVisualizer
{
    using Pixel = ValueTuple<short, short, EarthPixelColor>;

    static class Program
    {
        private static int w, h;
        private static DateTime startTime;
        private static EarthPixelColor[,] initialMapState;
        private static short leftX, topY, rightX, bottomY;
        private static readonly List<Delta> deltas = new List<Delta>();

        private static Logger logger;
        private static VisualizerOptions options;

        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();

        private static void Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args))
                {
                    return;
                }

                logger = new Logger(options.LogFilePath, finishCTS.Token)
                {
                    ShowDebugLogs = options.ShowDebugLogs
                };

                if (!options.DisableUpdates)
                {
                    if (UpdateChecker.IsStartingUpdate(logger))
                    {
                        return;
                    }
                }

                logger.LogTechState("Loading data from file...");
                try
                {
                    LoadFile(options.FileName);
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error during loading file: {ex.Message}");
                }
                logger.LogInfo($"File is loaded: {w}x{h}, {deltas.Count + 1} frames");

                DirectoryInfo dir = Directory.CreateDirectory($"seq_{startTime:MM.dd_HH-mm}");
                string pathTemplate = Path.Combine(dir.FullName, "{0:MM.dd_HH-mm-ss}.png");
                try
                {
                    SaveLoadedData(pathTemplate);
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error during saving results: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError($"Unhandled app level exception: {ex.Message}");
            }
            finally
            {
                logger?.LogInfo("Exiting...");
                finishCTS.Cancel();
                if (logger != null)
                {
                    Thread.Sleep(500);
                }
                logger?.Dispose();
                finishCTS.Dispose();
                Console.ForegroundColor = ConsoleColor.White;
                Environment.Exit(0);
            }
        }

        private static bool ParseArguments(string[] args)
        {
            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<VisualizerOptions>(args)
                    .WithNotParsed(e => success = false)
                    .WithParsed(o =>
                    {
                        options = o;
                        if (!File.Exists(o.FileName))
                        {
                            Console.WriteLine("File does not exist");
                            success = false;
                        }
                    });
                return success;
            }
        }

        private static void LoadFile(string path)
        {
            using (FileStream fileStream = File.OpenRead(path))
            {
                if (options.OldRecordFile)
                {
                    using (BinaryReader reader = new BinaryReader(fileStream))
                    {
                        reader.ReadMap();
                        logger.LogDebug($"LoadFile(): stream position {fileStream.Position}/{fileStream.Length}");
                        while (!reader.ReachedEnd())
                        {
                            deltas.Add(reader.ReadDelta());
                            logger.LogDebug($"LoadFile(): stream position {fileStream.Position}/{fileStream.Length}");
                        }
                    }
                }
                else
                {
                    byte[] buffer = new byte[sizeof(uint)];
                    fileStream.Read(buffer, 0, sizeof(uint));
                    uint mapLength = BitConverter.ToUInt32(buffer, 0);
                    logger.LogDebug($"LoadFile(): compressed map length is {sizeof(uint)}+{mapLength}, total stream length is {fileStream.Length}");
                    using (DeflateStream decompressingStream = new DeflateStream(fileStream, CompressionMode.Decompress, true))
                    {
                        using (BinaryReader reader = new BinaryReader(decompressingStream, Encoding.Default, true))
                        {
                            reader.ReadMap();
                        }
                    }
                    logger.LogDebug($"LoadFile(): seeking for deltas at {sizeof(uint)}+{mapLength}");
                    fileStream.Seek(sizeof(uint) + mapLength, SeekOrigin.Begin);
                    using (BinaryReader reader = new BinaryReader(fileStream))
                    {
                        while (fileStream.Length - fileStream.Position > 1)
                        {
                            deltas.Add(reader.ReadDelta());
                            logger.LogDebug($"LoadFile(): stream position {fileStream.Position}/{fileStream.Length}");
                        }
                    }
                }
            }
        }

        private static void SaveLoadedData(string filePathTemplate)
        {
            string initMapFileName = string.Format(filePathTemplate, startTime);
            Thread initialMapSavingThread = new Thread(() => SaveInitialMap(initMapFileName));
            initialMapSavingThread.Start();

            int padLength = 1 + (int)Math.Log10(deltas.Count);
            Parallel.For(0, deltas.Count, index =>
            {
                Delta delta = deltas[index++];
                using (Image<Rgba32> image = new Image<Rgba32>(w, h))
                {
                    foreach (Pixel pixel in delta.Pixels)
                    {
                        image[pixel.Item1 - leftX, pixel.Item2 - topY] = pixel.Item3.ToRgba32();
                    }
                    string fileName = string.Format(filePathTemplate, delta.DateTime);
                    logger.LogDebug($"SaveLoadedData(): saving delta {index} to {fileName}");
                    image.Save(fileName);
                    logger.LogInfo($"Saved delta {index.ToString().PadLeft(padLength)}/{deltas.Count}");
                }
            });

            initialMapSavingThread.Join();
        }

        private static void SaveInitialMap(string filename)
        {
            using (Image<Rgba32> image = new Image<Rgba32>(w, h))
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        image[x, y] = initialMapState[y, x].ToRgba32();
                    }
                }
                logger.LogDebug($"SaveLoadedData(): saving initial map to {filename}");
                image.Save(filename);
                logger.LogInfo("Initial map state image created");
            }
        }

        private static bool ReachedEnd(this BinaryReader reader) => reader.PeekChar() == -1;

        private static void ReadMap(this BinaryReader reader)
        {
            leftX = reader.ReadInt16();
            topY = reader.ReadInt16();
            rightX = reader.ReadInt16();
            bottomY = reader.ReadInt16();
            w = rightX - leftX + 1;
            h = bottomY - topY + 1;
            startTime = DateTime.FromBinary(reader.ReadInt64());
            byte[] data = reader.ReadBytes(w * h);
            initialMapState = BinaryConversion.ConvertToColorRectangle(data, h, w);
        }

        private static Delta ReadDelta(this BinaryReader reader)
        {
            DateTime dateTime = DateTime.FromBinary(reader.ReadInt64());
            uint count = reader.ReadUInt32();
            List<(short, short, EarthPixelColor)> pixels = new List<Pixel>();
            for (int j = 0; j < count; j++)
            {
                short x = reader.ReadInt16();
                short y = reader.ReadInt16();
                EarthPixelColor color = (EarthPixelColor)reader.ReadByte();
                pixels.Add((x, y, color));
            }
            return new Delta
            {
                DateTime = dateTime,
                Pixels = pixels
            };
        }
    }
}
