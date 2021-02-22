using System;
using System.Diagnostics.CodeAnalysis;

namespace PixelPlanetUtils.Imaging
{

    namespace Exceptions
    {
        [SuppressMessage("Design", "CA1032:Implement standard exception constructors")]
        [SuppressMessage("Usage", "CA2237:Mark ISerializable types with serializable")]
        public class MaskInvalidSizeException : Exception
        {
            public MaskInvalidSizeException() : base ("Image and mask sizes are not equal")
            { }
        }
    }

}
