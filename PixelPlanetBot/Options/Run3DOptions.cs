using CommandLine;
using PixelPlanetBot.Options.Enums;
using PixelPlanetUtils.Canvas;

namespace PixelPlanetBot.Options
{
    [Verb("run3d", HelpText = "Runs the bot (for voxel canvas)")]
    class Run3DOptions : RunOptions
    {
        [Option('i', "imagePath", Group = "Image", HelpText = "Path to the Sproxel PNG, URL, local absolute or relative")]
        public string ImagePath { get; set; }

        [Option('t', "templatePath", Group = "Image", HelpText = "Path to the Sproxel CSV, URL, local absolute or relative")]
        public string DocumentPath { get; set; }

        [Option('c', "canvas", Default = CanvasType.Voxel, HelpText = "Canvas to operate at")]
        public override CanvasType Canvas { get; set; }

        [Option('x', "minX", Required = true, HelpText = "X coord of template min-X border")]
        public short MinX { get; set; }

        [Option('y', "minY", Required = true, HelpText = "Y coord of template min-Y border")]
        public short MinY { get; set; }

        [Option('z', "bottomZ", HelpText = "Z coord of bottom (min Z) voxel")]
        public byte BottomZ { get; set; }

        [Option("placingOrder", Default = PlacingOrderMode3D.Random, HelpText = "Determines how app places voxels")]
        public PlacingOrderMode3D PlacingOrderMode { get; set; }
    }
}
