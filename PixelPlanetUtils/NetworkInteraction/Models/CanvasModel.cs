using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace PixelPlanetUtils.NetworkInteraction.Models
{
    public class CanvasModel
    {
        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("v")]
        public bool Is3D { get; set; }

        [JsonProperty("colors")]
        public List<List<byte>> Colors { get; set; }

        [JsonProperty("bcd")]
        public int PlaceCooldown { get; set; }

        [JsonProperty("pcd")]
        public int ReplaceCooldown { get; set; }

        [JsonProperty("cds")]
        public int CumulativeCooldown { get; set; }

        public int TimeBuffer => CumulativeCooldown - ReplaceCooldown;

        public double OptimalCooldown => Math.Max(0D, (39D * PlaceCooldown - 16000D) / 140D); //1s on Earth, 0.44s on voxels, 1.8s on 1bit, 25ms on coronavirus
    }
}
