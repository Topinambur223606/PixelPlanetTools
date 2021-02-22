using PixelPlanetUtils.Canvas.Cache;
using System;
using System.Threading.Tasks;

namespace PixelPlanetUtils.Canvas
{
    public static class BinaryConversion
    {
        public static void DropPixelProtectionInfo(byte[] rawData)
        {
            if (rawData.Length == 0)
            {
                return;
            }

            const byte mask8 = 0x3f; //first 2 bits contain flags
            const ulong mask64 = 0x3f3f3f3f3f3f3f3f;

            int length64 = rawData.Length / sizeof(ulong);

            unsafe
            {
                fixed (byte* arr = &rawData[0])
                {
                    ulong* longArr = (ulong*)arr;
                    for (int j = 0; j < length64; j++)
                    {
                        longArr[j] &= mask64;
                    }
                }
            }

            for (int j = sizeof(ulong) * length64; j < rawData.Length; j++)
            {
                rawData[j] &= mask8;
            }
        }

        public static byte[,] ToColorRectangle(byte[] bytes, int height, int width)
        {
            byte[,] map = new byte[height, width];
            if (bytes.Length != 0)
            {
                Buffer.BlockCopy(bytes, 0, map, 0, bytes.Length);
            }
            return map;
        }

        public static byte[,,] ToColorCuboid(byte[] bytes, int xSize, int ySize, int height)
        {
            byte[,,] map = new byte[height, ySize, xSize];
            if (bytes.Length != 0)
            {
                Buffer.BlockCopy(bytes, 0, map, 0, bytes.Length);
            }
            return map;
        }

        public static byte[] GetRectangle(ChunkCache2D cache, short left, short top, short right, short bottom)
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
                        fixed (byte* chunkPtr = &chunkEntry.Value[0, 0])
                        {
                            (byte chunkX, byte chunkY) = chunkEntry.Key;
                            short xChunkStart = PixelMap.RelativeToAbsolute(chunkX, 0);
                            short yChunkStart = PixelMap.RelativeToAbsolute(chunkY, 0);
                            short xChunkEndExclusive = PixelMap.RelativeToAbsolute(chunkX + 1, 0);
                            short yChunkEndExclusive = PixelMap.RelativeToAbsolute(chunkY + 1, 0);

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
                            byte* inputCurrentPosition = chunkPtr + xBlockStartChunkOffset + PixelMap.ChunkSize * yBlockStartChunkOffset;

                            for (int chunkLine = yBlockStartChunkOffset; chunkLine < yBlockEndExclusiveChunkOffset; chunkLine++)
                            {
                                Buffer.MemoryCopy(inputCurrentPosition, outputCurrentPosition, blockWidth, blockWidth);
                                inputCurrentPosition += PixelMap.ChunkSize;
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
