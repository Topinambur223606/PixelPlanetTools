namespace PixelPlanetUtils.NetworkInteraction
{
    enum Opcode : byte
    {
        ChangedMe = 0xA6,
        Cooldown = 0xC2,
        OnlineCounter = 0xA7,
        PixelUpdated = 0xC1,
        RegisterCanvas = 0xA0,
        RegisterChunk = 0xA1,
        RegisterMultipleChunks = 0xA3,
        RequestChatHistory = 0xA5,
        Unsubscribe = 0xA2,
    }
}