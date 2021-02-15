using Newtonsoft.Json;

namespace PixelPlanetUtils.NetworkInteraction.Accounts.Models
{
    class UserModel
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

}
