using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace PixelPlanetUtils
{
    public class UpdateChecker
    {
        const string latestReleaseUrl = "https://api.github.com/repos/Topinambur223606/PixelPlanetTools/releases/latest";
        private readonly static string updaterPath = Path.Combine(PathTo.AppFolder, "Updater.exe");

        private readonly string appName;
        private string downloadUrl;
        private bool isCompatible;
        
        public UpdateChecker(string appName)
        {
            this.appName = appName;
        }

        private static string GetCompressedArgs()
        {
            IEnumerable<string> modifiedArgs = Environment.GetCommandLineArgs().
                Skip(1).Select(s => s.Contains(" ") ? $"\"{s}\"" : s);
            byte[] bytes = Encoding.Default.GetBytes(string.Join(" ", modifiedArgs));
            return Convert.ToBase64String(bytes);
        }

        public bool UpdateIsAvailable(out string version, out bool isCompatible)
        {
            try
            {
                string json;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "PixelPlanetTools";
                    json = wc.DownloadString("https://api.github.com/repos/Topinambur223606/PixelPlanetTools/releases/latest");
                }
                JObject release = JObject.Parse(json);
                downloadUrl = release["assets"].
                    Single(t => t["name"].ToString().StartsWith(appName, StringComparison.InvariantCultureIgnoreCase))
                    ["browser_download_url"].ToString();
                Version appVersion = Assembly.GetEntryAssembly().GetName().Version;
                version = release["tag_name"].ToString();
                Version availableVersion = Version.Parse(version.TrimStart('v'));
                isCompatible = this.isCompatible = availableVersion.Major == appVersion.Major;
                return 
                    availableVersion.Major > appVersion.Major
                    ||
                    availableVersion.Minor > appVersion.Minor
                    ||
                    availableVersion.Build > appVersion.Build;
            }
            catch
            {
                isCompatible = true;
                version = null;
                return false;
            }
        }

        public void StartUpdate()
        {
            if (!File.Exists(updaterPath))
            {
                Directory.CreateDirectory(PathTo.AppFolder);
                File.WriteAllBytes(updaterPath, Properties.Resources.Updater);
            }
            string args;
            string location = Assembly.GetEntryAssembly().Location;
            if (location.Contains(" "))
            {
                location = $"\"{location}\"";
            }
            if (isCompatible)
            {
                args = $"{downloadUrl} {location} {GetCompressedArgs()}";
            }
            else
            {
                args = $"{downloadUrl} {location}";
            }
            Process.Start(updaterPath, args);
        }
    }
}
