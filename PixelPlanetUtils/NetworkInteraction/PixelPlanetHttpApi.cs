using Newtonsoft.Json;
using PixelPlanetUtils.NetworkInteraction.Models;
using PixelPlanetUtils.Sessions;
using Svg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PixelPlanetUtils.NetworkInteraction
{
    public class PixelPlanetHttpApi
    {
        public ProxySettings ProxySettings { get; set; }

        public Session Session { get; set; }

        private string captchaId;

        private HttpClientHandler GetHttpClientHandler()
        {
            HttpClientHandler handler = new HttpClientHandler();
            if (ProxySettings != null)
            {
                handler.Proxy = new WebProxy
                {
                    Address = new Uri(ProxySettings.Address),
                    Credentials = new NetworkCredential
                    {
                        UserName = ProxySettings.Username,
                        Password = ProxySettings.Password
                    }
                };
            }
            if (Session != null)
            {
                foreach (KeyValuePair<string, string> cookie in Session.Cookies)
                {
                    handler.CookieContainer.Add(new Cookie
                    {
                        Name = cookie.Key,
                        Value = cookie.Value,
                        Domain = UrlManager.Hostname
                    });
                }
            }
            return handler;
        }

        private static HttpClient GetHttpClient(HttpClientHandler handler)
        {
            HttpClient client = new HttpClient(handler);
            HttpRequestHeaders headers = client.DefaultRequestHeaders;
            headers.UserAgent.ParseAdd(HttpHeaderValues.UserAgent);
            Uri uri = new Uri(UrlManager.BaseHttpAdress);
            headers.Referrer = uri;
            headers.Add(HttpHeaderValues.Origin, uri.AbsoluteUri);
            return client;
        }

        public async Task<UserModel> GetMeAsync(CancellationToken cancellationToken = default)
        {
            using (HttpClientHandler handler = GetHttpClientHandler())
            {
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    Uri uri = new Uri(UrlManager.MeUrl);
                    using (HttpResponseMessage responseMessage = await httpClient.GetAsync(uri, cancellationToken))
                    {
                        string responseJson = await responseMessage.Content.ReadAsStringAsync();
                        UserModel response = JsonConvert.DeserializeObject<UserModel>(responseJson);
                        return response;
                    }
                }
            }
        }

        public async Task<SvgDocument> GetCaptchaImageAsync(CancellationToken cancellationToken = default)
        {
            using (HttpClientHandler handler = GetHttpClientHandler())
            {
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    Uri uri = new Uri(UrlManager.CaptchaImageUrl);
                    using (HttpResponseMessage responseMessage = await httpClient.GetAsync(uri, cancellationToken))
                    {
                        captchaId = responseMessage.Headers.GetValues("captcha-id").FirstOrDefault();
                        using (Stream svgStream = await responseMessage.Content.ReadAsStreamAsync())
                        {
                            return SvgDocument.Open<SvgDocument>(svgStream);
                        }
                    }
                }
            }
        }

        public async Task Login(string nameOrEmail, string password, CancellationToken cancellationToken = default)
        {
            AuthRequestModel authModel = new AuthRequestModel
            {
                NameOrEmail = nameOrEmail,
                Password = password
            };
            string json = JsonConvert.SerializeObject(authModel);

            using (HttpClientHandler handler = GetHttpClientHandler())
            {
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        Uri uri = new Uri(UrlManager.LoginUrl);
                        using (HttpResponseMessage responseMessage = await httpClient.PostAsync(uri, content, cancellationToken))
                        {
                            string responseJson = await responseMessage.Content.ReadAsStringAsync();
                            AuthResponseModel response = JsonConvert.DeserializeObject<AuthResponseModel>(responseJson);
                            if (response.Success)
                            {
                                Session = new Session
                                {
                                    Username = response.User.Name,
                                    Cookies = handler.CookieContainer
                                    .GetCookies(new Uri(UrlManager.BaseHttpAdress))
                                    .Cast<Cookie>().ToDictionary(c => c.Name, c => c.Value)
                                };
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

        public async Task Logout(CancellationToken cancellationToken = default)
        {
            using (HttpClientHandler handler = GetHttpClientHandler())
            {
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    Uri uri = new Uri(UrlManager.LogoutUrl);
                    using (HttpResponseMessage responseMessage = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        Session = null;
                        return;
                    }
                }
            }
        }

        public async Task PostCaptchaText(string text, CancellationToken cancellationToken = default)
        {
            CaptchaPostRequest request = new CaptchaPostRequest(text, captchaId);
            string json = JsonConvert.SerializeObject(request);

            using (HttpClientHandler handler = GetHttpClientHandler())
            {
                using (HttpClient httpClient = GetHttpClient(handler))
                {
                    using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        Uri uri = new Uri(UrlManager.CaptchaPostUrl);
                        using (HttpResponseMessage responseMessage = await httpClient.PostAsync(uri, content, cancellationToken))
                        {
                            string responseJson = await responseMessage.Content.ReadAsStringAsync();
                            CaptchaPostResponse response = JsonConvert.DeserializeObject<CaptchaPostResponse>(responseJson);
                            if (!response.Success)
                            {
                                throw new Exception(response.Errors.First());
                            }
                        }
                    }
                }
            }
        }

    }
}
