using CommandLine;

namespace PixelPlanetUtils.Options
{
    public abstract class NetworkAppOptions : AppOptions
    {
        [Option("useMirror", SetName = "useMirror", Default = false, HelpText = "Makes app use mirror (https://fuckyouarkeros.fun/)")]
        public bool UseMirror { get; set; }

        [Option("serverUrl", SetName = "serverUrl", Default = null, HelpText = "Custom server URL")]
        public string ServerUrl { get; set; }
    }
}
