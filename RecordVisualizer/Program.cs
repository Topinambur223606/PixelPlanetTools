using CommandLine;
using PixelPlanetUtils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;

namespace RecordVisualizer
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    class Program
    {
        private static short x1, y1, x2, y2;
        private static int w, h;
        private static DateTime startTime;
        private static PixelColor[,] initialMapState;
        private static List<Delta> deltas;
        private static string fileName;
        private static bool disableUpdates;

        private static void Main(string[] args)
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
                        disableUpdates = o.DisableUpdates;
                        fileName = o.FileName;
                        if (!File.Exists(fileName))
                        {
                            Console.WriteLine("File does not exist");
                            success = false;
                        }
                    });
                if (!success)
                {
                    return;
                }
            }

            if (!disableUpdates)
            {
                if (CheckForUpdates())
                {
                    return;
                }
            }
            
            Console.WriteLine("Loading data from file...");
            try
            {
                LoadFile(fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during loading file: {ex.Message}");
            }
            Console.WriteLine($"File is loaded: {w}x{h}, {deltas.Count + 1} frames");

            DirectoryInfo dir = Directory.CreateDirectory($"seq_{startTime:MM.dd_HH-mm}");
            string pathTemplate = Path.Combine(dir.FullName, "{0:MM.dd_HH-mm}.png");
            try
            {
                SaveLoadedData(pathTemplate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during saving results: {ex.Message}");
            }
        }

        private static void SaveLoadedData(string filePathTemplate)
        {
            int padLength = 1 + (int)Math.Log10(deltas.Count);
            
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
            Console.WriteLine("Initial map state image created");
            int d = 0;
            foreach (Delta delta in deltas)
            {
                d++;
                using (Image<Rgba32> image = new Image<Rgba32>(w, h))
                {
                    foreach ((short, short, PixelColor) pixel in delta.Pixels)
                    {
                        image[pixel.Item1 - x1, pixel.Item2 - y1] = pixel.Item3.ToRgba32();
                    }
                    image.Save(string.Format(filePathTemplate, delta.DateTime));
                    Console.WriteLine($"Saved delta {d.ToString().PadLeft(padLength)}/{deltas.Count}");
                }
            }
        }

        private static void LoadFile(string path)
        {
            using (FileStream f = File.OpenRead(path))
            {
                using (BinaryReader reader = new BinaryReader(f))
                {
                    x1 = reader.ReadInt16();
                    y1 = reader.ReadInt16();
                    x2 = reader.ReadInt16();
                    y2 = reader.ReadInt16();
                    w = x2 - x1 + 1;
                    h = y2 - y1 + 1;
                    startTime = DateTime.FromBinary(reader.ReadInt64());
                    initialMapState = new PixelColor[w, h];
                    for (int dy = 0; dy < h; dy++)
                    {
                        for (int dx = 0; dx < w; dx++)
                        {
                            initialMapState[dx, dy] = (PixelColor)reader.ReadByte();
                        }
                    }
                    deltas = new List<Delta>();
                    while (f.Position < f.Length - 1)
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
                        deltas.Add(new Delta
                        {
                            DateTime = dateTime,
                            Pixels = pixels
                        });
                    }
                }
            }
        }

        private static bool CheckForUpdates()
        {
            using (UpdateChecker checker = new UpdateChecker())
            {
                if (checker.NeedsToCheckUpdates())
                {
                    Console.WriteLine("Checking for updates...");
                    if (checker.UpdateIsAvailable(out string version, out bool isCompatible))
                    {
                        Console.WriteLine($"Update is available: {version} (current version is {App.Version})");
                        if (isCompatible)
                        {
                            Console.WriteLine("New version is backwards compatible, it will be relaunched with same arguments");
                        }
                        else
                        {
                            Console.WriteLine("Argument list or order was changed, app should be relaunched manually after update");
                        }
                        Console.WriteLine("Press Enter to update, anything else to skip");
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            checker.StartUpdate();
                            return true;
                        }
                    }
                    else
                    {
                        if (version == null)
                        {
                            Console.WriteLine("Cannot check for updates");
                        }
                    }
                }
                return false;
            }
        }
    }
}
