using Newtonsoft.Json.Linq;
using PixelPlanetUtils.Logging;
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
        private readonly Logger logger;
        private readonly string appName;
        private string downloadUrl;
        private bool isCompatible;
        private FileStream lastCheckFileStream;

        static UpdateChecker()
        {
            DirectoryInfo di = new DirectoryInfo(PathTo.AppFolder);
            foreach (FileInfo fi in di.EnumerateFiles("*.lastcheck"))
            {
                fi.Delete();
            }
        }

        public UpdateChecker(Logger logger)
        {
            this.logger = logger;
            appName = Assembly.GetEntryAssembly().GetName().Name;
            lastCheckFilePath = Path.Combine(PathTo.LastCheckFolder, appName + ".lastcheck");
        }

        private static string GetPackedArgs()
        {
            string commandLine = Environment.CommandLine;
            char searched = commandLine.StartsWith("\"") ? '"' : ' ';
            string argsString = commandLine.Substring(commandLine.IndexOf(searched, 1) + 1).Trim();
            byte[] bytes = Encoding.Default.GetBytes(argsString);
            return Convert.ToBase64String(bytes);
        }

        public bool NeedsToCheckUpdates()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lastCheckFilePath));
                while (lastCheckFileStream == null)
                {
                    try
                    {
                        logger.LogDebug("NeedsToCheckUpdates(): trying to access last check file");
                        lastCheckFileStream = File.Open(lastCheckFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (IOException ex)
                    {
                        logger.LogDebug($"NeedsToCheckUpdates(): cannot access last check file: {ex.Message}");
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
            catch (Exception ex)
            {
                logger.LogError($"Error while retrieving last check time: {ex.Message}");
                return false;
            }
        }

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

                Version appVersion = App.Version;
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
            catch (Exception ex)
            {
                logger.LogError($"Error while checking for updates: {ex.Message}");
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
                Directory.CreateDirectory(Path.GetDirectoryName(updaterPath));
                File.WriteAllBytes(updaterPath, Properties.Resources.Updater);
            }
        }

        public void StartUpdate()
        {
            try
            {
                logger.LogDebug("StartUpdate(): unpacking updater if needed");
                UnpackUpdater();
                string args;
                int id = Process.GetCurrentProcess().Id;
                if (isCompatible)
                {
                    args = $"{id} {downloadUrl} {GetPackedArgs()}";
                }
                else
                {
                    args = $"{id} {downloadUrl}";
                }
                Process.Start(updaterPath, args);
            }
            catch (Exception ex)
            {
                logger.LogDebug($"StartUpdate(): exception during update start - {ex.Message}");
            }
        }

        public void Dispose()
        {
            lastCheckFileStream?.Dispose();
        }
    }
}
