using CommandLine;
using PixelPlanetUtils.Options;

namespace RecordVisualizer
{
    [Verb("run", HelpText = "Runs the visualizer")]
    class VisualizerOptions : AppOptions
    {
        [Option('f', "fileName", Required = true, HelpText = "Input file")]
        public string FileName { get; set; }

        [Option("oldRecordFile", Default = false, HelpText = "If specified, changes are read without uncompressing (if recorded with watcher older than 2.0)")]
        public bool OldRecordFile { get; set; }
    }
}
