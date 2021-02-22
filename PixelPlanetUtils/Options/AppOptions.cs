using CommandLine;
using PixelPlanetUtils.Canvas;

namespace PixelPlanetUtils.Options
{
    public abstract class AppOptions
    {
        [Option('c', "canvas", Default = CanvasType.Earth, HelpText = "Canvas to operate at")]
        public virtual CanvasType Canvas { get; set; }

        [Option("logFilePath", HelpText = "If specified, is used to write logs; otherwise, logs are written to %appdata%/PixelPlanetTools/logs/<app name>")]
        public string LogFilePath { get; set; }

        [Option("disableUpdates", HelpText = "If specified, updates check is skipped")]
        public bool DisableUpdates { get; set; }

        [Option("showDebug", HelpText = "Toggles debug output to console window")]
        public bool ShowDebugLogs { get; set; }

        [Option("useMirror", SetName = "useMirror", HelpText = "Makes app use mirror (https://fuckyouarkeros.fun/)")]
        public bool UseMirror { get; set; }

        [Option("serverUrl", SetName = "serverUrl", HelpText = "Custom server URL")]
        public string ServerUrl { get; set; }

        [Option("proxyAddress", HelpText = "Proxy address that is used for placing pixels")]
        public string ProxyAddress { get; set; }

        [Option("proxyUsername", HelpText = "Username for connecting to proxy if specified")]
        public string ProxyUsername { get; set; }

        [Option("proxyPassword", HelpText = "Password for connecting to proxy if specified")]
        public string ProxyPassword { get; set; }
    }
}
