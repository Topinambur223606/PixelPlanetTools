using Newtonsoft.Json;
using PixelPlanetUtils.Canvas;
using System.Collections.Generic;

namespace PixelPlanetUtils.NetworkInteraction.Models
{
    public class UserModel : SimpleUserModel
    {
        [JsonProperty("canvases")]
        public Dictionary<CanvasType, CanvasModel> Canvases { get; set; }
    }
}
