namespace PixelPlanetUtils.NetworkInteraction
{
    public static class UrlManager
    {
        public static bool MirrorMode
        {
            get => mirrorMode;
            set
            {
                mirrorMode = value;
                BaseUrl = mirrorMode ? mirrorUrl : mainUrl;
            }
        }

        static UrlManager()
        {
            MirrorMode = false;
        }

        private const string mainUrl = "pixelplanet.fun";
        private const string mirrorUrl = "fuckyouarkeros.fun";
        private static bool mirrorMode = false;

        public static string BaseHttpAdress => $"https://{BaseUrl}";
        public static string WebSocketUrl => $"wss://{BaseUrl}/ws";

        public static string BaseUrl { get; private set; }
    }
}
