using CommandLine;

namespace PixelPlanetBot.Options
{
    [Verb("run", HelpText = "Runs the bot")]
    class RunOptions : BotOptions
    {
        [Option('x', "leftX", Required = true, HelpText = "X coord of left picture pixel")]
        public short LeftX { get; set; }

        [Option('y', "topY", Required = true, HelpText = "Y coord of top picture pixel")]
        public short TopY { get; set; }

        [Option('i', "imagePath", Required = true, HelpText = "Path to the image, URL, local absolute or relative")]
        public string ImagePath { get; set; }

        [Option('d', "defenseMode", Default = false, HelpText = "Determines whether app continues observation and defense after picture building is finished")]
        public bool DefenseMode { get; set; }

        [Option("notificationMode", Default = CaptchaNotificationMode.Sound, HelpText = "Determines how app reacts to captcha. Available options: sound, browser, both, none")]
        public CaptchaNotificationMode NotificationMode { get; set; }

        [Option("placingOrder", Default = PlacingOrderMode.Random, HelpText = "Determines how app places pixels. Available options: random, outline, left, right, top, bottom, color, colorDsc, colorRnd, combined directional (e.g. leftTop, bottomRight), color-directional (e.g. colorTop, colorDescBottom, colorRndRight), mask-dependent (e.g. maskTop, maskColor)")]
        public PlacingOrderMode PlacingOrderMode { get; set; }

        [Option("brightnessMaskPath", HelpText = "Path to the image for advanced pixel placing order management, URL, local absolute or relative")]
        public string BrightnessMaskImagePath { get; set; }

        [Option("captchaTimeout", HelpText = "If specified and greater than zero, bot will wait corresponding amount of time (in seconds) for user to solve captcha instead of waiting for key press", Default = 0)]
        public int CaptchaTimeout { get; set; }

        [Option('s', "session", HelpText = "Name of previously created session")]
        public string SessionName { get; set; }
    }
}
