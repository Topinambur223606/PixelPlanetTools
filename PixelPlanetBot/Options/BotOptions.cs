using CommandLine;
using PixelPlanetUtils.Options;

namespace PixelPlanetBot.Options
{
    class BotOptions : NetworkAppOptions
    {
        [Option("proxyAddress", Default = null, HelpText = "Proxy address that is used for placing pixels")]
        public string ProxyAddress { get; set; }

        [Option("proxyUsername", Default = null, HelpText = "Username for connecting to proxy if specified")]
        public string ProxyUsername { get; set; }

        [Option("proxyPassword", Default = null, HelpText = "Password for connecting to proxy if specified")]
        public string ProxyPassword { get; set; }
    }
}
