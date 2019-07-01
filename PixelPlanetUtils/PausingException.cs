using System;

namespace PixelPlanetUtils
{
    public class PausingException : ApplicationException
    {
        public PausingException(string message) : base (message)
        { }
    }
}
