using CommandLine;
using PixelPlanetUtils.Options;

namespace PixelPlanetBot.Options
{
    [Verb("sessions", HelpText = "Runs the bot")]
    class SessionsOptions : AppOptions
    {
        [Option('a', "add", HelpText = "If specified, bot will log in using given credentials and save session")]
        public bool Add { get; set; }

        [Option('r', "remove", HelpText = "If specified, bot will log out from and delete session by its name")]
        public bool Remove { get; set; }

        [Option('l', "list", HelpText = "If specified, bot will print all present session names")]
        public bool PrintSessionList { get; set; }

        [Option('u', "username", HelpText = "Username or email for logging in")]
        public string UserName { get; set; }

        [Option('p', "password", HelpText = "Password for login")]
        public string Password { get; set; }

        [Option('s', "session", HelpText = "Session name")]
        public string SessionName { get; set; }
    }
}
