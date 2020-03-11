using System;

namespace PixelPlanetUtils.Canvas
{
    public static class BinaryConversion
    {
        public static EarthPixelColor[,] ConvertToColorRectangle(byte[] bytes, int height, int width)
        {
            EarthPixelColor[,] chunk = new EarthPixelColor[height, width];
            if (bytes.Length != 0)
            {
                unsafe
                {
                    fixed (byte* byteArr = &bytes[0])
                    fixed (EarthPixelColor* colorArr = &chunk[0, 0])
                    {
                        Buffer.MemoryCopy(byteArr, colorArr, width * height, bytes.Length);
                    }
                }
            }
            return chunk;
        }
    }
}
