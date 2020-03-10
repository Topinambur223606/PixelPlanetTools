using CommandLine;

namespace RecordVisualizer
{
    class Options
    {
        [Option('f', "fileName", Required = true, HelpText = "Input file")]
        public string FileName { get; set; }

        [Option("disableUpdates", Default = false, HelpText = "If specified, updates are disabled")]
        public bool DisableUpdates { get; set; }

        [Option("logFilePath", Default = null, HelpText = "If specified, is used to write logs; otherwise, logs are written to %appdata%/PixelPlanetTools/logs/<app name>")]
        public string LogFilePath { get; set; }

        [Option("oldRecordFile", Default = false, HelpText = "If specified, changes are read without uncompressing (if recorded with watcher older than 2.0)")]
        public bool OldRecordFile { get; set; }

        [Option("showDebug", Default = false, HelpText = "Toggles debug output to console window")]
        public bool ShowDebugLogs { get; set; }
    }
}
