using System;

namespace PixelPlanetBot
{
    [Flags]
    enum CaptchaNotificationMode : byte
    {
        None = 0b00,
        Sound = 0b01,
        Browser = 0b10,
        Both = Sound | Browser
    }
}
