using Newtonsoft.Json;

namespace PixelPlanetUtils.NetworkInteraction.Models
{
    class CaptchaPostRequest
    {
        public CaptchaPostRequest(string text)
        {
            Text = text;
        }

        [JsonProperty("text")]
        public string Text { get; }
    }
}
