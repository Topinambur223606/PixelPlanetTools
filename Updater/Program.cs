using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Updater
{
    static class Program
    {
        static void Main(string[] args)
        {
            try
            {
                try
                {
                    Thread.Sleep(500);
                    byte[] data;
                    using (WebClient wc = new WebClient())
                    {
                        data = wc.DownloadData(args[0]);
                    }
                    File.WriteAllBytes(args[1], data);
                }
                finally
                {
                    if (args.Length > 2)
                    {
                        Process.Start(args[1], Encoding.Default.GetString(Convert.FromBase64String(args[2])));
                    }
                }
            }
            catch
            { }
        }
    }
}
