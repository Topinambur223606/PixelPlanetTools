using CommandLine;
using PixelPlanetBot.Options.Enums;

namespace PixelPlanetBot.Options
{
    [Verb("run", HelpText = "Runs the bot")]
    class Run2DOptions : RunOptions
    {
        [Option('i', "imagePath", Required = true, HelpText = "Path to the image, URL, local absolute or relative")]
        public string ImagePath { get; set; }

        [Option('x', "leftX", Required = true, HelpText = "X coord of left image pixel")]
        public short LeftX { get; set; }

        [Option('y', "topY", Required = true, HelpText = "Y coord of top image pixel")]
        public short TopY { get; set; }

        [Option("placingOrder", Default = PlacingOrderMode2D.Random, HelpText = "Determines how app places pixels")]
        public PlacingOrderMode2D PlacingOrderMode { get; set; }

        [Option("brightnessMaskPath", HelpText = "Path to the image for advanced pixel placing order management, URL, local absolute or relative")]
        public string BrightnessMaskImagePath { get; set; }
    }
}
