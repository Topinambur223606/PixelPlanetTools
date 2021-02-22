using System;
using System.Threading.Tasks;

namespace PixelPlanetBot.Activities.Abstract
{
    interface IActivity : IDisposable
    {
        Task Run();
    }
}
