using System;

namespace PixelPlanetUtils.Logging
{
    public static class LoggerExtensions
    {
        public static void Log(this Logger logger, string msg, MessageGroup group)
        {
            logger.Log(msg, group, DateTime.Now);
        }

        public static void LogPixel(this Logger logger, string msg, DateTime time, MessageGroup group, int x, int y, PixelColor color)
        {
            string text = $"{msg.PadRight(22)} {color.ToString().PadRight(13)} at ({x.ToString().PadLeft(6)};{y.ToString().PadLeft(6)})";
            logger.Log(text, group, time);
        }

        public static void LogDebug(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.Debug);
        }

        public static void LogError(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.Error);
        }
        public static void LogWarning(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.Warning);
        }

        public static void LogInfo(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.Info);
        }

        public static void LogTechState(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.TechState);
        }

        public static void LogTechInfo(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.TechInfo);
        }

    }

}
