using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelPlanetUtils.Imaging
{
    public class Palette
    {
        public const double NoneColorDistance = byte.MaxValue * 2D;
        public const byte ColorsSkipped = 2;

        private readonly List<Rgba32> colors;
        private readonly byte[] bgColorReplacement;

        public int Size => colors.Count - ColorsSkipped;

        public double MaxDistance { get; }

        public Rgba32 this[byte color] => colors[color];

        public Palette(IEnumerable<List<byte>> palette, bool transparentEmpty = false)
        {
            colors = palette.Select(c => new Rgba32(c[0], c[1], c[2])).ToList();
            if (transparentEmpty)
            {
                for (int i = 0; i < ColorsSkipped; i++)
                {
                    var c = colors[i];
                    c.A = 0;
                    colors[i] = c;
                }
            }
            else
            {
                bgColorReplacement = Enumerable.Range(0, ColorsSkipped)
                    .Select(i => (byte)colors.FindIndex(ColorsSkipped, c => c == colors[i]))
                    .ToArray();
            }
            MaxDistance = Enumerable.Range(ColorsSkipped, colors.Count - ColorsSkipped - 1)
                .Max(i => Enumerable.Range(i + 1, colors.Count - i - 1).Max(j => RgbCubeDistance((byte)i, (byte)j)));
        }

        public byte ClosestAvailable(Rgba32 color)
        {
            if (color.A == 0)
            {
                return 0;
            }
            byte index = 0;
            int bestD = 260000;
            for (byte i = ColorsSkipped; i < colors.Count; i++)
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
            return index;
        }

        public bool IsCorrectPixelColor(byte actualColor, byte desiredColor)
        {
            return (actualColor == desiredColor) || 
                   (bgColorReplacement != null &&
                   actualColor < ColorsSkipped &&
                   desiredColor == bgColorReplacement[actualColor]);
        }

        public double RgbCubeDistance(byte c1, byte c2)
        {
            if (c1 == c2)
            {
                return 0;
            }
            if (c1 < ColorsSkipped || c2 < ColorsSkipped)
            {
                return NoneColorDistance;
            }
            Rgba32 rgb1 = colors[c1];
            Rgba32 rgb2 = colors[c2];
            int dR = rgb1.R - rgb2.R;
            int dG = rgb1.G - rgb2.G;
            int dB = rgb2.B - rgb2.B;
            return Math.Sqrt(dR * dR + dG * dG + dB * dB);
        }

        public bool IsIgnored(byte desiredColor) => bgColorReplacement != null && desiredColor == 0;
    }
}
