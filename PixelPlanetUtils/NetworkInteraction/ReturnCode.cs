namespace PixelPlanetUtils.NetworkInteraction
{
    public enum ReturnCode : byte
    {
        Success = 0,
        InvalidCanvas = 1,
        InvalidCoordinateX = 2,
        InvalidCoordinateY = 3,
        InvalidCoordinateZ = 4,
        InvalidColor = 5,
        RegisteredUsersOnly = 6,
        NotEnoughPlacedForThisCanvas = 7,
        ProtectedPixel = 8,
        IpOverused = 9,
        Captcha = 10,
        ProxyDetected = 11
    }
}
