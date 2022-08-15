using PixelPlanetBot.Activities.Abstract;
using PixelPlanetBot.Options;
using PixelPlanetBot.Options.Enums;
using PixelPlanetUtils;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Canvas.Cache;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Imaging;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Websocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Voxel = System.ValueTuple<short, short, byte, byte>;

namespace PixelPlanetBot.Activities
{
    class VoxelBuildActivity : BuildActivity
    {
        private List<Voxel> voxelsToBuild;

        private byte[,,] imageVoxels;
        private int sizeX, sizeY, height;

        private ChunkCache3D cache3d;
        private readonly Run3DOptions options;

        private readonly HashSet<Voxel> placed = new HashSet<Voxel>();

        public VoxelBuildActivity(Logger logger, Run3DOptions options, ProxySettings proxySettings, CancellationToken finishToken)
            : base(logger, options, proxySettings, finishToken)
        {
            this.options = options;
        }

        private double OutlineCriteria(Voxel p)
        {
            const int radius = 1;
            double score = ThreadSafeRandom.NextDouble() * palette.MaxDistance / 3.5;
            (short x, short y, byte z, byte c) = p;
            for (int i = -radius; i <= radius; i++)
            {
                for (int j = -radius; j <= radius; j++)
                {
                    for (int k = -radius; k <= radius; k++)
                    {
                        int ox = x + i;
                        int oy = y + j;
                        int oz = z + k;
                        double dist = Math.Sqrt(i * i + j * j);
                        if (ox >= 0 && oy >= 0 && oz >= 0 && ox < sizeX && oy < sizeY && oz < height)
                        {
                            byte c2 = imageVoxels[ox, oy, oz];
                            if (c != c2)
                            {
                                score += palette.RgbCubeDistance(c, c2) / dist;
                            }
                        }
                        else
                        {
                            score += Palette.NoneColorDistance / dist;
                        }
                    }
                }
            }
            return score;
        }

