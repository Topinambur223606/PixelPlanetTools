using CommandLine;
using PixelPlanetBot.Options.Enums;
using PixelPlanetUtils.Options;

namespace PixelPlanetBot.Options
{
    class RunOptions : AppOptions
    {
        [Option('i', "imagePath", Required = true, HelpText = "Path to the image (CSV for 3d canvas), URL, local absolute or relative")]
        public string ImagePath { get; set; }

        [Option('d', "defenseMode", Default = false, HelpText = "Determines whether app continues observation and defense after building is finished")]
        public bool DefenseMode { get; set; }

        [Option("notificationMode", Default = CaptchaNotificationMode.Sound, HelpText = "Determines how app reacts to captcha. Available options: sound, browser, both, none")]
        public CaptchaNotificationMode NotificationMode { get; set; }

        [Option("captchaTimeout", HelpText = "If specified and greater than zero, bot will wait corresponding amount of time (in seconds) for user to solve captcha instead of waiting for key press", Default = 0)]
        public int CaptchaTimeout { get; set; }

        [Option('s', "session", HelpText = "Name of previously created session")]
        public string SessionName { get; set; }
    }
}
