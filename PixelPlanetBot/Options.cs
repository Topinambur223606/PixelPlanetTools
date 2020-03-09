using CommandLine;

namespace PixelPlanetBot
{
    class Options
    {
        [Option('x', "leftX", Required = true, HelpText = "X coord of left picture pixel")]
        public short LeftX { get; set; }

        [Option('y', "topY", Required = true, HelpText = "Y coord of top picture pixel")]
        public short TopY { get; set; }

        [Option('i', "imagePath", Required = true, HelpText = "Path to the image, URL, local absolute or relative")]
        public string ImagePath { get; set; }

        [Option('d', "defenseMode", Default = false, HelpText = "Determines whether app continues observation and defense after picture building is finished")]
        public bool DefenseMode { get; set; }

        [Option("captchaNotification", Default = CaptchaNotificationMode.Sound, HelpText = "Determines how app reacts to captcha (sound, browser, both)")]
        public CaptchaNotificationMode CaptchaNotificationMode { get; set; }

        [Option("placingOrder", Default = PlacingOrderMode.Random, HelpText = "Determines how app places pixels (random, left, right, top, bottom, outline)")]
        public PlacingOrderMode PlacingOrderMode { get; set; }

        [Option("proxyAddress", Default = null, HelpText = "Proxy address that is used for placing pixels")]
        public string Proxy { get; set; }

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
    }
}
