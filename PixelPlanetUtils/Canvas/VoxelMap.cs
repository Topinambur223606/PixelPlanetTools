namespace PixelPlanetUtils.Canvas
{
    public static class VoxelMap
    {
        public const int ChunkSize = 32;
        public const int Height = 128;

        public static int MapSize { get; set; }

        public static void AbsoluteToRelative(short absolute, out byte chunk, out byte relative)
        {
            int shifted = absolute + MapSize / 2;
            chunk = (byte)(shifted / ChunkSize);
            relative = (byte)(shifted % ChunkSize);
        }

        public static int RelativeToOffset(byte relativeX, byte relativeY, byte z)
        {
            return z * ChunkSize * ChunkSize + relativeY * ChunkSize + relativeX;
        }

        public static void OffsetToRelative(uint offset, out byte rx, out byte ry, out byte z)
        {
            rx = (byte)(offset % ChunkSize);
            offset /= ChunkSize;
            ry = (byte)(offset % ChunkSize);
            z = (byte)(offset / ChunkSize);
        }

        public static short RelativeToAbsolute(byte chunk, byte relative)
        {
            return (short)(chunk * ChunkSize + relative - MapSize / 2);
        }
    }
}
