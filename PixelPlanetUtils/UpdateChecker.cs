using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace PixelPlanetUtils
{
    public class UpdateChecker : IDisposable
    {
        private const string latestReleaseUrl = "https://api.github.com/repos/Topinambur223606/PixelPlanetTools/releases/latest";
        private const string updaterFileName = "Updater.exe";
        
        private readonly static string updaterPath = Path.Combine(PathTo.AppFolder, updaterFileName);
        private readonly string lastCheckFilePath;
        private static readonly Version updaterVersion = new Version(3, 1);

        private readonly string appName;
        private string downloadUrl;
        private bool isCompatible;
        private FileStream lastCheckFileStream;

        public UpdateChecker()
        {
            appName = Assembly.GetEntryAssembly().GetName().Name;
            lastCheckFilePath = Path.Combine(PathTo.AppFolder, appName + ".lastcheck");
        }

        private static string GetCompressedArgs()
        {
            string commandLine = Environment.CommandLine;
            char searched = commandLine.StartsWith("\"") ? '"' : ' ';
            string argsString = commandLine.Substring(commandLine.IndexOf(searched, 1) + 1).Trim();
            byte[] bytes = Encoding.Default.GetBytes(argsString);
            return Convert.ToBase64String(bytes);
        }

        public bool NeedsToCheckUpdates()
        {
            Directory.CreateDirectory(PathTo.AppFolder);
            while (lastCheckFileStream == null)
            {
                try
                {
                    lastCheckFileStream = File.Open(lastCheckFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    Thread.Sleep(300);
                }
            }
            if (lastCheckFileStream.Length > 0)
            {
                using (BinaryReader br = new BinaryReader(lastCheckFileStream, Encoding.Default, true))
                {
                    DateTime lastCheck = DateTime.FromBinary(br.ReadInt64());
                    if (DateTime.Now - lastCheck < TimeSpan.FromHours(1))
                    {
                        lastCheckFileStream.Dispose();
                        return false;
                    }
                }
            }
            return true;
        }

        public static Version CurrentAppVersion => Assembly.GetEntryAssembly().GetName().Version;

        public bool UpdateIsAvailable(out string availableVersion, out bool isCompatible)
        {
            try
            {
                JObject release, versions;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "PixelPlanetTools";
                    release = JObject.Parse(wc.DownloadString(latestReleaseUrl));
                    string versionsJsonUrl = release["assets"].
                        Single(t => t["name"].ToString().StartsWith("versions", StringComparison.InvariantCultureIgnoreCase))
                        ["browser_download_url"].ToString();
                    versions = JObject.Parse(wc.DownloadString(versionsJsonUrl));
                }

                downloadUrl = release["assets"].
                    Single(t => t["name"].ToString().StartsWith(appName, StringComparison.InvariantCultureIgnoreCase))
                    ["browser_download_url"].ToString();

                Version appVersion = CurrentAppVersion;
                availableVersion = versions[appName].ToString();
                Version upToDateVersion = Version.Parse(availableVersion);
                isCompatible = this.isCompatible = upToDateVersion.Major == appVersion.Major;
                lastCheckFileStream.Seek(0, SeekOrigin.Begin);
                using (BinaryWriter br = new BinaryWriter(lastCheckFileStream))
                {
                    br.Write(DateTime.Now.ToBinary());
                }
                return appVersion < upToDateVersion;
            }
            catch
            {
                isCompatible = true;
                availableVersion = null;
                return false;
            }
        }

        public static void UnpackUpdater()
        {
            if (!File.Exists(updaterPath) ||
                Version.Parse(FileVersionInfo.GetVersionInfo(updaterPath).FileVersion) < updaterVersion)
            {
                Directory.CreateDirectory(PathTo.AppFolder);
                using (FileStream fileStream = File.Create(updaterPath))
                using (Stream updaterStream = typeof(UpdateChecker).Assembly.GetManifestResourceStream(typeof(UpdateChecker), updaterFileName))
                {
                    updaterStream.CopyTo(fileStream);
                }
            }
        }

        public void StartUpdate()
        {
            UnpackUpdater();
            string args;
            int id = Process.GetCurrentProcess().Id;
            if (isCompatible)
            {
                args = $"{id} {downloadUrl} {GetCompressedArgs()}";
            }
            else
            {
                args = $"{id} {downloadUrl}";
            }
            Process.Start(updaterPath, args);
        }

        public void Dispose()
        {
            lastCheckFileStream?.Dispose();
        }
    }
}
