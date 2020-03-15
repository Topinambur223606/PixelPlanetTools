using System;
using System.Threading.Tasks;

namespace PixelPlanetUtils.Canvas
{
    public static class BinaryConversion
    {
        public static void DropPixelProtectionInfo(byte[] rawData)
        {
            for (int j = 0; j < rawData.Length; j++)
            {
                rawData[j] &= 0x3F; //remove first 2 bits that indicate if pixel is protected
            }
        }

        public static EarthPixelColor[,] ToColorRectangle(byte[] bytes, int height, int width)
        {
            EarthPixelColor[,] map = new EarthPixelColor[height, width];
            if (bytes.Length != 0)
            {
                unsafe
                {
                    fixed (byte* byteArr = &bytes[0])
                    fixed (EarthPixelColor* colorArr = &map[0, 0])
                    {
                        Buffer.MemoryCopy(byteArr, colorArr, width * height, bytes.Length);
                    }
                }
            }
            return map;
        }

        public static byte[] GetRectangle(ChunkCache cache, short left, short top, short right, short bottom)
        {
            int rightExclusive = right + 1;
            int bottomExclusive = bottom + 1;
            int width = rightExclusive - left;
            int height = bottomExclusive - top;

            byte[] bytes = new byte[height * width];

            unsafe
            {
                fixed (byte* byteArrPtr = &bytes[0])
                {
                    byte* byteArrStartPosition = byteArrPtr;
                    Parallel.ForEach(cache.CachedChunks, chunkEntry =>
                    {
                        fixed (EarthPixelColor* chunkPtr = &chunkEntry.Value[0, 0])
                        {
                            (byte chunkX, byte chunkY) = chunkEntry.Key;
                            short xChunkStart = PixelMap.ConvertToAbsolute(chunkX, 0);
                            short yChunkStart = PixelMap.ConvertToAbsolute(chunkY, 0);
                            short xChunkEndExclusive = PixelMap.ConvertToAbsolute(chunkX + 1, 0);
                            short yChunkEndExclusive = PixelMap.ConvertToAbsolute(chunkY + 1, 0);

                            short xBlockStart = Math.Max(xChunkStart, left);
                            short yBlockStart = Math.Max(yChunkStart, top);
                            int xBlockEndExclusive = Math.Min(xChunkEndExclusive, rightExclusive);
                            int yBlockEndExclusive = Math.Min(yChunkEndExclusive, bottomExclusive);

                            if (xBlockStart >= xBlockEndExclusive || yBlockStart >= yBlockEndExclusive)
                            {
                                return;
                            }

                            int xBlockStartChunkOffset = xBlockStart - xChunkStart;
                            int yBlockStartChunkOffset = yBlockStart - yChunkStart;
                            int yBlockEndExclusiveChunkOffset = yBlockEndExclusive - yChunkStart;
                            int blockWidth = xBlockEndExclusive - xBlockStart;

                            byte* outputCurrentPosition = byteArrStartPosition + xBlockStart - left + (yBlockStart - top) * width;
                            EarthPixelColor* inputCurrentPosition = chunkPtr + xBlockStartChunkOffset + PixelMap.ChunkSideSize * yBlockStartChunkOffset;

                            for (int chunkLine = yBlockStartChunkOffset; chunkLine < yBlockEndExclusiveChunkOffset; chunkLine++)
                            {
                                Buffer.MemoryCopy(inputCurrentPosition, outputCurrentPosition, blockWidth, blockWidth);
                                inputCurrentPosition += PixelMap.ChunkSideSize;
                                outputCurrentPosition += width;
                            }
                        }
                    });
                }
            }
            return bytes;
        }
    }
}
