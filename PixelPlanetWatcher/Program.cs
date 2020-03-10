using CommandLine;
using PixelPlanetUtils;
using PixelPlanetUtils.CanvasInteraction;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetWatcher
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    class Program
    {
        private static List<Pixel> updates = new List<Pixel>();

        private static Logger logger;
        private static Options options;
        private static ChunkCache cache;

        private static Thread saveThread;
        private static Action stopListening;
        private static Task<FileStream> lockingStreamTask;
        private static readonly object listLockObj = new object();
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();

        private static void Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args))
                {
                    return;
                }

                logger = new Logger(options.LogFilePath, finishCTS.Token)
                {
                    ShowDebugLogs = options.ShowDebugLogs
                };
                logger.LogDebug("Command line: " + Environment.CommandLine);
                HttpWrapper.Logger = logger;

                if (!options.DisableUpdates)
                {
                    if (UpdateChecker.IsStartingUpdate(logger))
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
                        HttpWrapper.ConnectToApi();
                        using (WebsocketWrapper wrapper = new WebsocketWrapper(logger, true))
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
            finally
            {
                logger?.LogInfo("Waiting for saving to finish...");
                finishCTS.Cancel();
                if (logger != null)
                {
                    Thread.Sleep(500);
                }
                finishCTS.Dispose();
                logger?.Dispose();
                if (saveThread != null && !saveThread.Join(TimeSpan.FromMinutes(1)))
                {
                    Console.WriteLine("Save thread can't finish, aborting");
                }
                Console.ForegroundColor = ConsoleColor.White;
                Environment.Exit(0);
            }
        }

        private static bool ParseArguments(string[] args)
        {
            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<Options>(args)
                    .WithNotParsed(e => success = false)
                    .WithParsed(o =>
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
                            if (o.ServerUrl != null)
                            {
                                Console.WriteLine("Invalid args: mirror usage and custom server address are specified");
                                success = false;
                                return;
                            }
                            UrlManager.MirrorMode = true;
                        }
                        if (o.ServerUrl != null)
                        {
                            UrlManager.BaseUrl = o.ServerUrl;
                        }
                    });
                return success;
            }
        }

        private static FileStream SaveInitialMapState()
        {
            DateTime now = DateTime.Now;
            cache.DownloadChunks();
            using (FileStream fileStream = File.Open(options.FileName, FileMode.Create, FileAccess.Write))
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    using (DeflateStream compressionStream = new DeflateStream(memoryStream, CompressionLevel.Fastest, true))
                    {
                        using (BinaryWriter writer = new BinaryWriter(compressionStream))
                        {
                            writer.Write(options.LeftX);
                            writer.Write(options.TopY);
                            writer.Write(options.RightX);
                            writer.Write(options.BottomY);
                            writer.Write(now.ToBinary());
                            for (int y = options.TopY; y <= options.BottomY; y++)
                            {
                                for (int x = options.LeftX; x <= options.RightX; x++)
                                {
                                    writer.Write((byte)cache.GetPixelColor((short)x, (short)y));
                                }
                            }
                        }
                    }
                    fileStream.Write(BitConverter.GetBytes((uint)memoryStream.Length), 0, sizeof(uint));
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    memoryStream.CopyTo(fileStream);
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
                        lockingStreamTask = lockingStreamTask.ContinueWith(t =>
                        {
                            t.Result.Close();
                            WriteChangesToFile(saved);
                            return new FileStream(options.FileName, FileMode.Open, FileAccess.Read, FileShare.None);
                        });
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
                WriteChangesToFile(updates);
            }
            catch (Exception ex)
            {
                logger.LogError($"Unhandled exception during saving: {ex.GetBaseException().Message}");
                stopListening();
            }

            void WriteChangesToFile(List<Pixel> pixels)
            {
                if (pixels.Count > 0)
                {
                    using (FileStream fileStream = File.Open(options.FileName, FileMode.Append, FileAccess.Write))
                    {
                        using (BinaryWriter writer = new BinaryWriter(fileStream))
                        {
                            writer.Write(DateTime.Now.ToBinary());
                            writer.Write((uint)pixels.Count);
                            foreach ((short, short, PixelColor) pixel in pixels)
                            {
                                writer.Write(pixel.Item1);
                                writer.Write(pixel.Item2);
                                writer.Write((byte)pixel.Item3);
                            }
                        }
                    }
                    logger.LogInfo($"{pixels.Count} pixel updates are saved to file");
                }
                else
                {
                    logger.LogInfo($"No pixel updates to save");
                }
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
