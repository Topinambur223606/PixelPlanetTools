using System.Runtime.Serialization;

namespace PixelPlanetUtils.NetworkInteraction
{
    [DataContract(Name = "pixel")]
    class PixelPlacingData
    {
        [DataMember(Name = "cn")]
        public byte Canvas { get; set; }

        [DataMember(Name = "clr")]
        public PixelColor Color { get; set; }

        [DataMember(Name = "a")]
        public int ValidationSum => AbsoluteX + AbsoluteY + 8;

        [DataMember(Name = "x")]
        public int AbsoluteX;

        [DataMember(Name = "y")]
        public int AbsoluteY;
    }
}