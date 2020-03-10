using CommandLine;

namespace PixelPlanetWatcher
{
    class Options
    {
        [Option('l', "leftX", Required = true, HelpText = "Left boundary of recorded rectangle")]
        public short LeftX { get; set; }

        [Option('r', "rightX", Required = true, HelpText = "Right boundary of recorded rectangle")]
        public short RightX { get; set; }

        [Option('t', "topY", Required = true, HelpText = "Upper boundary of recorded rectangle")]
        public short TopY { get; set; }

        [Option('b', "bottomY", Required = true, HelpText = "Lower boundary of recorded rectangle")]
        public short BottomY { get; set; }

        [Option('f', "fileName", HelpText = "Custom path to output file or its name")]
        public string FileName { get; set; }

        //[Option("canvas", Default = Canvas.Earth, HelpText = "Canvas that is used (earth, moon or voxel)")]
        //public Canvas Canvas { get; set; }

        [Option("useMirror", Default = false, HelpText = "Makes app use mirror (https://fuckyouarkeros.fun/)")]
        public bool UseMirror { get; set; }

        [Option("serverUrl", Default = null, HelpText = "Custom server URL")]
        public string ServerUrl { get; set; }

        [Option("logFilePath", Default = null, HelpText = "If specified, is used to write logs; otherwise, logs are written to %appdata%/PixelPlanetTools/logs/<app name>")]
        public string LogFilePath { get; set; }

        [Option("disableUpdates", Default = false, HelpText = "If specified, updates are disabled")]
        public bool DisableUpdates { get; set; }

        [Option("showDebug", Default = false, HelpText = "Toggles debug output to console window")]
        public bool ShowDebugLogs { get; set; }
    }
}
