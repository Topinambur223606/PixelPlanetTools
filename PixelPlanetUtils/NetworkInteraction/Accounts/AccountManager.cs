using Newtonsoft.Json;
using PixelPlanetUtils.NetworkInteraction.Accounts.Exceptions;
using PixelPlanetUtils.NetworkInteraction.Accounts.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PixelPlanetUtils.NetworkInteraction.Accounts
{
    public static class AccountManager
    {
        private static readonly DirectoryInfo accountsDir = new DirectoryInfo(PathTo.AccountsFolder);
        private const string ext = "cookies";

        public static List<string> GetSessions()
        {
            accountsDir.Create();
            return accountsDir
                        .EnumerateFiles($"*.{ext}")
                        .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                        .ToList();
        }

        public async static Task<string> Login(string nameOrEmail, string password, string sessionName)
        {
            accountsDir.Create();
            AuthRequestModel authModel = new AuthRequestModel
            {
                NameOrEmail = nameOrEmail,
                Password = password
            };
            string json = JsonConvert.SerializeObject(authModel);

            using (HttpClientHandler handler = new HttpClientHandler())
            {
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        Uri uri = new Uri(UrlManager.LoginUrl);
                        using (HttpResponseMessage responseMessage = await httpClient.PostAsync(uri, content))
                        {
                            string responseJson = await responseMessage.Content.ReadAsStringAsync();
                            AuthResponseModel response = JsonConvert.DeserializeObject<AuthResponseModel>(responseJson);
                            if (response.Success)
                            {
                                IEnumerable<Cookie> cookies = handler.CookieContainer.GetCookies(new Uri(UrlManager.BaseHttpAdress)).Cast<Cookie>();
                                string cookiesJson = JsonConvert.SerializeObject(cookies);
                                sessionName = Regex.Replace(sessionName ?? response.User.Name, @"[^\w-.]", "_");
                                string filePath = Path.Combine(accountsDir.FullName, $"{sessionName}.{ext}");

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
                                File.WriteAllText(filePath, cookiesJson);
                                return sessionName;
                            }
                            else
                            {
                                if (response.Errors != null && response.Errors.Count > 0)
                                {
                                    throw new Exception(response.Errors.First());
                                }
                                else
                                {
                                    throw new Exception($"Server responded with code {(int)responseMessage.StatusCode} {responseMessage.StatusCode} while logging in");
                                }
                            }
                        }
                    }
                }
            }
        }

        private static HttpClient GetHttpClient(HttpClientHandler handler)
        {
            HttpClient client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpHeaderValues.UserAgent);
            Uri uri = new Uri(UrlManager.BaseHttpAdress);
            client.DefaultRequestHeaders.Referrer = uri;
            client.DefaultRequestHeaders.Add(HttpHeaderValues.Origin, uri.AbsoluteUri);
            return client;
        }

        public static List<Cookie> GetSessionCookies(string sessionName)
        {
            accountsDir.Create();
            string filePath = GetSessionFilePath(sessionName);
            if (!File.Exists(filePath))
            {
                throw new SessionDoesNotExistException();
            }
            string json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<List<Cookie>>(json);
        }

        public async static Task CheckSession(List<Cookie> cookies)
        {
            using (HttpClientHandler handler = new HttpClientHandler())
            {
                foreach (Cookie cookie in cookies)
                {
                    handler.CookieContainer.Add(cookie);
                }
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    Uri uri = new Uri(UrlManager.MeUrl);
                    using (HttpResponseMessage responseMessage = await httpClient.GetAsync(uri))
                    {
                        string responseJson = await responseMessage.Content.ReadAsStringAsync();
                        UserModel response = JsonConvert.DeserializeObject<UserModel>(responseJson);
                        if (response.Name == null)
                        {
                            throw new SessionExpiredException();
                        }
                    }
                }
            }
        }

        private static string GetSessionFilePath(string sessionName)
        {
            return Path.Combine(accountsDir.FullName, $"{sessionName}.{ext}");
        }

        public async static Task Logout(string sessionName)
        {
            accountsDir.Create();
            List<Cookie> cookies = GetSessionCookies(sessionName);
            File.Delete(GetSessionFilePath(sessionName));
            using (HttpClientHandler handler = new HttpClientHandler())
            {
                foreach (Cookie cookie in cookies)
                {
                    handler.CookieContainer.Add(cookie);
                }
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    Uri uri = new Uri(UrlManager.LogoutUrl);
                    using (HttpResponseMessage responseMessage = await httpClient.GetAsync(uri))
                    {
                        return;
                    }
                }
            }
        }
    }
}
