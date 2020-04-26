using CommandLine;

namespace PixelPlanetUtils.Options
{
    public abstract class AppOptions
    {
        [Option("logFilePath", Default = null, HelpText = "If specified, is used to write logs; otherwise, logs are written to %appdata%/PixelPlanetTools/logs/<app name>")]
        public string LogFilePath { get; set; }

        [Option("disableUpdates", Default = false, HelpText = "If specified, updates check is skipped")]
        public bool DisableUpdates { get; set; }

        [Option("showDebug", Default = false, HelpText = "Toggles debug output to console window")]
        public bool ShowDebugLogs { get; set; }

        //[Option("canvas", Default = CanvasType.Earth, HelpText = "CanvasType that is used (earth, moon or voxel)")]
        //public CanvasType CanvasType { get; set; }
    }
}
