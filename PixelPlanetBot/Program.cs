using CommandLine;
using PixelPlanetBot.Activities;
using PixelPlanetBot.Activities.Abstract;
using PixelPlanetBot.Options;
using PixelPlanetBot.Options.Enums;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.Options;
using PixelPlanetUtils.Updates;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetBot
{
    static partial class Program
    {
        private static readonly CancellationTokenSource finishCTS = new CancellationTokenSource();
        private static readonly CancellationTokenSource loggingCTS = new CancellationTokenSource();

        private static Logger logger;
        private static AppOptions appOptions;
        private static Run2DOptions run2dOptions;
        private static Run3DOptions run3dOptions;
        private static SessionsOptions sessionsOptions;

        private static bool checkUpdates;
        private static ProxySettings proxySettings;

        private static async Task Main(string[] args)
        {
            try
            {
                if (!ParseArguments(args))
                {
                    return;
                }

                logger = new Logger(appOptions?.LogFilePath, loggingCTS.Token)
                {
                    ShowDebugLogs = appOptions?.ShowDebugLogs ?? false
                };
                logger.LogDebug("Command line: " + Environment.CommandLine);

                if (checkUpdates || !appOptions.DisableUpdates)
                {
                    if (UpdateChecker.IsStartingUpdate(logger, checkUpdates) || checkUpdates)
                    {
                        return;
                    }
                }

                Console.CancelKeyPress += (o, e) =>
                {
                    e.Cancel = true;
                    finishCTS.Cancel();
                };

                IActivity activity = null;

                if (run2dOptions != null)
                {
                    activity = new PixelBuildActivity(logger, run2dOptions, proxySettings, finishCTS.Token);
                }
                else if (run3dOptions != null)
                {
                    activity = new VoxelBuildActivity(logger, run3dOptions, proxySettings, finishCTS.Token);
                }
                else if (sessionsOptions != null)
                {
                    activity = new SessionActivity(logger, sessionsOptions, proxySettings);
                }

                try
                {
                    await activity?.Run();
                }
                finally
                {
                    activity.Dispose();
                }
            }
            catch (OperationCanceledException)
            { }
            catch (Exception ex)
            {
                string msg = $"Unhandled app level exception: {ex.Message}";
                if (logger != null)
                {
                    logger.LogError(msg);
                    logger.LogDebug(ex.ToString());
                }
                else
                {
                    Console.WriteLine(msg);
                }
            }
            finally
            {
                if (logger != null)
                {
                    logger.LogInfo("Exiting...");
                    logger.LogInfo($"Logs were saved to {logger.LogFilePath}");
                    Thread.Sleep(100);
                    loggingCTS.Cancel();
                }
                logger?.Dispose();
                finishCTS.Dispose();
                loggingCTS.Dispose();
                Console.ForegroundColor = ConsoleColor.White;
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
                if (o.ServerHostname != null)
                {
                    UrlManager.Hostname = o.ServerHostname;
                }
                UrlManager.NoSsl = o.NoSsl;
                return true;
            }

            void CheckWarnProxy(RunOptions o)
            {
                if (proxySettings != null && o.NotificationMode.HasFlag(CaptchaNotificationMode.Browser))
                {
                    Console.WriteLine($"Warning: proxy usage in browser notification mode is detected{Environment.NewLine}" +
                        "Ensure that same proxy settings are set in your default browser");
                }
            }

            using (Parser parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Out;
            }))
            {
                bool success = true;
                parser.ParseArguments<Run2DOptions, Run3DOptions, CheckUpdatesOption, SessionsOptions>(args)
                    .WithNotParsed(e => success = false)
                    .WithParsed<CheckUpdatesOption>(o => checkUpdates = true)
                    .WithParsed<Run2DOptions>(o =>
                    {
                        if (!ProcessAppOptions(o))
                        {
                            success = false;
                            return;
                        }
                        appOptions = run2dOptions = o;
                        if (o.PlacingOrderMode.HasFlag(PlacingOrderMode2D.Mask) && o.BrightnessMaskImagePath == null)
                        {
                            Console.WriteLine("Mask path not specified");
                            success = false;
                            return;
                        }
                        CheckWarnProxy(o);

                    })
                    .WithParsed<Run3DOptions>(o =>
                    {
                        if (!ProcessAppOptions(o))
                        {
                            success = false;
                            return;
                        }
                        if (o.DocumentPath != null && o.ImagePath != null)
                        {
                            Console.WriteLine("Both CSV and PNG are passed, aborting");
                            success = false;
                            return;
                        };
                        appOptions = run3dOptions = o;
                        CheckWarnProxy(o);

                    })
                    .WithParsed<SessionsOptions>(o =>
                    {
                        if (!ProcessAppOptions(o))
                        {
                            success = false;
                            return;
                        }
                        appOptions = sessionsOptions = o;
                        if (o.Add && o.Remove)
                        {
                            success = false;
                            Console.WriteLine("You can't add and remove session at the same time");
                            return;
                        }
                        if (!(o.Add || o.Remove || o.PrintSessionList))
                        {
                            o.PrintSessionList = true;
                            return;
                        }
                        if (o.Add && (o.UserName == null || o.Password == null))
                        {
                            success = false;
                            Console.WriteLine("Both username and password should be specified to log in");
                            return;
                        }
                        if (o.Remove && o.SessionName == null)
                        {
                            success = false;
                            Console.WriteLine("Session name to remove should be specified");
                            return;
                        }
                    });
                return success;
            }
        }
    }
}
