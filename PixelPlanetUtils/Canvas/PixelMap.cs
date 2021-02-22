namespace PixelPlanetUtils.Canvas
{
    public static class PixelMap
    {
        public const int ChunkSize = 256;

        public static int MapSize { get; set; }

        public static short RelativeToAbsolute(int chunk, int relative)
        {
            return (short)(chunk * ChunkSize + relative - MapSize / 2);
        }

        public static void AbsoluteToRelative(short absolute, out byte chunk, out byte relative)
        {
            int shifted = absolute + MapSize / 2;
            chunk = (byte)(shifted / ChunkSize);
            relative = (byte)(shifted % ChunkSize);
        }

        public static int RelativeToOffset(byte relativeX, byte relativeY)
        {
            return relativeY * ChunkSize + relativeX;
        }

        public static void OffsetToRelative(uint offset, out byte rx, out byte ry)
        {
            rx = (byte)(offset % ChunkSize);
            offset /= ChunkSize;
            ry = (byte)(offset % ChunkSize);
        }
    }
}
