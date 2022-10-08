namespace PixelPlanetUtils.NetworkInteraction.Websocket
{
    enum Opcode : byte
    {
        RegisterCanvas = 0xA0,
        RegisterChunk = 0xA1,
        UnregisterChunk = 0xA2,
        RegisterMultipleChunks = 0xA3,
        UnregisterMultipleChunks = 0xA4,
        //RequestChatHistory = 0xA5,
        ChangedMe = 0xA6,
        OnlineCounter = 0xA7,

        Ping = 0xB0,

        PixelUpdate = 0xC1,
        Cooldown = 0xC2,
        PixelReturn = 0xC3,
        CaptchaReturn = 0xC6
    }
}