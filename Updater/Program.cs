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
                Process process = Process.GetProcessById(int.Parse(args[0]));
                string path = process.MainModule.FileName;
                process.WaitForExit();
                try
                {
                    Thread.Sleep(500);
                    byte[] data;
                    using (WebClient wc = new WebClient())
                    {
                        data = wc.DownloadData(args[1]);
                    }
                    File.WriteAllBytes(path, data);
                }
                finally
                {
                    if (args.Length > 2)
                    {
                        Process.Start(path, Encoding.Default.GetString(Convert.FromBase64String(args[2])));
                    }
                }
            }
            catch
            { }
        }
    }
}
