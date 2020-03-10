using CommandLine;
using PixelPlanetUtils;
using PixelPlanetUtils.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace RecordVisualizer
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    static class Program
    {
        private static short leftX, topY, rightX, bottomY;
        private static int w, h;
        private static DateTime startTime;
        private static PixelColor[,] initialMapState;
        private static readonly List<Delta> deltas = new List<Delta>();
        private static Options options;
        private static Logger logger;
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
                    if (CheckForUpdates())
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
                parser.ParseArguments<Options>(args)
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

        private static void SaveLoadedData(string filePathTemplate)
        {
            using (Image<Rgba32> image = new Image<Rgba32>(w, h))
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        image[x, y] = initialMapState[x, y].ToRgba32();
                    }
                }
                image.Save(string.Format(filePathTemplate, startTime));
            }
            logger.LogInfo("Initial map state image created");
            int d = 0;
            int padLength = 1 + (int)Math.Log10(deltas.Count);
            foreach (Delta delta in deltas)
            {
                d++;
                using (Image<Rgba32> image = new Image<Rgba32>(w, h))
                {
                    foreach ((short, short, PixelColor) pixel in delta.Pixels)
                    {
                        image[pixel.Item1 - leftX, pixel.Item2 - topY] = pixel.Item3.ToRgba32();
                    }
                    image.Save(string.Format(filePathTemplate, delta.DateTime));
                    logger.LogInfo($"Saved delta {d.ToString().PadLeft(padLength)}/{deltas.Count}");
                }
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
                        while (!reader.ReachedEnd())
                        {
                            deltas.Add(reader.ReadDelta());
                        }
                    }
                }
                else
                {
                    byte[] buffer = new byte[sizeof(uint)];
                    fileStream.Read(buffer, 0, sizeof(uint));
                    uint mapLength = BitConverter.ToUInt32(buffer, 0);
                    using (DeflateStream decompressingStream = new DeflateStream(fileStream, CompressionMode.Decompress, true))
                    {
                        using (BinaryReader reader = new BinaryReader(decompressingStream, Encoding.Default, true))
                        {
                            reader.ReadMap();
                            while (!reader.ReachedEnd())
                            {
                                reader.ReadByte();
                            }
                        }
                    }
                    fileStream.Seek(sizeof(uint) + mapLength, SeekOrigin.Begin);
                    using (BinaryReader reader = new BinaryReader(fileStream))
                    {
                        while (fileStream.Length - fileStream.Position > 1)
                        {
                            deltas.Add(reader.ReadDelta());
                        }
                    }
                }
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
            initialMapState = BinaryConversion.ConvertToColorRectangle(data, w, h);
        }

        private static Delta ReadDelta(this BinaryReader reader)
        {
            DateTime dateTime = DateTime.FromBinary(reader.ReadInt64());
            uint count = reader.ReadUInt32();
            List<(short, short, PixelColor)> pixels = new List<Pixel>();
            for (int j = 0; j < count; j++)
            {
                short x = reader.ReadInt16();
                short y = reader.ReadInt16();
                PixelColor color = (PixelColor)reader.ReadByte();
                pixels.Add((x, y, color));
            }
            return new Delta
            {
                DateTime = dateTime,
                Pixels = pixels
            };
        }

        private static bool CheckForUpdates()
        {
            using (UpdateChecker checker = new UpdateChecker(logger))
            {
                if (checker.NeedsToCheckUpdates())
                {
                    logger.Log("Checking for updates...", MessageGroup.Update);
                    if (checker.UpdateIsAvailable(out string version, out bool isCompatible))
                    {
                        logger.Log($"Update is available: {version} (current version is {App.Version})", MessageGroup.Update);
                        if (isCompatible)
                        {
                            logger.Log("New version is backwards compatible, it will be relaunched with same arguments", MessageGroup.Update);
                        }
                        else
                        {
                            logger.Log("Argument list was changed, check it and relaunch bot manually after update", MessageGroup.Update);
                        }
                        logger.Log("Press Enter to update, anything else to skip", MessageGroup.Update);
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            logger.Log("Starting update...", MessageGroup.Update);
                            checker.StartUpdate();
                            return true;
                        }
                    }
                    else
                    {
                        if (version == null)
                        {
                            logger.LogError("Cannot check for updates");
                        }
                    }
                }
            }
            return false;
        }

    }
}
