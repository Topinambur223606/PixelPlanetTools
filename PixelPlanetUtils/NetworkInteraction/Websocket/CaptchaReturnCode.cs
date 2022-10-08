namespace PixelPlanetUtils.NetworkInteraction.Websocket
{
    public enum CaptchaReturnCode : byte
    {
        Success = 0,
        Expired = 1,
        Failed = 2,
        InvalidText = 3,
        NoCaptchaId = 4,

        MaxKnownError = NoCaptchaId,

        Timeout = 100,
        Unknown = 101
    }
}
