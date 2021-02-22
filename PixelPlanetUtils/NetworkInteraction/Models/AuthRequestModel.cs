using Newtonsoft.Json;

namespace PixelPlanetUtils.NetworkInteraction.Models
{
    class AuthRequestModel
    {
        [JsonProperty("nameoremail")]
        public string NameOrEmail { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }
    }
}
