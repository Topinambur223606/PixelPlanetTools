using System;

namespace PixelPlanetUtils.NetworkInteraction.Accounts.Exceptions
{
    class SessionExpiredException : Exception
    {
        public SessionExpiredException() : base("Session is expired, delete it and log in again")
        { }
    }
}
