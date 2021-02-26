using CommandLine;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Imaging;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Models;
using PixelPlanetUtils.Options;
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
    using Pixel = ValueTuple<short, short, byte>;

    static class Program
    {
        private static int w, h;
        private static DateTime startTime;
        private static byte[,] initialMapState;
        private static short leftX, topY, rightX, bottomY;
        private static readonly List<Delta> deltas = new List<Delta>();

        private static CanvasType canvasType = CanvasType.Earth;
        private static Logger logger;
        private static VisualizerOptions options;
        private static bool checkUpdates;
        private static Palette palette;
        private static ProxySettings proxySettings;
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();

        private async static Task Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args))
                {
                    return;
                }

                logger = new Logger(options?.LogFilePath, finishCTS.Token)
                {
                    ShowDebugLogs = options?.ShowDebugLogs ?? false
                };

                if (checkUpdates || !options.DisableUpdates)
                {
                    if (UpdateChecker.IsStartingUpdate(logger, checkUpdates) || checkUpdates)
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

                logger.LogTechState("Downloading palette...");
                PixelPlanetHttpApi api = new PixelPlanetHttpApi
                {
                    ProxySettings = proxySettings
                };
                UserModel user = await api.GetMeAsync();
                logger.LogTechInfo("Palette retrieved");
                CanvasModel canvas = user.Canvases[canvasType];
                palette = new Palette(canvas.Colors);

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
                logger?.LogDebug(ex.ToString());
            }
            finally
            {
                if (logger != null)
                {
                    logger.LogInfo("Exiting...");
                    logger.LogInfo($"Logs were saved to {logger.LogFilePath}");
                }
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

        private static bool ParseArguments(IEnumerable<string> args)
        {
            bool ProcessAppOptions(AppOptions o)
            {
                if (!string.IsNullOrWhiteSpace(o.ProxyAddress))
                {
                    int protocolLength = o.ProxyAddress.IndexOf("://");
                    if (!o.ProxyAddress.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (protocolLength > -1)
                        {
                            o.ProxyAddress = "http" + o.ProxyAddress.Substring(protocolLength);
                        }
                        else
                        {
                            o.ProxyAddress = "http://" + o.ProxyAddress;
                        }
                    }
                    if (!Uri.IsWellFormedUriString(o.ProxyAddress, UriKind.Absolute))
                    {
                        Console.WriteLine("Invalid proxy address");
                        return false;
                    }

                    proxySettings = new ProxySettings
                    {
                        Address = o.ProxyAddress,
                        Username = o.ProxyUsername,
                        Password = o.ProxyPassword
                    };
                }
                if (o.UseMirror)
                {
                    UrlManager.MirrorMode = o.UseMirror;
                }
                if (o.ServerUrl != null)
                {
                    UrlManager.BaseUrl = o.ServerUrl;
                }
                return true;
            }


            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<VisualizerOptions, CheckUpdatesOption>(args)
                    .WithNotParsed(e => success = false)
                    .WithParsed<CheckUpdatesOption>(o => checkUpdates = true)
                    .WithParsed<VisualizerOptions>(o =>
                    {
                        if (!ProcessAppOptions(o))
                        {
                            success = false;
                            return;
                        }

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
                byte version = 0;

                using (BinaryReader reader = new BinaryReader(fileStream, Encoding.Default, true))
                {
                    if (!options.OldRecordFile)
                    {
                        version = reader.ReadByte();
                    }

                    if (version == 1)
                    {
                        canvasType = (CanvasType)reader.ReadByte();
                    }

                    leftX = reader.ReadInt16();
                    topY = reader.ReadInt16();
                    rightX = reader.ReadInt16();
                    bottomY = reader.ReadInt16();
                    w = rightX - leftX + 1;
                    h = bottomY - topY + 1;
                    startTime = DateTime.FromBinary(reader.ReadInt64());
                }


                using (BinaryReader reader = new BinaryReader(fileStream))
                {
                    long mapLength = reader.ReadInt64();

                    using (DeflateStream decompressingStream = new DeflateStream(fileStream, CompressionMode.Decompress, true))
                    {
                        using (BinaryReader decompressingReader = new BinaryReader(decompressingStream))
                        {
                            initialMapState = decompressingReader.ReadMap();
                        }
                    }

                    int metadataLength = sizeof(short) * 4 + sizeof(long) + sizeof(long); //rect boundaries, start time and map length
                    if (version == 1)
                    {
                        metadataLength += sizeof(byte) * 2; //version and canvas
                    }
                    fileStream.Seek(metadataLength + mapLength, SeekOrigin.Begin);

                    while (fileStream.Length - fileStream.Position > 1)
                    {
                        deltas.Add(reader.ReadDelta());
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
                        image[pixel.Item1 - leftX, pixel.Item2 - topY] = palette[pixel.Item3];
                    }
                    string fileName = string.Format(filePathTemplate, delta.DateTime);
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
                        image[x, y] = palette[initialMapState[y, x]];
                    }
                }
                image.Save(filename);
                logger.LogInfo("Initial map state image created");
            }
        }

        private static byte[,] ReadMap(this BinaryReader reader)
        {
            byte[] data = reader.ReadBytes(w * h);
            return BinaryConversion.ToColorRectangle(data, h, w);
        }

        private static Delta ReadDelta(this BinaryReader reader)
        {
            DateTime dateTime = DateTime.FromBinary(reader.ReadInt64());
            uint count = reader.ReadUInt32();
            List<Pixel> pixels = new List<Pixel>();
            for (int j = 0; j < count; j++)
            {
                short x = reader.ReadInt16();
                short y = reader.ReadInt16();
                byte color = reader.ReadByte();
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
