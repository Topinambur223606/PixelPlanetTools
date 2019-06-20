using System;

namespace PixelPlanetBot
{
    [Flags]
    enum CaptchaNotificationMode : byte
    {
        None,
        Sound,
        Browser
    }
}
