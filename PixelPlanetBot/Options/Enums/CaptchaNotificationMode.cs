using System;

namespace PixelPlanetBot.Options.Enums
{
    [Flags]
    enum CaptchaNotificationMode : byte
    {
        None =                  0b0000,
        Sound =                 0b0001,
        Browser =               0b0010,
        Solver =                0b0100,
        ShowInBackground =      0b1000,

        BgSolver = Solver | ShowInBackground,
        SoundBrowser = Sound | Browser,
        SoundSolver = Sound | Solver,
        SoundBgSolver = Sound | BgSolver
    }
}
