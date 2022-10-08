using PixelPlanetUtils.NetworkInteraction.Websocket;
using System.Collections.Generic;

namespace PixelPlanetUtils
{
    public static class ErrorTextResources
    {
        public static string Get(ReturnCode returnCode) => errorReasons.TryGetValue(returnCode, out var result) ? result : returnCode.ToString();
        public static string Get(CaptchaReturnCode returnCode) => captchaErrorReasons.TryGetValue(returnCode, out var result) ? result : returnCode.ToString();


        private static readonly Dictionary<ReturnCode, string> errorReasons = new Dictionary<ReturnCode, string>
        {
            [ReturnCode.InvalidCanvas] = "invalid canvas",
            [ReturnCode.InvalidCoordinateX] = "invalid x coordinate",
            [ReturnCode.InvalidCoordinateY] = "invalid y coordinate",
            [ReturnCode.InvalidCoordinateZ] = "invalid z coordinate",
            [ReturnCode.InvalidColor] = "invalid color",
            [ReturnCode.RegisteredUsersOnly] = "canvas is for registered users only",
            [ReturnCode.NotEnoughPlacedForThisCanvas] = "no access to canvas because not enough pixels placed at main canvas",
            [ReturnCode.ProtectedPixel] = "pixel is protected",
            [ReturnCode.IpOverused] = "IP is overused",
            [ReturnCode.ProxyDetected] = "proxy usage is detected"
        };

        private static readonly Dictionary<CaptchaReturnCode, string> captchaErrorReasons = new Dictionary<CaptchaReturnCode, string>
        {
            [CaptchaReturnCode.Expired] = "too long - captcha is expired",
            [CaptchaReturnCode.Failed] = "incorrect captcha answer",
            [CaptchaReturnCode.NoCaptchaId] = "no captcha ID given",
            [CaptchaReturnCode.Timeout] = "no captcha response from server",
            [CaptchaReturnCode.Unknown] = "unknown error"
        };
    }
}
