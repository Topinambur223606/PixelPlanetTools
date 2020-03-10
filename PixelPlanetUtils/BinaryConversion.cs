using PixelPlanetUtils.CanvasInteraction;
using System;

namespace PixelPlanetUtils
{
    public static class BinaryConversion
    {
        public static PixelColor[,] ConvertToColorRectangle(byte[] bytes, int height, int width)
        {
            PixelColor[,] chunk = new PixelColor[height, width];
            if (bytes.Length != 0)
            {
                unsafe
                {
                    fixed (byte* byteArr = &bytes[0])
                    fixed (PixelColor* colorArr = &chunk[0, 0])
                    {
                        Buffer.MemoryCopy(byteArr, colorArr, width * height, bytes.Length);
                    }
                }
            }
            return chunk;
        }
    }
}
