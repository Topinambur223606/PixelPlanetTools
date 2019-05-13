using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
    static class ImageProcessing
    {
        private static readonly Color[] colors = new Color[24]
        {
            Color.White,
            Color.FromArgb(228, 228, 228),
            Color.FromArgb(136,136,136),
            Color.FromArgb(78,78,78),
            Color.Black,
            Color.FromArgb(244,179,174),
            Color.FromArgb(255,167,209),
            Color.FromArgb(255,101,101),
            Color.FromArgb(229,0,0),
            Color.FromArgb(254,164,96),
            Color.FromArgb(229,149,0),
            Color.FromArgb(160,106,66),
            Color.FromArgb(245,223,176),
            Color.FromArgb(229,217,0),
            Color.FromArgb(148,224,68),
            Color.FromArgb(2,190,1),
            Color.FromArgb(0,101,19),
            Color.FromArgb(202,227,255),
            Color.FromArgb(0,211,221),
            Color.FromArgb(0,131,199),
            Color.FromArgb(0,0,234),
            Color.FromArgb(25,25,115),
            Color.FromArgb(207,110,228),
            Color.FromArgb(130,0,128)
        };

        private static PixelColor ClosestAvailable(Color color)
        {
            if (color.A == 0)
            {
                return PixelColor.None;
            }
            int index = 0, d = 260000;
            foreach ((int i, int) diff in colors.Select((c, i) =>
                {
                    int dr = c.R - color.R,
                    dg = c.G - color.G,
                    db = c.B - color.B;
                    return (i, dr * dr + dg * dg + db * db);
                }))
            {
                if (diff.Item2 < d)
                {
                    (index, d) = diff;
                }
            }
            return (PixelColor)(index + 2);
        }

        public static PixelColor[,] ToPixelWorldColors(Bitmap image)
        {
            int w = image.Width;
            int h = image.Height;
            PixelColor[,] res = new PixelColor[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    res[x, y] = ClosestAvailable(image.GetPixel(x, y));
                }
            }
            return res;
        }
    }
}
