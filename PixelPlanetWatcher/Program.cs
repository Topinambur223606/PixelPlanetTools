using CommandLine;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Canvas.Cache;
using PixelPlanetUtils.Canvas.Colors;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Models;
using PixelPlanetUtils.NetworkInteraction.Websocket;
using PixelPlanetUtils.Options;
using PixelPlanetUtils.Updates;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetWatcher
{
    using Pixel = ValueTuple<short, short, byte>;

    class Program
    {
        private static List<Pixel> updates = new List<Pixel>();

        private static Logger logger;
        private static WatcherOptions options;
        private static ChunkCache2D cache;

        private static Thread saveThread;
        private static Action stopListening;
        private static Task<FileStream> lockingStreamTask;
        private static bool checkUpdates;
        private static UserModel user;
        private static ColorNameResolver colorNameResolver;
        private static ProxySettings proxySettings;
        private static readonly object listLockObj = new object();
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();

        private static async Task Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args))
                {
                    return;
                }

                logger = new Logger(options?.LogFilePath, finishCTS.Token)
                {
                    ShowDebugLogs = options?.ShowDebugLogs ?? false
                };
                logger.LogDebug("Command line: " + Environment.CommandLine);

                logger.LogTechState("Connecting to API...");
                PixelPlanetHttpApi api = new PixelPlanetHttpApi
                {
                    ProxySettings = proxySettings
                };

                user = await api.GetMeAsync();
                logger.LogTechInfo("Successfully connected");

                CanvasModel canvas = user.Canvases[options.Canvas];

                if (canvas.Is3D)
                {
                    throw new Exception("3D canvas is not supported");
                }

                LoggerExtensions.MaxCoordXYLength = 1 + (int)Math.Log10(canvas.Size / 2);

                PixelMap.MapSize = canvas.Size;

                try
                {
                    if (options.LeftX < -(canvas.Size / 2) || options.RightX >= canvas.Size / 2)
                    {
                        throw new Exception("X");
                    }

                    if (options.TopY < -(canvas.Size / 2) || options.BottomY >= canvas.Size / 2)
                    {
                        throw new Exception("Y");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Entire rectangle should be inside the map (failed by {ex.Message})");
                }

                colorNameResolver = new ColorNameResolver(options.Canvas);

                if (checkUpdates || !options.DisableUpdates)
                {
                    if (UpdateChecker.IsStartingUpdate(logger, checkUpdates) || checkUpdates)
                    {
                        return;
                    }
                }

                cache = new ChunkCache2D(options.LeftX, options.TopY, options.RightX, options.BottomY, logger, options.Canvas);
                bool initialMapSavingStarted = false;
                saveThread = new Thread(SaveChangesThreadBody);
                saveThread.Start();
                if (string.IsNullOrWhiteSpace(options.FileName))
                {
                    options.FileName = string.Format("pixels_({0};{1})-({2};{3})_{4:yyyy.MM.dd_HH-mm}.bin", options.LeftX, options.TopY, options.RightX, options.BottomY, DateTime.Now);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.FileName)));
                do
                {
                    try
                    {
                        using (WebsocketWrapper wrapper = new WebsocketWrapper(logger, true, null, null, options.Canvas))
                        {
                            cache.Wrapper = wrapper;
                            if (!initialMapSavingStarted)
                            {
                                logger.LogDebug("Main(): initiating map saving");
                                initialMapSavingStarted = true;
                                lockingStreamTask = Task.Run(SaveInitialMapState);
                            }
                            wrapper.OnMapChanged += Wrapper_OnMapChanged;
                            stopListening = wrapper.StopListening;
                            Console.CancelKeyPress += (o, e) =>
                            {
                                logger.LogDebug("Console.CancelKeyPress received");
                                e.Cancel = true;
                                wrapper.StopListening();
                            };
                            logger.LogInfo("Press Ctrl+C to stop");
                            wrapper.StartListening();
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Unhandled exception: {ex.Message}");
                        Thread.Sleep(1000);
                    }
                } while (true);
            }
            catch (Exception ex)
            {
                logger?.LogError($"Unhandled app level exception: {ex.Message}");
                logger?.LogDebug(ex.ToString());
            }
            finally
            {
                if (logger != null)
                {
                    logger.LogInfo("Exiting when everything is saved...");
                    logger.LogInfo($"Logs were saved to {logger.LogFilePath}");
                }
                finishCTS.Cancel();
                if (logger != null)
                {
                    Thread.Sleep(500);
                }
                finishCTS.Dispose();
                logger?.Dispose();
                Console.ForegroundColor = ConsoleColor.White;
                if (saveThread != null && !saveThread.Join(TimeSpan.FromMinutes(1)))
                {
                    Console.WriteLine("Save thread doesn't finish, aborting");
                    Environment.Exit(0);
                }
            }
        }

        private static bool ParseArguments(IEnumerable<string> args)
        {
            bool ProcessAppOptions(AppOptions o)
            {
                if (!string.IsNullOrWhiteSpace(o.ProxyAddress))
                {
                    int protocolLength = o.ProxyAddress.IndexOf("://");
                    if (!o.ProxyAddress.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (protocolLength > -1)
                        {
                            o.ProxyAddress = "http" + o.ProxyAddress.Substring(protocolLength);
                        }
                        else
                        {
                            o.ProxyAddress = "http://" + o.ProxyAddress;
                        }
                    }
                    if (!Uri.IsWellFormedUriString(o.ProxyAddress, UriKind.Absolute))
                    {
                        Console.WriteLine("Invalid proxy address");
                        return false;
                    }

                    proxySettings = new ProxySettings
                    {
                        Address = o.ProxyAddress,
                        Username = o.ProxyUsername,
                        Password = o.ProxyPassword
                    };
                }
                if (o.UseMirror)
                {
                    UrlManager.MirrorMode = o.UseMirror;
                }
                if (o.ServerUrl != null)
                {
                    UrlManager.BaseUrl = o.ServerUrl;
                }
                return true;
            }

            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<WatcherOptions, CheckUpdatesOption>(args)
                    .WithNotParsed(e => success = false)
                    .WithParsed<CheckUpdatesOption>(o => checkUpdates = true)
                    .WithParsed<WatcherOptions>(o =>
                    {
                        if (!ProcessAppOptions(o))
                        {
                            return;
                        }
                        options = o;
                        if (o.LeftX > o.RightX || o.TopY > o.BottomY)
                        {
                            Console.WriteLine("Invalid args: check rectangle borders");
                            success = false;
                            return;
                        }
                    });
                return success;
            }
        }

        private static FileStream SaveInitialMapState()
        {
            const byte version = 1;
            DateTime now = DateTime.Now;
            cache.DownloadChunks();
            byte[] mapBytes = BinaryConversion.GetRectangle(cache, options.LeftX, options.TopY, options.RightX, options.BottomY);
            using (FileStream fileStream = File.Open(options.FileName, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fileStream))
                {
                    writer.Write(version);
                    writer.Write((byte)options.Canvas);
                    writer.Write(options.LeftX);
                    writer.Write(options.TopY);
                    writer.Write(options.RightX);
                    writer.Write(options.BottomY);
                    writer.Write(now.ToBinary());
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        using (DeflateStream compressionStream = new DeflateStream(memoryStream, CompressionLevel.Optimal, true))
                        {
                            compressionStream.Write(mapBytes, 0, mapBytes.Length);
                        }
                        writer.Write(memoryStream.Length);
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        memoryStream.CopyTo(fileStream);
                    }

                }

            }
            logger.Log("Chunk data is saved to file", MessageGroup.TechInfo);
            return new FileStream(options.FileName, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        private static async void SaveChangesThreadBody()
        {
            Task GetDelayTask() => Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token);

            Task delayTask = GetDelayTask();
            try
            {
                try
                {
                    do
                    {
                        await delayTask;
                        delayTask = GetDelayTask();
                        List<Pixel> saved;
                        lock (listLockObj)
                        {
                            saved = updates;
                            updates = new List<Pixel>();
                        }
                        DateTime now = DateTime.Now;
                        if (lockingStreamTask.IsFaulted)
                        {
                            throw lockingStreamTask.Exception.GetBaseException();
                        }
                        if (saved.Count > 0)
                        {
                            lockingStreamTask = lockingStreamTask.ContinueWith(t =>
                            {
                                t.Result.Close();
                                WriteChangesToFile(saved);
                                return new FileStream(options.FileName, FileMode.Open, FileAccess.Read, FileShare.None);
                            });
                        }
                        else
                        {
                            logger.LogInfo($"No pixel updates to save");
                        }
                    } while (true);
                }
                catch (ThreadInterruptedException)
                { }
                catch (OperationCanceledException)
                { }
                lockingStreamTask.Result.Close();

                if (updates.Count > 0)
                {
                    WriteChangesToFile(updates);
                }
                else
                {
                    logger.LogInfo($"No pixel updates to save");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Unhandled exception during saving: {ex.GetBaseException().Message}");
                logger.LogDebug(ex.ToString());
                stopListening();
            }

            void WriteChangesToFile(List<Pixel> pixels)
            {
                using (FileStream fileStream = File.Open(options.FileName, FileMode.Append, FileAccess.Write))
                {
                    using (BinaryWriter writer = new BinaryWriter(fileStream))
                    {
                        writer.Write(DateTime.Now.ToBinary());
                        writer.Write((uint)pixels.Count);
                        foreach (Pixel pixel in pixels)
                        {
                            writer.Write(pixel.Item1);
                            writer.Write(pixel.Item2);
                            writer.Write(pixel.Item3);
                        }
                    }
                }
                logger.LogInfo($"{pixels.Count} pixel updates are saved to file");
            }

        }

        private static void Wrapper_OnMapChanged(object sender, MapChangedEventArgs e)
        {
            foreach (MapChange c in e.Changes)
            {
                PixelMap.OffsetToRelative(c.Offset, out byte rx, out byte ry);
                short x = PixelMap.RelativeToAbsolute(e.Chunk.Item1, rx);
                short y = PixelMap.RelativeToAbsolute(e.Chunk.Item2, ry);
                if (x <= options.RightX && x >= options.LeftX && y <= options.BottomY && y >= options.TopY)
                {
                    logger.LogPixel("Received pixel update:", e.DateTime, MessageGroup.PixelInfo, x, y, colorNameResolver.GetName(c.Color));
                    lock (listLockObj)
                    {
                        updates.Add((x, y, c.Color));
                    }
                }
            }
        }
    }
}
