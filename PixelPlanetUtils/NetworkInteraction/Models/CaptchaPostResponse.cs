using Newtonsoft.Json;
using System.Collections.Generic;

namespace PixelPlanetUtils.NetworkInteraction.Models
{
    class CaptchaPostResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; }
    }

}
