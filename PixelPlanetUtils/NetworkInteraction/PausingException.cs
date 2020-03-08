using System;

namespace PixelPlanetUtils.NetworkInteraction
{
    public class PausingException : ApplicationException
    {
        public PausingException(string message) : base(message)
        { }
    }
}
