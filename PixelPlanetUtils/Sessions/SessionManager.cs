using Newtonsoft.Json;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Models;
using PixelPlanetUtils.NetworkInteraction.Sessions.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PixelPlanetUtils.Sessions
{
    public class SessionManager
    {
        private const string oldExt = "cookies";
        private const string ext = "session";
        private readonly DirectoryInfo sessionsDir, oldDir;
        private readonly ProxySettings proxySettings;

        public List<string> GetOldSessions()
        {
            if (oldDir.Exists)
            {
                return oldDir.EnumerateFiles($"*.{oldExt}")
                            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                            .ToList();
            }
            else
            {
                return null;
            }
        }

        public async Task MigrateOldSessions()
        {
            if (!oldDir.Exists)
            {
                return;
            }
            foreach (FileInfo oldSession in oldDir
                        .EnumerateFiles($"*.{oldExt}"))
            {
                string json = File.ReadAllText(oldSession.FullName);
                List<Cookie> cookies = JsonConvert.DeserializeObject<List<Cookie>>(json);
                Session session = new Session
                {
                    Cookies = cookies.ToDictionary(c => c.Name, c => c.Value)
                };
                PixelPlanetHttpApi api = new PixelPlanetHttpApi
                {
                    ProxySettings = proxySettings,
                    Session = session
                };
                UserModel me = await api.GetMeAsync();
                if (me.Name != null)
                {
                    session.Username = me.Name;
                    string newJson = JsonConvert.SerializeObject(session);
                    string filePath = Path.Combine(PathTo.SessionsFolder, $"{Path.GetFileNameWithoutExtension(oldSession.Name)}.{ext}");
                    File.WriteAllText(filePath, newJson);
                }
            }
        }

        public void DeleteOldSessionFolder()
        {
            if (oldDir.Exists)
            {
                oldDir.Delete(true);
            }
        }

        public SessionManager(ProxySettings proxySettings)
        {
            oldDir = new DirectoryInfo(PathTo._AccountsFolder);
            sessionsDir = new DirectoryInfo(PathTo.SessionsFolder);
            this.proxySettings = proxySettings;
        }

        public List<string> GetSessions()
        {
            sessionsDir.Create();
            return sessionsDir
                        .EnumerateFiles($"*.{ext}")
                        .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                        .ToList();
        }

        public async Task<string> Login(string nameOrEmail, string password, string sessionName)
        {
            sessionsDir.Create();
            PixelPlanetHttpApi api = new PixelPlanetHttpApi
            {
                ProxySettings = proxySettings
            };
            await api.Login(nameOrEmail, password);
            sessionName = Regex.Replace(sessionName ?? api.Session.Username, @"[^\w-.]", "_");
            string filePath = Path.Combine(sessionsDir.FullName, $"{sessionName}.{ext}");

            if (File.Exists(filePath))
            {
                int index = 0;
                string altSessionName, altPath;
                do
                {
                    altSessionName = $"{sessionName}_{++index}";
                    altPath = GetSessionFilePath(altSessionName);
                }
                while (File.Exists(altPath));
                filePath = altPath;
                sessionName = altSessionName;
            }
            File.WriteAllText(filePath, JsonConvert.SerializeObject(api.Session));
            return sessionName;
        }

        public Session GetSession(string sessionName)
        {
            sessionsDir.Create();
            string filePath = GetSessionFilePath(sessionName);
            if (!File.Exists(filePath))
            {
                throw new SessionDoesNotExistException();
            }
            string json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<Session>(json);
        }

        private string GetSessionFilePath(string sessionName)
        {
            return Path.Combine(sessionsDir.FullName, $"{sessionName}.{ext}");
        }

        public async Task Logout(string sessionName)
        {
            sessionsDir.Create();
            Session session = GetSession(sessionName);
            PixelPlanetHttpApi api = new PixelPlanetHttpApi
            {
                ProxySettings = proxySettings,
                Session = session
            };
            await api.Logout();
            File.Delete(GetSessionFilePath(sessionName));
        }
    }
}
