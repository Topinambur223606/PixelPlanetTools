using CommandLine;

namespace RecordVisualizer
{
    class Options
    {
        [Option('f', "fileName", Required = true, HelpText = "Input file")]
        public string FileName { get; set; }

        [Option("disableUpdates", Default = false, HelpText = "If specified, updates are disabled")]
        public bool DisableUpdates { get; set; }
    }
}