        protected override void LogMapChanged(object sender, MapChangedEventArgs e)
        {
            foreach (MapChange c in e.Changes)
            {
                MessageGroup msgGroup;
                VoxelMap.OffsetToRelative(c.Offset, out byte rx, out byte ry, out byte z);
                short x = VoxelMap.RelativeToAbsolute(e.Chunk.Item1, rx);
                short y = VoxelMap.RelativeToAbsolute(e.Chunk.Item2, ry);

                if (!placed.Remove((x, y, z, c.Color)))
                {
                    try
                    {
                        int irx = x - options.MinX;
                        int iry = y - options.MinY;
                        int irz = z - options.BottomZ;

                        if (irx < 0 || irx >= sizeX || iry < 0 || iry >= sizeY || irz < 0 || irz > height)
                        {
                            //beyond image
                            msgGroup = MessageGroup.PixelInfo;
                        }
                        else
                        {
                            byte desiredColor = imageVoxels[x - options.MinX, y - options.MinY, z - options.BottomZ];
                            if (desiredColor == c.Color)
                            {
                                msgGroup = MessageGroup.Assist;
                                builtInLastMinute++;
                            }
                            else
                            {
                                msgGroup = MessageGroup.Attack;
                                griefedInLastMinute++;
                                gotGriefed?.Set();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug($"LogMapChanged: unhandled exception - {ex}");
                        msgGroup = MessageGroup.PixelInfo;
                    }
                    logger.LogVoxel($"Received voxel update:", e.DateTime, msgGroup, x, y, z, colorNameResolver.GetName(c.Color));
                }
                else
                {
                    builtInLastMinute++;
                }
            }
        }

        protected override int GetTotalCount() => voxelsToBuild.Count;

        protected override void InitCache()
        {
            cache = cache3d = new ChunkCache3D(voxelsToBuild, logger, options.Canvas);
        }

        protected override async Task LoadImage()
        {
            try
            {
                if (options.ImagePath != null)
                {
                    imageVoxels = await ImageConverter.VoxelColorsByPngUri(options.ImagePath, palette, logger);
                }
                else
                {
                    imageVoxels = await ImageConverter.VoxelColorsByCsvUri(options.DocumentPath, palette, logger);
                }
                sizeX = imageVoxels.GetLength(0);
                sizeY = imageVoxels.GetLength(1);
                height = imageVoxels.GetLength(2);
                try
                {
                    if (options.MinX < -(canvas.Size / 2) || options.MinX + sizeX > canvas.Size / 2)
                    {
                        throw new Exception("X");
                    }

                    if (options.MinY < -(canvas.Size / 2) || options.MinY + sizeY > canvas.Size / 2)
                    {
                        throw new Exception("Y");
                    }

                    if (options.BottomZ + height > VoxelMap.Height)
                    {
                        throw new Exception("Z");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Entire image should fit in map (failed by {ex.Message})");
                }
            }
            catch (WebException ex)
            {
                throw new Exception("Cannot download image", ex);
            }
            catch (SystemException ex)
            {
                throw new Exception("Cannot convert image", ex);
            }
        }

        protected override void ClearPlaced() => placed.Clear();

        protected override void CalculateOrder()
        {
            IEnumerable<Voxel> relativeVoxelsToBuild;
            IEnumerable<int> allX = Enumerable.Range(0, sizeX);
            IEnumerable<int> allY = Enumerable.Range(0, sizeY);
            IEnumerable<int> allZ = Enumerable.Range(0, height);
            List<Voxel> voxels =
                allX.SelectMany(X =>
                allY.SelectMany(Y =>
                allZ.Select(Z => ((short)X, (short)Y, (byte)Z, C: imageVoxels[X, Y, Z]))))
                .ToList();

            if (options.PlacingOrderMode == PlacingOrderMode3D.Outline)
            {
                relativeVoxelsToBuild = voxels.AsParallel().OrderByDescending(OutlineCriteria);
            }
            else if (options.PlacingOrderMode == PlacingOrderMode3D.Random)
            {
                Random rnd = new Random();
                for (int i = 0; i < voxels.Count; i++)
                {
                    int r = rnd.Next(i, voxels.Count);
                    Voxel tmp = voxels[r];
                    voxels[r] = voxels[i];
                    voxels[i] = tmp;
                }
                relativeVoxelsToBuild = voxels;
            }
            else
            {
                OrderedParallelQuery<Voxel> sortedParallel;
                ParallelQuery<Voxel> nonEmptyParallel = voxels.AsParallel();

                if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.AscX))
                {
                    sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item1);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.DescX))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item1);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.AscY))
                {
                    sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.DescY))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.Top))
                {
                    sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.Bottom))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.Color))
                {
                    sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item4);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ColorDesc))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item4);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ColorRnd))
                {
                    Dictionary<byte, Guid> colorOrder =
                       Enumerable.Range(0, palette.Size)
                                 .ToDictionary(c => (byte)c, c => Guid.NewGuid());

                    sortedParallel = nonEmptyParallel.OrderBy(xy => colorOrder[xy.Item4]);
                }
                else
                {
                    throw new Exception($"{options.PlacingOrderMode} is not valid placing order mode");
                }

                if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenAscX))
                {
                    sortedParallel = sortedParallel.ThenBy(xy => xy.Item1);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenDescX))
                {
                    sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item1);
                }
                if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenAscY))
                {
                    sortedParallel = sortedParallel.ThenBy(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenDescY))
                {
                    sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenTop))
                {
                    sortedParallel = sortedParallel.ThenBy(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenBottom))
                {
                    sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenColor))
                {
                    sortedParallel = sortedParallel.ThenBy(xy => xy.Item4);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenColorDesc))
                {
                    sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item4);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode3D.ThenColorRnd))
                {
                    Dictionary<byte, Guid> colorOrder =
                       Enumerable.Range(0, palette.Size)
                                 .ToDictionary(c => (byte)c, c => Guid.NewGuid());

                    sortedParallel = sortedParallel.ThenBy(xy => colorOrder[xy.Item4]);
                }
                else
                {
                    sortedParallel = sortedParallel.ThenBy(e => Guid.NewGuid());
                }
                relativeVoxelsToBuild = sortedParallel;
            }
            voxelsToBuild = relativeVoxelsToBuild
                                .Select(p => ((short)(p.Item1 + options.MinX),
                                              (short)(p.Item2 + options.MinY),
                                              (byte)(p.Item3 + options.BottomZ),
                                              p.Item4))
                                .ToList();
        }

        protected override int CountDone()
        {
            return voxelsToBuild.AsParallel()
                    .Count(p => palette.IsCorrectPixelColor(cache3d.GetVoxelColor(p.Item1, p.Item2, p.Item3), p.Item4));
        }

        protected override async Task<bool> PerformBuildingCycle(WebsocketWrapper wrapper)
        {
            bool changed = false;
            foreach (Voxel voxel in voxelsToBuild)
            {
                mapUpdatedResetEvent.WaitOne();
                (short x, short y, byte z, byte color) = voxel;
                byte actualColor = cache3d.GetVoxelColor(x, y, z);
                if (!palette.IsCorrectPixelColor(actualColor, color))
                {
                    logger.LogDebug($"PerformBuildingCycle: {voxel} - {actualColor}");
                    changed = true;
                    bool success;
                    placed.Add(voxel);
                    do
                    {
                        wrapper.PlaceVoxel(x, y, z, color);
                        PixelReturnData response = wrapper.GetPlaceResponse();

                        if (response == null)
                        {
                            success = false;
                            continue;
                        }

                        success = response.ReturnCode == ReturnCode.Success;
                        if (success)
                        {
                            bool placed = actualColor < Palette.ColorsSkipped;
                            logger.LogVoxel($"{(placed ? "P" : "Rep")}laced voxel:", DateTime.Now, MessageGroup.Pixel, x, y, z, colorNameResolver.GetName(color));
                            await WaitAfterPlaced(response, placed);
                        }
                        else
                        {
                            await ProcessPlaceFail((x, y, z), response);
                        }
                    } while (!success);
                }
            }
            return changed;
        }

        protected override void ValidateCanvas()
        {
            if (!canvas.Is3D)
            {
                throw new Exception("2D canvases are not supported, use \"run\" instead of \"run3d\"");
            }
            VoxelMap.MapSize = canvas.Size;
        }
    }
}
