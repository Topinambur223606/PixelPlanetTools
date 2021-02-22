using PixelPlanetUtils.NetworkInteraction.Websocket;
using System;
using System.Collections.Generic;
using System.Threading;

namespace PixelPlanetUtils.Logging
{
    public static class LoggerExtensions
    {
        public static int MaxCoordXYLength { get; set; } = 6;
        
        const int maxCoordZLength = 3;
        const int maxPixelMsgLength = 22;

        public static void Log(this Logger logger, string msg, MessageGroup group)
        {
            logger.Log(msg, group, DateTime.Now);
        }

        public static void LogPixel(this Logger logger, string msg, DateTime time, MessageGroup group, int x, int y, string color)
        {
            string text = string.Format("{0} {1} at ({2};{3})",
                                    msg.PadRight(maxPixelMsgLength),
                                    color,
                                    x.ToString().PadLeft(MaxCoordXYLength),
                                    y.ToString().PadLeft(MaxCoordXYLength));
            logger.Log(text, group, time);
        }

        public static void LogVoxel(this Logger logger, string msg, DateTime time, MessageGroup group, int x, int y, int z, string color)
        {
            string text = string.Format("{0} {1} at ({2};{3};{4})",
                                    msg.PadRight(maxPixelMsgLength),
                                    color,
                                    x.ToString().PadLeft(MaxCoordXYLength),
                                    y.ToString().PadLeft(MaxCoordXYLength),
                                    z.ToString().PadLeft(maxCoordZLength));
            logger.Log(text, group, time);
        }


        public static void LogDebug(this Logger logger, string msg)
        {
            string threadId = Thread.CurrentThread.ManagedThreadId.ToString().PadRight(3, ' ');
            logger.Log($"T{threadId} | {msg}", MessageGroup.Debug);
        }

        public static void LogError(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.Error);
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

        public static void LogUpdate(this Logger logger, string msg)
        {
            logger.Log(msg, MessageGroup.Update);
        }

        public static void LogFail(this Logger logger, ReturnCode returnCode)
        {
            logger.Log($"Failed to place: {errorReasons[returnCode]}", MessageGroup.PlaceFail);
        }

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

    }

}
