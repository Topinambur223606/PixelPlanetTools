using System.Collections.Generic;

namespace PixelPlanetUtils.Sessions
{
    public class Session
    {
        public string Username { get; set; }

        public Dictionary<string, string> Cookies { get; set; }
    }
}
