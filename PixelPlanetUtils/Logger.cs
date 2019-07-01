using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace PixelPlanetUtils
{

    using LogEntry = ValueTuple<string, ConsoleColor>;

    public class Logger : IDisposable
    {

        private readonly ConcurrentQueue<LogEntry> messages = new ConcurrentQueue<LogEntry>();
        private readonly ConcurrentQueue<LogEntry> consoleMessages = new ConcurrentQueue<LogEntry>();
        private readonly AutoResetEvent messagesAvailable = new AutoResetEvent(false);
        private readonly AutoResetEvent consoleMessagesAvailable = new AutoResetEvent(false);
        private readonly ManualResetEvent noMessagesInConsoleQueue = new ManualResetEvent(true);
        private readonly CancellationToken finishToken;
        private readonly string logFilePath;
        private readonly bool logToFile;
        bool disposed;
        bool consolePaused = false;
        private Thread loggingThread, consoleThread;

        public Logger(CancellationToken finishToken) : this(finishToken, null)
        { }

        public Logger(CancellationToken finishToken, string logFilePath)
        {
            this.finishToken = finishToken;
            loggingThread = new Thread(LogWriterThreadBody);
            loggingThread.Start();
            consoleThread = new Thread(ConsoleWriterThreadBody);
            consoleThread.Start();
            if (logToFile = !string.IsNullOrWhiteSpace(logFilePath))
            {
                this.logFilePath = logFilePath;
            }
        }

        private ManualResetEvent consoleLoggingResetEvent = new ManualResetEvent(true);

        public void LogLine(string msg, MessageGroup group)
        {
            LogTimedLine(msg, group, DateTime.Now);
        }

        public void WaitForAllConsoleMessages()
        {
            if (!consolePaused)
            {
                noMessagesInConsoleQueue.WaitOne();
            }
        }
        
        public void PauseConsoleLogging()
        {
            consolePaused = true;
            consoleLoggingResetEvent.Reset();
        }

        public void ResumeConsoleLogging()
        {
            consolePaused = false;
            consoleLoggingResetEvent.Set();
        }

        public void LogTimedLine(string msg, MessageGroup group, DateTime time)
        {
            string line = string.Format("{0}  {1}  {2}", time.ToString("HH:mm:ss"), $"[{group.ToString().ToUpper()}]".PadRight(11), msg);
            ConsoleColor color;
            switch (group)
            {
                case MessageGroup.Attack:
                case MessageGroup.Captcha:
                case MessageGroup.PixelFail:
                case MessageGroup.Error:
                    color = ConsoleColor.Red;
                    break;
                case MessageGroup.Assist:
                case MessageGroup.Pixel:
                    color = ConsoleColor.Green;
                    break;
                case MessageGroup.Info:
                    color = ConsoleColor.Magenta;
                    break;
                case MessageGroup.TechInfo:
                    color = ConsoleColor.Blue;
                    break;
                case MessageGroup.TechState:
                    color = ConsoleColor.Yellow;
                    break;
                default:
                case MessageGroup.PixelInfo:
                    color = ConsoleColor.DarkGray;
                    break;
            }
            messages.Enqueue((line, color));
            messagesAvailable.Set();
        }

        public void LogPixel(string msg, DateTime time, MessageGroup group, int x, int y, PixelColor color)
        {
            string text = $"{msg.PadRight(22)} {color.ToString().PadRight(13)} at ({x.ToString().PadLeft(6)};{y.ToString().PadLeft(6)})";
            LogTimedLine(text, group, time);
        }

        private void ConsoleWriterThreadBody()
        {
            try
            {
                while (true)
                {
                    consoleLoggingResetEvent.WaitOne();
                    if (consoleMessages.TryDequeue(out LogEntry msg))
                    {
                        (string line, ConsoleColor color) = msg;
                        Console.ForegroundColor = color;
                        Console.WriteLine(line);
                    }
                    else
                    {
                        if (disposed || finishToken.IsCancellationRequested)
                        {
                            return;
                        }
                        noMessagesInConsoleQueue.Set();
                        consoleMessagesAvailable.WaitOne();
                    }
                }
            }
            catch (ThreadInterruptedException)
            {
                return;
            }
        }

        private void LogWriterThreadBody()
        {
            try
            {
                while (true)
                {
                    if (messages.TryDequeue(out LogEntry msg))
                    {
                        consoleMessages.Enqueue(msg);
                        noMessagesInConsoleQueue.Reset();
                        consoleMessagesAvailable.Set();
                        if (logToFile)
                        {
                            using (StreamWriter writer = new StreamWriter(logFilePath, true))
                            {
                                writer.WriteLine(msg.Item1);
                            }
                        }
                    }
                    else
                    {
                        if (disposed || finishToken.IsCancellationRequested)
                        {
                            return;
                        }
                        messagesAvailable.WaitOne();
                    }
                }
            }
            catch (ThreadInterruptedException)
            {
                return;
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                messagesAvailable.Set();
                consoleMessagesAvailable.Set();
                consoleLoggingResetEvent.Set();
                Thread.Sleep(50);
                messagesAvailable.Dispose();
                consoleMessagesAvailable.Dispose();
                consoleLoggingResetEvent.Dispose();
                if (loggingThread.IsAlive)
                {
                    loggingThread.Interrupt();
                }
                if (consoleThread.IsAlive)
                {
                    consoleThread.Interrupt();
                }
            }
        }
    }
}
