using System;
using System.IO;

namespace PixelPlanetUtils
{
    public static class PathTo
    {
        public static readonly string AppFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetTools");

        public static readonly string LastCheckFolder = Path.Combine(AppFolder, "lastcheck");

        public static readonly string LogsFolder = Path.Combine(AppFolder, "logs");

        public static readonly string SessionsFolder = Path.Combine(AppFolder, "sessions");

        public static readonly string _AccountsFolder = Path.Combine(AppFolder, "accounts");
    }
}
