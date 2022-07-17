using System;

namespace PixelPlanetUtils.NetworkInteraction
{
    public static class UrlManager
    {
        private const string mainUrl = "pixelplanet.fun";
        private const string mirrorUrl = "fuckyouarkeros.fun";

        private static bool? mirrorMode = false;
        private static string baseUrl;

        static UrlManager()
        {
            MirrorMode = false;
        }

        public static string BaseUrl
        {
            get => baseUrl;
            set
            {
                baseUrl = value;
                mirrorMode = null;
            }
        }

        public static bool? MirrorMode
        {
            get => mirrorMode;
            set
            {
                mirrorMode = value ?? throw new ArgumentException("Mirror mode cannot be set to null");
                baseUrl = mirrorMode.Value ? mirrorUrl : mainUrl;
            }
        }

        public static string BaseHttpAdress => $"https://{BaseUrl}";

        public static string WebSocketUrl => $"wss://{BaseUrl}/ws";

        public static string ChunkUrl(byte canvas, byte x, byte y) => $"{BaseHttpAdress}/chunks/{canvas}/{x}/{y}.bmp";

        public static string LoginUrl => $"{BaseHttpAdress}/api/auth/local";

        public static string LogoutUrl => $"{BaseHttpAdress}/api/auth/logout";

        public static string MeUrl => $"{BaseHttpAdress}/api/me";

        public static string CaptchaImageUrl => $"{BaseHttpAdress}/captcha.svg";

        public static string CaptchaPostUrl => $"{BaseHttpAdress}/api/captcha";
    }
}