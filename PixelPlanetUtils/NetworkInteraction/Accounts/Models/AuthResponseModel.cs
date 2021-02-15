using Newtonsoft.Json;
using System.Collections.Generic;

namespace PixelPlanetUtils.NetworkInteraction.Accounts.Models
{
    class AuthResponseModel
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("me")]
        public UserModel User { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; }
    }
}
