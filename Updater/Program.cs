using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace Updater
{
    static class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    Console.WriteLine("Downloading...");
                    System.Threading.Tasks.Task<byte[]> dataTask = wc.DownloadDataTaskAsync(args[1]);
                    Process process = Process.GetProcessById(int.Parse(args[0]));
                    string path = process.MainModule.FileName;
                    Console.WriteLine("Waiting for bot to finish...");
                    process.WaitForExit();
                    dataTask.ContinueWith(t => Console.WriteLine("Downloaded!")).Wait();
                    Console.WriteLine("Writing new version to disk...");
                    try
                    {
                        File.WriteAllBytes(path, dataTask.Result);
                    }
                    finally
                    {
                        if (args.Length > 2)
                        {
                            Process.Start(path, Encoding.Default.GetString(Convert.FromBase64String(args[2])));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
