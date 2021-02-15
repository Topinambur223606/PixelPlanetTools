using System;

namespace PixelPlanetUtils.NetworkInteraction.Accounts.Exceptions
{
    class SessionDoesNotExistException : Exception
    {
        public SessionDoesNotExistException() : base("Session does not exist")
        { }
    }
}
