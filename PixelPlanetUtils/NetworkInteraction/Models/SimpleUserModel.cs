using Newtonsoft.Json;

namespace PixelPlanetUtils.NetworkInteraction.Models
{
    public class SimpleUserModel
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

}
