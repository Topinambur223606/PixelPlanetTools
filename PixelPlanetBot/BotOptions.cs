using CommandLine;
using PixelPlanetUtils.Options;

namespace PixelPlanetBot
{
    class BotOptions : NetworkAppOptions
    {
        [Option('x', "leftX", Required = true, HelpText = "X coord of left picture pixel")]
        public short LeftX { get; set; }

        [Option('y', "topY", Required = true, HelpText = "Y coord of top picture pixel")]
        public short TopY { get; set; }

        [Option('i', "imagePath", Required = true, HelpText = "Path to the image, URL, local absolute or relative")]
        public string ImagePath { get; set; }

        [Option('d', "defenseMode", Default = false, HelpText = "Determines whether app continues observation and defense after picture building is finished")]
        public bool DefenseMode { get; set; }

        [Option("notificationMode", Default = CaptchaNotificationMode.Sound, HelpText = "Determines how app reacts to captcha (sound, browser, both)")]
        public CaptchaNotificationMode NotificationMode { get; set; }

        [Option("placingOrder", Default = PlacingOrderMode.Random, HelpText = "Determines how app places pixels (random, left, right, top, bottom, outline)")]
        public PlacingOrderMode PlacingOrderMode { get; set; }

        [Option("proxyAddress", Default = null, HelpText = "Proxy address that is used for placing pixels")]
        public string Proxy { get; set; }
    }
}
