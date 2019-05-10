using XY = System.ValueTuple<byte, byte>;

namespace PixelWorldBot
{
    static class PixelMap
    {
        public const int ChunkSize = 256;

        public const int ChunksPerSide = 256;

        public static int ConvertToAbsolute(int chunk, int relative)
        {
            return (chunk - ChunksPerSide / 2) * ChunkSize + relative;
        }

        public static void ConvertToRelative(int absolute, out byte chunk, out byte relative)
        {
            absolute += ChunksPerSide * ChunkSize / 2;
            chunk = (byte)(absolute / ChunkSize);
            relative = (byte)(absolute % ChunkSize);
        }
    }
}
