using System;

namespace PixelPlanetBot.Options.Enums
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
