using CommandLine;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.Options;
using PixelPlanetUtils.Updates;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetWatcher
{
    using Pixel = ValueTuple<short, short, EarthPixelColor>;

    class Program
    {
        private static List<Pixel> updates = new List<Pixel>();

        private static Logger logger;
        private static WatcherOptions options;
        private static ChunkCache cache;

        private static Thread saveThread;
        private static Action stopListening;
        private static Task<FileStream> lockingStreamTask;
        private static bool checkUpdates;
        private static readonly object listLockObj = new object();
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();

        private static void Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args, out bool isVerbError))
                {
                    bool exit = true;
                    if (isVerbError)
                    {
                        Console.WriteLine("No command were found");
                        Console.WriteLine("Check if your scripts are updated with 'run' command before other parameters");
                        Console.WriteLine();
                        Console.WriteLine("If you want to start app with 'run' command added, press Enter");
                        Console.WriteLine("Please note that this option is added for compatibility with older scripts and will be removed soon");
                        Console.WriteLine("Press any other key to exit");
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        if (Console.ReadKey(true).Key == ConsoleKey.Enter)
                        {
                            Console.Clear();
                            if (ParseArguments(args.Prepend("run"), out _))
                            {
                                exit = false;
                            }
                        }
                    }
                    if (exit)
                    {
                        return;
                    }
                }

                logger = new Logger(options?.LogFilePath, finishCTS.Token)
                {
                    ShowDebugLogs = options?.ShowDebugLogs ?? false
                };
                logger.LogDebug("Command line: " + Environment.CommandLine);

                if (checkUpdates || !options.DisableUpdates)
                {
                    if (UpdateChecker.IsStartingUpdate(logger, checkUpdates) || checkUpdates)
                    {
                        return;
                    }
                }

                cache = new ChunkCache(options.LeftX, options.TopY, options.RightX, options.BottomY, logger);
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
                        using (WebsocketWrapper wrapper = new WebsocketWrapper(logger, true, null))
                        {
                            cache.Wrapper = wrapper;
                            if (!initialMapSavingStarted)
                            {
                                logger.LogDebug("Main(): initiating map saving");
                                initialMapSavingStarted = true;
                                lockingStreamTask = Task.Run(SaveInitialMapState);
                            }
                            wrapper.OnPixelChanged += Wrapper_OnPixelChanged;
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

        private static bool ParseArguments(IEnumerable<string> args, out bool isVerbError)
        {
            bool noVerb = false;
            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<WatcherOptions, CheckUpdatesOption>(args)
                    .WithNotParsed(e =>
                    {
                        noVerb = e.Any(err => err.Tag == ErrorType.NoVerbSelectedError || err.Tag == ErrorType.BadVerbSelectedError);
                        success = false;
                    })
                    .WithParsed<CheckUpdatesOption>(o => checkUpdates = true)
                    .WithParsed<WatcherOptions>(o =>
                    {
                        options = o;

                        if (o.LeftX > o.RightX || o.TopY > o.BottomY)
                        {
                            Console.WriteLine("Invalid args: check rectangle borders");
                            success = false;
                            return;
                        }

                        if (o.UseMirror)
                        {
                            UrlManager.MirrorMode = o.UseMirror;
                        }
                        if (o.ServerUrl != null)
                        {
                            UrlManager.BaseUrl = o.ServerUrl;
                        }
                    });
                isVerbError = noVerb;
                return success;
            }
        }

        private static FileStream SaveInitialMapState()
        {
            DateTime now = DateTime.Now;
            cache.DownloadChunks();
            byte[] mapBytes = BinaryConversion.GetRectangle(cache, options.LeftX, options.TopY, options.RightX, options.BottomY);
            using (FileStream fileStream = File.Open(options.FileName, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fileStream))
                {
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

        private static void SaveChangesThreadBody()
        {
            Task GetDelayTask() => Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token);

            Task delayTask = GetDelayTask();
            try
            {
                try
                {
                    do
                    {
                        delayTask.Wait();
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
                {
                    logger.LogDebug("SaveChangesThreadBody(): cancelling (1)");
                }
                catch (TaskCanceledException)
                {
                    logger.LogDebug("SaveChangesThreadBody(): cancelling (2)");
                }
                catch (AggregateException ae) when (ae.GetBaseException() is TaskCanceledException)
                {
                    logger.LogDebug("SaveChangesThreadBody(): cancelling (3)");
                }
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
                        foreach ((short, short, EarthPixelColor) pixel in pixels)
                        {
                            writer.Write(pixel.Item1);
                            writer.Write(pixel.Item2);
                            writer.Write((byte)pixel.Item3);
                        }
                    }
                }
                logger.LogInfo($"{pixels.Count} pixel updates are saved to file");
            }

        }

        private static void Wrapper_OnPixelChanged(object sender, PixelChangedEventArgs e)
        {
            short x = PixelMap.ConvertToAbsolute(e.Chunk.Item1, e.Pixel.Item1);
            short y = PixelMap.ConvertToAbsolute(e.Chunk.Item2, e.Pixel.Item2);
            if (x <= options.RightX && x >= options.LeftX && y <= options.BottomY && y >= options.TopY)
            {
                logger.LogPixel("Received pixel update:", e.DateTime, MessageGroup.PixelInfo, x, y, e.Color);
                lock (listLockObj)
                {
                    updates.Add((x, y, e.Color));
                }
            }
        }
    }
}
