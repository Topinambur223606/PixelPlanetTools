using System;

namespace PixelPlanetUtils.NetworkInteraction.Sessions.Exceptions
{
    class SessionDoesNotExistException : Exception
    {
        public SessionDoesNotExistException() : base("Session does not exist")
        { }
    }
}
