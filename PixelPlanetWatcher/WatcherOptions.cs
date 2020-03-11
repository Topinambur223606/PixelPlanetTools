using CommandLine;
using PixelPlanetUtils.Options;

namespace PixelPlanetWatcher
{
    class WatcherOptions : NetworkAppOptions
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
    }
}
