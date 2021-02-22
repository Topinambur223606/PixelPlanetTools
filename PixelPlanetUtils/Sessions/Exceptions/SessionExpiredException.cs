using System;

namespace PixelPlanetUtils.Sessions.Exceptions
{
    public class SessionExpiredException : Exception
    {
        public SessionExpiredException() : base("Session is expired, delete it and log in again")
        { }
    }
}
