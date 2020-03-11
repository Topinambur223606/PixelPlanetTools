using System;
using System.Reflection;

namespace PixelPlanetUtils.Updates
{
    public static class AppInfo
    {
        private static readonly AssemblyName assemblyName = Assembly.GetEntryAssembly().GetName();

        public static Version Version => assemblyName.Version;

        public static string Name => assemblyName.Name;
    }
}
