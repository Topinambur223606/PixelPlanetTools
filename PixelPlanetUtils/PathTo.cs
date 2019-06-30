using System;
using System.IO;

namespace PixelPlanetUtils
{
    public static class PathTo
    {
        public static readonly string AppFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetTools");

        public static readonly string Fingerprint = Path.Combine(AppFolder, "fingerprint.bin");


        //for backward compatibility, will be removed soon

        public static readonly string OldAppFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelPlanetTools");

        public static readonly string OldFingerprint = Path.Combine(AppFolder, "fingerprint.bin");
    }
}
