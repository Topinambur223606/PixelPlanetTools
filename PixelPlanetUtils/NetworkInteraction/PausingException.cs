using System;

namespace PixelPlanetUtils.NetworkInteraction
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2237:Mark ISerializable types with serializable", Justification = "Not needed")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Not needed")]
    public class PausingException : ApplicationException
    {
        public PausingException(string message) : base(message)
        { }
    }
}
