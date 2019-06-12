namespace PixelPlanetUtils
{
    public static class PixelMap
    {
        public const int ChunkSize = 256;
        private const int ChunksPerSide = 256;

        public static short ConvertToAbsolute(int chunk, int relative)
        {
            return (short)((chunk - ChunksPerSide / 2) * ChunkSize + relative);
        }

        public static void ConvertToRelative(short absolute, out byte chunk, out byte relative)
        {
            int shifted = absolute + ChunksPerSide * ChunkSize / 2;
            chunk = (byte)(shifted / ChunkSize);
            relative = (byte)(shifted % ChunkSize);
        }
    }
}
