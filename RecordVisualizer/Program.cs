using System;
using System.Collections.Generic;
using System.IO;
using PixelPlanetUtils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RecordVisualizer
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    class Delta
    {
        public Delta(DateTime dateTime)
        {
            DateTime = dateTime;
        }

        public DateTime DateTime { get; }
        public List<Pixel> Pixels { get; } = new List<Pixel>();
    }


    class Program
    {
        static void Main(string[] args)
        {
            short x1, y1, x2, y2;
            int w, h;
            DateTime startTime;
            PixelColor[,] initialMapState;
            List<Delta> deltas = new List<Delta>();
            Console.WriteLine("Loading deltas from file...");
            using (FileStream f = File.OpenRead(args[0]))
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
                    while (f.Position < f.Length - 1)
                    {
                        DateTime dateTime = DateTime.FromBinary(reader.ReadInt64());
                        Delta delta = new Delta(dateTime);
                        uint count = reader.ReadUInt32();
                        for (int j = 0; j < count; j++)
                        {
                            short x = reader.ReadInt16();
                            short y = reader.ReadInt16();
                            PixelColor color = (PixelColor)reader.ReadByte();
                            delta.Pixels.Add((x, y, color));
                        }
                        if (count > 0)
                        {
                            deltas.Add(delta);
                        }
                    }
                }
            }
            Console.WriteLine("Deltas loaded");
            int padLength = 1 + (int)Math.Log10(deltas.Count);
            DirectoryInfo dir = Directory.CreateDirectory($"seq_{startTime:MM.dd_HH-mm}");
            string pathTemplate = Path.Combine(dir.FullName, "{0:MM.dd_HH-mm}.png");
            using (Image<Rgba32> image = new Image<Rgba32>(w, h))
            {
                for (int dy = 0; dy < h; dy++)
                {
                    for (int dx = 0; dx < w; dx++)
                    {
                        image[dx, dy] = initialMapState[dx, dy].ToRgba32();
                    }
                }
                image.Save(string.Format(pathTemplate, startTime));
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
                    image.Save(string.Format(pathTemplate, delta.DateTime));
                    Console.WriteLine($"Saved delta {d.ToString().PadLeft(padLength)}/{deltas.Count}");
                }
            }
        }
    }
}
