using PixelPlanetUtils;
using PixelPlanetUtils.CanvasInteraction;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetWatcher
{
    using Pixel = ValueTuple<short, short, PixelColor>;

    class Program
    {
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();
        private static ChunkCache cache;
        private static short x1, y1, x2, y2;
        private static string logFilePath;
        private static string filename;
        private static Logger logger;
        private static List<Pixel> updates = new List<Pixel>();
        private static Task<FileStream> lockingStreamTask;
        private static readonly object listLockObj = new object();
        private static readonly Thread saveThread = new Thread(SaveChangesThreadBody);

        static void Main(string[] args)
        {
            try
            {
                if (CheckForUpdates())
                {
                    return;
                }

                try
                {
                    ParseArguments(args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while parsing arguments: {ex.Message}");
                    return;
                }

                logger = new Logger(finishCTS.Token, logFilePath);
                cache = new ChunkCache(x1, y1, x2, y2, logger);
                bool initialMapSavingStarted = false;
                saveThread.Start();
                filename = string.Format("pixels_({0};{1})-({2};{3})_{4:yyyy.MM.dd_HH-mm}.bin", x1, y1, x2, y2, DateTime.Now);
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
                                initialMapSavingStarted = true;
                                lockingStreamTask = Task.Run(SaveInitialMapState);
                            }
                            wrapper.OnPixelChanged += Wrapper_OnPixelChanged;
                            wrapper.StartListening();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Unhandled exception: {ex.Message}");
                    }
                } while (true);
            }
            finally
            {
                finishCTS.Cancel();
                Thread.Sleep(1000);
                finishCTS.Dispose();
                logger?.Dispose();
                if (saveThread.IsAlive)
                {
                    saveThread.Interrupt();
                }
            }
        }

        private static void Wrapper_OnPixelChanged(object sender, PixelChangedEventArgs e)
        {
            short x = PixelMap.ConvertToAbsolute(e.Chunk.Item1, e.Pixel.Item1);
            short y = PixelMap.ConvertToAbsolute(e.Chunk.Item2, e.Pixel.Item2);
            if (x <= x2 && x >= x1 && y <= y2 && y >= y1)
            {
                logger.LogPixel("Received pixel update:", e.DateTime, MessageGroup.PixelInfo, x, y, e.Color);
                lock (listLockObj)
                {
                    updates.Add((x, y, e.Color));
                }
            }
        }

        private static FileStream SaveInitialMapState()
        {
            DateTime now = DateTime.Now;
            cache.DownloadChunks();
            using (FileStream fileStream = File.Open(filename, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fileStream))
                {
                    writer.Write(x1);
                    writer.Write(y1);
                    writer.Write(x2);
                    writer.Write(y2);
                    writer.Write(now.ToBinary());
                    for (int y = y1; y <= y2; y++)
                    {
                        for (int x = x1; x <= x2; x++)
                        {
                            writer.Write((byte)cache.GetPixelColor((short)x, (short)y));
                        }
                    }
                }
            }
            logger.Log("Chunk data is saved to file", MessageGroup.TechInfo);
            return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        private static void ParseArguments(string[] args)
        {
            try
            {
                x1 = short.Parse(args[0]);
                y1 = short.Parse(args[1]);
                x2 = short.Parse(args[2]);
                y2 = short.Parse(args[3]);
                if (x1 > x2 || y1 > y2)
                {
                    throw new Exception();
                }
                try
                {
                    File.Open(args[4], FileMode.Append, FileAccess.Write).Dispose();
                    logFilePath = args[4];
                }
                catch
                { }
            }
            catch (OverflowException)
            {
                throw new Exception("Entire watched zone should be inside the map");
            }
            catch
            {
                throw new Exception("Parameters: <leftX> <topY> <rightX> <bottomY> [logFilePath] ; all in range -32768..32767");
            }
        }

        static bool CheckForUpdates()
        {
            using (UpdateChecker checker = new UpdateChecker())
            {
                if (checker.NeedsToCheckUpdates())
                {
                    Console.WriteLine("Checking for updates...");
                    if (checker.UpdateIsAvailable(out string version, out bool isCompatible))
                    {
                        Console.WriteLine($"Update is available: {version} (current version is {UpdateChecker.CurrentAppVersion})");
                        if (isCompatible)
                        {
                            Console.WriteLine("New version is backwards compatible, it will be relaunched with same arguments");
                        }
                        else
                        {
                            Console.WriteLine("Argument list or order was changed, app should be relaunched manually after update");
                        }
                        Console.WriteLine("Press Enter to update, anything else to skip");
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            checker.StartUpdate();
                            return true;
                        }
                    }
                    else
                    {
                        if (version == null)
                        {
                            Console.WriteLine("Cannot check for updates");
                        }
                    }
                }
            }
            return false;
        }

        static void SaveChangesThreadBody()
        {
            Task GetDelayTask() => Task.Delay(TimeSpan.FromMinutes(1), finishCTS.Token);

            Task delayTask = GetDelayTask();
            try
            {
                do
                {
                    if (finishCTS.IsCancellationRequested)
                    {
                        return;
                    }
                    try
                    {
                        delayTask.Wait();
                        delayTask = GetDelayTask();
                    }
                    catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
                    {
                        return;
                    }
                    List<Pixel> saved;
                    lock (listLockObj)
                    {
                        saved = updates;
                        updates = new List<Pixel>();
                    }
                    DateTime now = DateTime.Now;
                    lockingStreamTask = lockingStreamTask.ContinueWith(t =>
                    {
                        t.Result.Close();
                        using (FileStream fileStream = File.Open(filename, FileMode.Append, FileAccess.Write))
                        {

                            WriteChanges(fileStream, saved);

                        }
                        return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.None);
                    });
                } while (true);
            }
            catch (ThreadInterruptedException)
            {
                using (FileStream fileStream = File.Open(filename, FileMode.Append, FileAccess.Write))
                {
                    lockingStreamTask.Wait();
                    lockingStreamTask.Result.Close();
                    WriteChanges(fileStream, updates);
                }
            }

            void WriteChanges(FileStream fileStream, List<Pixel> pixels)
            {
                if (pixels.Count > 0)
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
                    logger.Log($"{pixels.Count} pixel updates are saved to file", MessageGroup.TechInfo);
                }
                else
                {
                    logger.Log($"No pixel updates to save", MessageGroup.TechInfo);
                }
            }
        }
    }
}
