using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Voxel = System.ValueTuple<short, short, byte, byte>;
using XY = System.ValueTuple<byte, byte>;

namespace PixelPlanetUtils.Canvas.Cache
{

    public class ChunkCache3D : ChunkCache
    {
        private ConcurrentDictionary<XY, byte[,,]> CachedChunks { get; } = new ConcurrentDictionary<XY, byte[,,]>();

        public ChunkCache3D(IEnumerable<Voxel> voxels, Logger logger, CanvasType canvas)
        {
            interactiveMode = true;
            this.canvas = canvas;
            this.logger = logger;
            logger.LogTechState("Calculating list of chunks...");
            chunks = voxels.AsParallel().Select(p =>
            {
                VoxelMap.AbsoluteToRelative(p.Item1, out byte chunkX, out _);
                VoxelMap.AbsoluteToRelative(p.Item2, out byte chunkY, out _);
                return (chunkX, chunkY);
            }).Distinct().ToList();
            logger.LogTechInfo("Chunk list is calculated");
        }

        public byte GetVoxelColor(short x, short y, byte z)
        {
            VoxelMap.AbsoluteToRelative(x, out byte chunkX, out byte relativeX);
            VoxelMap.AbsoluteToRelative(y, out byte chunkY, out byte relativeY);
            try
            {
                byte[,,] chunkMap = CachedChunks[(chunkX, chunkY)];
                return chunkMap[z, relativeY, relativeX];
            }
            catch
            {
                logger.LogDebug($"GetPixelColor(): exception while retrieving ({x};{y};{z}) = ({chunkX};{chunkY}):({relativeX};{relativeY};{z})");
                throw;
            }
        }

        protected override void Wrapper_OnMapChanged(object sender, MapChangedEventArgs e)
        {
            if (CachedChunks.TryGetValue(e.Chunk, out byte[,,] chunkMap))
            {
                foreach (MapChange c in e.Changes)
                {
                    VoxelMap.OffsetToRelative(c.Offset, out byte rX, out byte rY, out byte z);
                    chunkMap[z, rY, rX] = c.Color;
                }
            }
            else
            {
                logger.LogDebug("Wrapper_OnVoxelChanged(): voxel is not in loaded area");
            }
        }

        protected override void SaveChunk(XY chunkXY, byte[] data)
        {
            CachedChunks[chunkXY] = BinaryConversion.ToColorCuboid(data, VoxelMap.ChunkSize, VoxelMap.ChunkSize, VoxelMap.Height);
        }
    }
}
