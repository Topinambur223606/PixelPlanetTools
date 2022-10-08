using PixelPlanetBot.Captcha;
using PixelPlanetBot.Options;
using PixelPlanetBot.Options.Enums;
using PixelPlanetUtils;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Canvas.Cache;
using PixelPlanetUtils.Canvas.Colors;
using PixelPlanetUtils.Eventing;
using PixelPlanetUtils.Imaging;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Models;
using PixelPlanetUtils.NetworkInteraction.Websocket;
using PixelPlanetUtils.Sessions;
using PixelPlanetUtils.Sessions.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetBot.Activities.Abstract
{
    abstract class BuildActivity : IActivity
    {
        private CancellationTokenSource captchaCts;
        private readonly RunOptions options;
        private Queue<int> doneInPast, builtInPast, griefedInPast;

        protected readonly CancellationToken finishToken;
        protected ManualResetEvent mapUpdatedResetEvent;
        protected AutoResetEvent gotGriefed;
        protected object waitingGriefLockObject;

        protected readonly Logger logger;
        protected readonly ProxySettings proxySettings;
        protected CanvasModel canvas;
        protected ColorNameResolver colorNameResolver;
        protected Palette palette;
        protected Session session;

        protected ChunkCache cache;
        protected bool repeatingFails;

        protected volatile int builtInLastMinute = 0;
        protected volatile int griefedInLastMinute = 0;

        public BuildActivity(Logger logger, RunOptions options, ProxySettings proxySettings, CancellationToken finishToken)
        {
            this.logger = logger;
            this.options = options;
            this.proxySettings = proxySettings;
            this.finishToken = finishToken;
        }

        public async Task Run()
        {
            if (options.Canvas == CanvasType.Moon)
            {
                throw new Exception("Moon canvas is not designed for bots");
            }

            if (options.SessionName != null)
            {
                logger.LogTechState("Loading session...");
                SessionManager sessionManager = new SessionManager(proxySettings);
                session = sessionManager.GetSession(options.SessionName);
                logger.LogTechInfo("Session loaded");
            }

            logger.LogTechState("Connecting to API...");
            PixelPlanetHttpApi api = new PixelPlanetHttpApi
            {
                ProxySettings = proxySettings,
                Session = session
            };
            UserModel user = await api.GetMeAsync(finishToken);
            logger.LogTechInfo("Successfully connected");
            if (options.SessionName != null)
            {
                if (user.Name != null)
                {
                    logger.LogTechInfo($"Session is alive; user \"{user.Name}\"");
                }
                else
                {
                    throw new SessionExpiredException();
                }
            }

            canvas = user.Canvases[options.Canvas];

            LoggerExtensions.MaxCoordXYLength = 1 + (int)Math.Log10(canvas.Size / 2);
            ValidateCanvas();

            palette = new Palette(canvas.Colors, canvas.Is3D);
            colorNameResolver = new ColorNameResolver(options.Canvas);

            await LoadImage();

            logger.LogTechState("Calculating pixel placing order...");
            CalculateOrder();
            logger.LogTechInfo("Pixel placing order is calculated");

            InitCache();

            mapUpdatedResetEvent = new ManualResetEvent(false);
            cache.OnMapUpdated += (o, e) =>
            {
                logger.LogDebug("cache.OnMapUpdated event fired");
                mapUpdatedResetEvent.Set();
            };
            if (options.DefenseMode)
            {
                gotGriefed = new AutoResetEvent(false);
                cache.OnMapUpdated += (o, e) => gotGriefed.Set();
                waitingGriefLockObject = new object();
            }
            Thread statsThread = new Thread(StatsCollectionThreadBody);
            statsThread.Start();
            do
            {
                try
                {
                    using (WebsocketWrapper wrapper = new WebsocketWrapper(logger, false, proxySettings, session, options.Canvas))
                    {
                        wrapper.OnConnectionLost += (o, e) => mapUpdatedResetEvent.Reset();
                        cache.Wrapper = wrapper;
                        cache.DownloadChunks();
                        wrapper.OnMapChanged += LogMapChanged;
                        ClearPlaced();
                        bool wasChanged;
                        do
                        {
                            repeatingFails = false;
                            wasChanged = await PerformBuildingCycle(wrapper);
                            if (!wasChanged && options.DefenseMode)
                            {
                                logger.Log("Image is intact, waiting...", MessageGroup.Info);
                                lock (waitingGriefLockObject)
                                {
                                    logger.LogDebug("Run(): acquiring grief waiting lock");
                                    gotGriefed.Reset();
                                    gotGriefed.WaitOne();
                                }
                                logger.LogDebug("Run(): got griefed");
                                await Task.Delay(ThreadSafeRandom.Next(500, 3000), finishToken);
                            }
                        } while (options.DefenseMode || wasChanged);
                        logger.Log("Building is finished", MessageGroup.Info);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError($"Unhandled exception: {ex.GetBaseException().Message}");
                    logger.LogDebug(ex.ToString());
                    int delay = repeatingFails ? 30 : 10;
                    repeatingFails = true;
                    logger.LogTechState($"Reconnecting in {delay} seconds...");
                    Thread.Sleep(TimeSpan.FromSeconds(delay));
                    continue;
                }
            } while (true);
        }


        private async void StatsCollectionThreadBody()
        {
            void AddToQueue(Queue<int> queue, int value)
            {
                const int maxCount = 5;
                queue.Enqueue(value);
                if (queue.Count > maxCount)
                {
                    queue.Dequeue();
                }
            }

            Task GetDelayTask() => Task.Delay(TimeSpan.FromMinutes(1), finishToken);

            doneInPast = new Queue<int>();
            builtInPast = new Queue<int>();
            griefedInPast = new Queue<int>();

            try
            {
                mapUpdatedResetEvent.WaitOne();
                logger.LogDebug("StatsCollectionThreadBody(): map updated, stats collection started");
                Task taskToWait = GetDelayTask();
                int total = GetTotalCount();
                int done = CountDone();
                doneInPast.Enqueue(done);
                do
                {
                    logger.LogDebug($"StatsCollectionThreadBody(): waiting");
                    await taskToWait;

                    AddToQueue(builtInPast, builtInLastMinute);
                    builtInLastMinute = 0;
                    AddToQueue(griefedInPast, griefedInLastMinute);
                    griefedInLastMinute = 0;

                    done = CountDone();
                    finishToken.ThrowIfCancellationRequested();

                    double buildSpeed = (done - doneInPast.First()) / ((double)doneInPast.Count);
                    AddToQueue(doneInPast, done);

                    double griefedPerMinute = griefedInPast.Average();
                    double builtPerMinute = builtInPast.Average();
                    double percent = Math.Floor(done * 1000D / total) / 10D;
                    DateTime time = DateTime.Now;


                    if (options.DefenseMode)
                    {
                        logger.Log($"Image integrity is {percent:F1}%, {total - done} corrupted pixels", MessageGroup.Info, time);
                        logger.LogDebug($"StatsCollectionThreadBody(): acquiring grief lock");
                        lock (waitingGriefLockObject)
                        { }
                        finishToken.ThrowIfCancellationRequested();
                        logger.LogDebug($"StatsCollectionThreadBody(): grief lock released");
                    }
                    else
                    {
                        string info = $"Image is {percent:F1}% complete, ";
                        if (buildSpeed > 0)
                        {
                            int minsLeft = (int)Math.Ceiling((total - done) / buildSpeed);
                            int hrsLeft = minsLeft / 60;
                            info += $"will be built approximately in {hrsLeft}h {minsLeft % 60}min";
                        }
                        else
                        {
                            info += $"no progress in last 5 minutes";
                        }
                        logger.Log(info, MessageGroup.Info, time);
                    }
                    if (griefedPerMinute > 1)
                    {
                        logger.Log($"Image is under attack at the moment, {done} pixels are good now", MessageGroup.Info, time);
                        logger.Log($"Building {builtPerMinute:F1} px/min, getting griefed {griefedPerMinute:F1} px/min", MessageGroup.Info, time);
                    }
                    taskToWait = GetDelayTask();
                } while (true);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug($"StatsCollectionThreadBody(): cancellation requested, finishing");
            }
            catch (Exception ex)
            {
                logger.LogError($"Stats collection thread: unhandled exception - {ex.Message}");
                logger.LogDebug(ex.ToString());
            }
        }

        private static void Beep()
        {
            for (int j = 0; j < 7; j++)
            {
                Console.Beep(1000, 100);
            }
        }

        private async void BeepThreadBody()
        {
            CancellationToken token = captchaCts.Token;
            while (!token.IsCancellationRequested)
            {
                Beep();
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        protected void ProcessCaptcha(WebsocketWrapper websocket)
        {
            if (options.NotificationMode.HasFlag(CaptchaNotificationMode.Sound))
            {
                captchaCts = new CancellationTokenSource();
                new Thread(BeepThreadBody).Start();
            }

            if (options.NotificationMode.HasFlag(CaptchaNotificationMode.Solver))
            {
                logger.LogAndPause("Please go to captcha window and enter the solution", MessageGroup.Captcha);
                CaptchaForm.EnableVisualStyles();
                using (CaptchaForm form = new CaptchaForm(proxySettings, websocket, logger)
                {
                    ShowInBackground = options.NotificationMode.HasFlag(CaptchaNotificationMode.ShowInBackground)
                })
                {
                    form.ShowDialog();
                }
            }
            else
            { 
                if (options.NotificationMode.HasFlag(CaptchaNotificationMode.Browser))
                {
                    Process.Start(UrlManager.BaseHttpAdress);
                }
                logger.LogAndPause("Please go to browser and place pixel, then return and press any key", MessageGroup.Captcha);
                while (Console.KeyAvailable)
                {
                    Console.ReadKey(true);
                }
                Console.ReadKey(true);
            }
            logger.ResumeLogging();
            captchaCts?.Cancel();
            captchaCts?.Dispose();
            captchaCts = null;
        }

        protected void ProcessCaptchaTimeout()
        {
            logger.Log("Got captcha, waiting...", MessageGroup.Captcha);
            if (options.NotificationMode.HasFlag(CaptchaNotificationMode.Sound))
            {
                Beep();
            }
            if (options.NotificationMode.HasFlag(CaptchaNotificationMode.Browser))
            {
                Process.Start(UrlManager.BaseHttpAdress);
            }
            Thread.Sleep(TimeSpan.FromSeconds(options.CaptchaTimeout));
            logger.LogTechInfo("Captcha timeout");
        }

        protected async Task WaitAfterPlaced(PixelReturnData response, bool placed)
        {
            if (options.ZeroCooldown)
            {
                return;
            }
            int expectedCooldown = placed ? canvas.PlaceCooldown : canvas.ReplaceCooldown;
            bool cdIsTooLong = Math.Max(expectedCooldown, 1000) / Math.Max(response.CoolDownSeconds, 1D) < 1000;
            TimeSpan timeToWait;

            if (cdIsTooLong)
            {
                //accumulate time when CD is long to use it efficiently when CD becames normal
                timeToWait = TimeSpan.FromMilliseconds(Math.Min(response.Wait - 1000, 30000 + 4 * canvas.ReplaceCooldown));
            }
            else if (response.Wait > canvas.TimeBuffer)
            {
                timeToWait = TimeSpan.FromSeconds(response.CoolDownSeconds);
            }
            else
            {
                timeToWait = TimeSpan.FromMilliseconds(canvas.OptimalCooldown);
            }

            await Task.Delay(timeToWait, finishToken);
        }

        protected async Task ProcessPlaceFail<T>(T coords, PixelReturnData response, WebsocketWrapper websocket)
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
                    ProcessCaptcha(websocket);
                }
            }
            else
            {
                logger.LogFail(coords, response.ReturnCode);
                if (response.ReturnCode == ReturnCode.IpOverused)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(response.Wait - 1000, 55000)), finishToken);
                }
                throw new Exception("critical error when trying to place");
            }
        }

        protected abstract void ValidateCanvas();
        protected abstract void LogMapChanged(object sender, MapChangedEventArgs e);
        protected abstract int GetTotalCount();
        protected abstract void InitCache();
        protected abstract Task LoadImage();
        protected abstract void ClearPlaced();
        protected abstract void CalculateOrder();
        protected abstract int CountDone();
        protected abstract Task<bool> PerformBuildingCycle(WebsocketWrapper wrapper);

        public void Dispose()
        {
            captchaCts?.Cancel();
            captchaCts?.Dispose();
            gotGriefed?.Set();
            gotGriefed?.Dispose();
            mapUpdatedResetEvent?.Set();
            mapUpdatedResetEvent?.Dispose();
        }
    }
}
