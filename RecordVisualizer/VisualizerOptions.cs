using CommandLine;
using PixelPlanetUtils.Canvas;
using PixelPlanetUtils.Options;

namespace RecordVisualizer
{
    [Verb("run", HelpText = "Runs the visualizer")]
    class VisualizerOptions : AppOptions
    {
        [Option('f', "fileName", Required = true, HelpText = "Input file")]
        public string FileName { get; set; }

        [Option("oldRecordFile", Default = false, HelpText = "Specify this flag for records of watcher 2.0 - 3.2")]
        public bool OldRecordFile { get; set; }

        [Option('c', "canvas", Hidden = true)] //option is ignored: old are earth only, new contain canvas info
        public override CanvasType Canvas { get; set; }
    }
}
