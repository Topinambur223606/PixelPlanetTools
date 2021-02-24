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
using Pixel = System.ValueTuple<short, short, byte>;

namespace PixelPlanetBot.Activities
{
    class PixelBuildActivity : BuildActivity
    {
        private List<Pixel> pixelsToBuild;

        private byte[,] imagePixels;
        private ushort[,] brightnessOrderMask;
        private int width, height;

        private ChunkCache2D cache2d;
        private readonly Run2DOptions options;

        private readonly HashSet<Pixel> placed = new HashSet<Pixel>();

        public PixelBuildActivity(Logger logger, Run2DOptions options, ProxySettings proxySettings, CancellationToken finishToken)
            : base(logger, options, proxySettings, finishToken)
        {
            this.options = options;
        }

        private double OutlineCriteria(Pixel p)
        {
            const int radius = 3;
            double score = ThreadSafeRandom.NextDouble() * palette.MaxDistance / 3.5;
            (short x, short y, byte c) = p;
            for (int i = -radius; i <= radius; i++)
            {
                for (int j = -radius; j <= radius; j++)
                {
                    int ox = x + i;
                    int oy = y + j;
                    double dist = Math.Sqrt(i * i + j * j);
                    if (ox >= 0 && oy >= 0 && ox < width && oy < height)
                    {
                        byte c2 = imagePixels[ox, oy];
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
            return score;
        }

        protected override void LogMapChanged(object sender, MapChangedEventArgs e)
        {
            foreach (MapChange c in e.Changes)
            {
                MessageGroup msgGroup;
                PixelMap.OffsetToRelative(c.Offset, out byte rx, out byte ry);
                short x = PixelMap.RelativeToAbsolute(e.Chunk.Item1, rx);
                short y = PixelMap.RelativeToAbsolute(e.Chunk.Item2, ry);

                if (!placed.Remove((x, y, c.Color)))
                {
                    try
                    {
                        var irx = x - options.LeftX;
                        var iry = y - options.TopY;

                        if (irx < 0 || irx >= width || iry < 0 || iry >= height)
                        {
                            //beyond image rectangle
                            msgGroup = MessageGroup.PixelInfo;
                        }
                        else
                        {
                            byte desiredColor = imagePixels[irx, iry];
                            if (palette.IsIgnored(desiredColor))
                            {
                                msgGroup = MessageGroup.PixelInfo;
                            }
                            else if (palette.IsCorrectPixelColor(c.Color, desiredColor))
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
                    logger.LogPixel($"Received pixel update:", e.DateTime, msgGroup, x, y, colorNameResolver.GetName(c.Color));
                }
                else
                {
                    builtInLastMinute++;
                }
            }
        }

        protected override int GetTotalCount() => pixelsToBuild.Count;

        protected override void InitCache()
        {
            cache = cache2d = new ChunkCache2D(pixelsToBuild, logger, options.Canvas);
        }

        protected override async Task LoadImage()
        {
            try
            {
                imagePixels = await ImageConverter.PixelColorsByUri(options.ImagePath, palette, logger);
                width = imagePixels.GetLength(0);
                height = imagePixels.GetLength(1);
                try
                {
                    if (options.LeftX < -(canvas.Size / 2) || options.LeftX + width > canvas.Size / 2)
                    {
                        throw new Exception("X");
                    }

                    if (options.TopY < -(canvas.Size / 2) || options.TopY + height > canvas.Size / 2)
                    {
                        throw new Exception("Y");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Entire image should be inside the map (failed by {ex.Message})");
                }

                if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Mask))
                {
                    brightnessOrderMask = await ImageConverter.GetBrightnessOrderMask(options.BrightnessMaskImagePath, logger, width, height);
                }
            }
            catch (WebException ex)
            {
                throw new Exception("Cannot download image", ex);
            }
            catch (ArgumentException ex)
            {
                throw new Exception("Cannot convert image", ex);
            }
        }

        protected override void ClearPlaced() => placed.Clear();

        protected override void CalculateOrder()
        {
            IEnumerable<Pixel> relativePixelsToBuild;
            IEnumerable<int> allX = Enumerable.Range(0, width);
            IEnumerable<int> allY = Enumerable.Range(0, height);
            IList<Pixel> nonEmptyPixels = allX.
                SelectMany(X => allY.Select(Y =>
                    ((short)X, (short)Y, C: imagePixels[X, Y]))).
                Where(xyc => xyc.C != 0).ToList();

            if (options.PlacingOrderMode == PlacingOrderMode2D.Outline)
            {
                relativePixelsToBuild = nonEmptyPixels.AsParallel().OrderByDescending(OutlineCriteria);
            }
            else if (options.PlacingOrderMode == PlacingOrderMode2D.Random)
            {
                Random rnd = new Random();
                for (int i = 0; i < nonEmptyPixels.Count; i++)
                {
                    int r = rnd.Next(i, nonEmptyPixels.Count);
                    Pixel tmp = nonEmptyPixels[r];
                    nonEmptyPixels[r] = nonEmptyPixels[i];
                    nonEmptyPixels[i] = tmp;
                }
                relativePixelsToBuild = nonEmptyPixels;
            }
            else
            {
                OrderedParallelQuery<Pixel> sortedParallel;
                ParallelQuery<Pixel> nonEmptyParallel = nonEmptyPixels.AsParallel();

                if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Left))
                {
                    sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item1);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Right))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item1);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Top))
                {
                    sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Bottom))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Color))
                {
                    sortedParallel = nonEmptyParallel.OrderBy(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ColorDesc))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ColorRnd))
                {
                    Dictionary<byte, Guid> colorOrder =
                       Enumerable.Range(0, palette.Size)
                                 .ToDictionary(c => (byte)c, c => Guid.NewGuid());

                    sortedParallel = nonEmptyParallel.OrderBy(xy => colorOrder[xy.Item3]);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Mask))
                {
                    sortedParallel = nonEmptyParallel.OrderByDescending(xy => brightnessOrderMask[xy.Item1, xy.Item2]);
                }
                else
                {
                    throw new Exception($"{options.PlacingOrderMode} is not valid placing order mode");
                }

                if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ThenLeft))
                {
                    sortedParallel = sortedParallel.ThenBy(xy => xy.Item1);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ThenRight))
                {
                    sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item1);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ThenTop))
                {
                    sortedParallel = sortedParallel.ThenBy(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ThenBottom))
                {
                    sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item2);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ThenColor))
                {
                    sortedParallel = sortedParallel.ThenBy(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ThenColorDesc))
                {
                    sortedParallel = sortedParallel.ThenByDescending(xy => xy.Item3);
                }
                else if (options.PlacingOrderMode.HasFlag(PlacingOrderMode2D.ThenColorRnd))
                {
                    Dictionary<byte, Guid> colorOrder =
                       Enumerable.Range(0, palette.Size)
                                 .ToDictionary(c => (byte)c, c => Guid.NewGuid());

                    sortedParallel = sortedParallel.ThenBy(xy => colorOrder[xy.Item3]);
                }
                else
                {
                    sortedParallel = sortedParallel.ThenBy(e => Guid.NewGuid());
                }
                relativePixelsToBuild = sortedParallel;
            }
            pixelsToBuild = relativePixelsToBuild
                                .Select(p => ((short)(p.Item1 + options.LeftX), (short)(p.Item2 + options.TopY), p.Item3))
                                .ToList();
        }

        protected override int CountDone()
        {
            return pixelsToBuild.AsParallel()
                    .Count(p => palette.IsCorrectPixelColor(cache2d.GetPixelColor(p.Item1, p.Item2), p.Item3));
        }

        protected override async Task<bool> PerformBuildingCycle(WebsocketWrapper wrapper)
        {
            bool changed = false;
            foreach (Pixel pixel in pixelsToBuild)
            {
                mapUpdatedResetEvent.WaitOne();
                (short x, short y, byte color) = pixel;
                byte actualColor = cache2d.GetPixelColor(x, y);
                if (!palette.IsCorrectPixelColor(actualColor, color))
                {
                    logger.LogDebug($"PerformBuildingCycle: {pixel} - {actualColor}");
                    changed = true;
                    bool success;
                    placed.Add(pixel);
                    do
                    {
                        wrapper.PlacePixel(x, y, color);
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
                            logger.LogPixel($"{(placed ? "P" : "Rep")}laced pixel:", DateTime.Now, MessageGroup.Pixel, x, y, colorNameResolver.GetName(color));
                            if (response.Wait > canvas.TimeBuffer || response.CoolDownSeconds * 1000 > Math.Max(canvas.ReplaceCooldown, 1000))
                            {
                                await Task.Delay(TimeSpan.FromSeconds(response.CoolDownSeconds), finishToken);
                            }
                            else
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(canvas.OptimalCooldown), finishToken);
                            }
                        }
                        else
                        {
                            logger.LogDebug($"PerformBuildingCycle: return code {response.ReturnCode}");
                            if (response.ReturnCode == ReturnCode.Captcha)
                            {
                                if (options.CaptchaTimeout > 0)
                                {
                                    ProcessCaptchaTimeout();
                                }
                                else
                                {
                                    ProcessCaptcha();
                                }
                            }
                            else
                            {
                                logger.LogFail(response.ReturnCode);
                                if (response.ReturnCode != ReturnCode.IpOverused)
                                {
                                    return true;
                                }
                            }
                            await Task.Delay(TimeSpan.FromMilliseconds(response.Wait), finishToken);
                        }
                    } while (!success);
                }
            }
            return changed;
        }

        protected override void ValidateCanvas()
        {
            if (canvas.Is3D)
            {
                throw new Exception("3D canvases are not supported, use \"run3d\" instead of \"run\"");
            }
            PixelMap.MapSize = canvas.Size;
        }
    }
}
