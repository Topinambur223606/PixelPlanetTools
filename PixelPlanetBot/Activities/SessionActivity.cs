using PixelPlanetBot.Activities.Abstract;
using PixelPlanetBot.Options;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PixelPlanetBot.Activities
{
    class SessionActivity : IActivity
    {
        private readonly Logger logger;
        private readonly SessionsOptions options;
        private readonly SessionManager sessionManager;

        public SessionActivity(Logger logger, SessionsOptions options, ProxySettings proxySettings)
        {
            this.logger = logger;
            this.options = options;
            sessionManager = new SessionManager(proxySettings);
        }

        public async Task Run()
        {
            if (options.Add)
            {
                await AddSession(logger, options);
            }

            if (options.Remove)
            {
                await RemoveSession(logger, options);
            }

            if (options.PrintSessionList)
            {
                PrintSessions();
            }
        }

        private void PrintSessions()
        {
            List<string> sessions = sessionManager.GetSessions();
            Console.ForegroundColor = ConsoleColor.White;
            if (sessions.Count > 0)
            {
                Console.WriteLine("All sessions:");
                foreach (string session in sessions.OrderBy(s => s.ToLower()))
                {
                    Console.WriteLine(session);
                }
            }
            else
            {
                Console.WriteLine("No sessions found");
            }
        }

        private async Task RemoveSession(Logger logger, SessionsOptions options)
        {
            logger.LogTechState("Logging out...");
            await sessionManager.Logout(options.SessionName);
            logger.LogTechInfo("Successfully logged out");
        }

        private async Task AddSession(Logger logger, SessionsOptions options)
        {
            logger.LogTechState("Logging in...");
            string sessionName = await sessionManager.Login(options.UserName, options.Password, options.SessionName);
            logger.LogTechInfo($"Successfully logged in with session name \"{sessionName}\"");
        }

        public void Dispose()
        { }
    }
}
