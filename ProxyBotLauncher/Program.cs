using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace ProxyBotLauncher
{
    class Program
    {
        private const string url = "http://localhost:12345/launch/";
        private static readonly byte[] pageBytes = Encoding.UTF8.GetBytes(Properties.Resources.Launch);

        static void Main(string[] args)
        {
            try
            {
                var fi = new FileInfo(args[0]);
                if (!fi.Exists)
                {
                    throw new Exception();
                }
                string.Format(args[1], 0, 0);
                Environment.CurrentDirectory = fi.DirectoryName;
            }
            catch
            {
                Console.WriteLine("Parameters: <path to bot> <args string with fields {0} for fingerprint and {1} for proxy address>");
                Environment.Exit(0);
            }
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();
            Console.WriteLine("Listening at " + url);
            while (true)
            {
                HttpListenerContext context = listener.GetContext();
                try
                {
                    HttpListenerRequest request = context.Request;

                    if (request.QueryString.Count > 0)
                    {
                        try
                        {
                            Process.Start(args[0], string.Format(args[1], request.QueryString["fingerprint"], request.QueryString["proxyAddress"]));
                            Console.WriteLine($"Launched instance for proxy {request.QueryString["proxyAddress"]}");
                        }
                        finally
                        {
                            context.Response.Redirect(url);
                            context.Response.Close();
                        }
                    }
                    else
                    {
                        HttpListenerResponse response = context.Response;
                        using (var s = response.OutputStream)
                        {
                            s.Write(pageBytes, 0, pageBytes.Length);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        class LaunchDetails
        {
            public string Fingerprint { get; set; }

            public string ProxyAddress { get; set; }
        }


    }
}
