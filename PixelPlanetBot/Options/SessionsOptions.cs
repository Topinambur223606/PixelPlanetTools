using CommandLine;

namespace PixelPlanetBot.Options
{
    [Verb("sessions", HelpText = "Runs the bot")]
    class SessionsOptions : BotOptions
    {
        [Option('a', "add", HelpText = "If specified, bot will log in using given credentials and save session", SetName = "Log in")]
        public bool Add { get; set; }

        [Option('r', "remove", HelpText = "If specified, bot will log out from and delete session by its name", SetName = "Log out")]
        public bool Remove { get; set; }

        [Option('l', "list", HelpText = "If specified, bot will print all present session names")]
        public bool PrintSessionList { get; set; }

        [Option('u', "username", HelpText = "Username or email for logging in", SetName = "Log in")]
        public string UserName { get; set; }

        [Option('p', "password", HelpText = "Password for login", SetName = "Log in")]
        public string Password { get; set; }

        [Option('s', "session", HelpText = "Session name")]
        public string SessionName { get; set; }
    }
}
