using Newtonsoft.Json;

namespace PixelPlanetUtils.NetworkInteraction.Models
{
    class CaptchaPostRequest
    {
        public CaptchaPostRequest(string text, string captchaId)
        {
            Text = text;
            CaptchaId = captchaId;
        }

        [JsonProperty("text")]
        public string Text { get; }
        
        [JsonProperty("id")]
        public string CaptchaId { get; private set; }
    }
}
