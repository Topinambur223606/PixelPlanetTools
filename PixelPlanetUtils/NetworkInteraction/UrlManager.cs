using System;

namespace PixelPlanetUtils.NetworkInteraction
{
    public static class UrlManager
    {
        private const string mainHostname = "pixelplanet.fun";
        private const string mirrorHostname = "fuckyouarkeros.fun";

        private static bool? mirrorMode = false;
        private static string hostname;

        static UrlManager()
        {
            MirrorMode = false;
        }

        public static string Hostname
        {
            get => hostname;
            set
            {
                hostname = value;
                mirrorMode = null;
            }
        }

        public static bool? MirrorMode
        {
            get => mirrorMode;
            set
            {
                mirrorMode = value ?? throw new ArgumentException("Mirror mode cannot be set to null");
                hostname = mirrorMode.Value ? mirrorHostname : mainHostname;
            }
        }

        public static bool NoSsl { get; set; }

        public static string BaseHttpAdress => $"{(NoSsl ? "http" : "https")}://{Hostname}";

        public static string WebSocketUrl => $"{(NoSsl ? "ws" : "wss")}://{Hostname}/ws";

        public static string ChunkUrl(byte canvas, byte x, byte y) => $"{BaseHttpAdress}/chunks/{canvas}/{x}/{y}.bmp";

        public static string LoginUrl => $"{BaseHttpAdress}/api/auth/local";

        public static string LogoutUrl => $"{BaseHttpAdress}/api/auth/logout";

        public static string MeUrl => $"{BaseHttpAdress}/api/me";

        public static string CaptchaImageUrl => $"{BaseHttpAdress}/captcha.svg";
    }
}