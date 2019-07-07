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
        private ConcurrentQueue<LogEntry> incomingConsoleMessages = new ConcurrentQueue<LogEntry>();
        private ConcurrentQueue<LogEntry> printableConsoleMessages;
        private readonly AutoResetEvent messagesAvailable = new AutoResetEvent(false);
        private readonly AutoResetEvent consoleMessagesAvailable = new AutoResetEvent(false);
        private readonly AutoResetEvent noPrintableMessages = new AutoResetEvent(true);
        private readonly CancellationToken finishToken;
        private readonly string logFilePath;
        private readonly bool logToFile;
        private readonly object lockObj = new object();
        bool disposed;
        bool consolePaused = false;
        private Thread loggingThread, consoleThread;

        public Logger(CancellationToken finishToken) : this(finishToken, null)
        { }

        public Logger(CancellationToken finishToken, string logFilePath)
        {
            printableConsoleMessages = incomingConsoleMessages;
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

        public void LogLine(string msg, MessageGroup group)
        {
            LogTimedLine(msg, group, DateTime.Now);
        }

        public void LogAndPause(string msg, MessageGroup group)
        {
            if (!consolePaused)
            {
                string text;
                lock (lockObj)
                {
                    text = FormatLine(msg, group, DateTime.Now);
                    consolePaused = true;
                    incomingConsoleMessages = new ConcurrentQueue<LogEntry>();
                    if (logToFile)
                    {
                        using (StreamWriter writer = new StreamWriter(logFilePath, true))
                        {
                            writer.WriteLine(text);
                        }
                    }
                }
                noPrintableMessages.Reset();
                printableConsoleMessages.Enqueue((text, ColorOf(group)));
                consoleMessagesAvailable.Set();
                noPrintableMessages.WaitOne();
            }
            else
            {
                throw new InvalidOperationException("Already paused");
            }
        }

        public void ResumeLogging()
        {
            if (consolePaused)
            {
                lock (lockObj)
                {
                    consolePaused = false;
                    printableConsoleMessages = incomingConsoleMessages;
                }
                consoleMessagesAvailable.Set();
            }
        }

        private static ConsoleColor ColorOf(MessageGroup group)
        {
            switch (group)
            {
                case MessageGroup.Attack:
                case MessageGroup.Captcha:
                case MessageGroup.PixelFail:
                case MessageGroup.Error:
                    return ConsoleColor.Red;
                case MessageGroup.Assist:
                case MessageGroup.Pixel:
                    return ConsoleColor.Green;
                case MessageGroup.Info:
                case MessageGroup.ImageDone:
                    return ConsoleColor.Magenta;
                case MessageGroup.TechInfo:
                    return ConsoleColor.Blue;
                case MessageGroup.TechState:
                    return ConsoleColor.Yellow;
                case MessageGroup.PixelInfo:
                default:
                    return ConsoleColor.DarkGray;
            }
        }

        private static string FormatLine(string msg, MessageGroup group, DateTime time)
        {
            return string.Format("{0}  {1}  {2}", time.ToString("HH:mm:ss"), $"[{group.ToString().ToUpper()}]".PadRight(11), msg);
        }

        public void LogTimedLine(string msg, MessageGroup group, DateTime time)
        {
            string line = FormatLine(msg, group, time);
            messages.Enqueue((line, ColorOf(group)));
            messagesAvailable.Set();
        }

        public void LogPixel(string msg, DateTime time, MessageGroup group, int x, int y, PixelColor color)
        {
            string text = $"{msg.PadRight(22)} {color.ToString().PadRight(13)} at ({x.ToString().PadLeft(6)};{y.ToString().PadLeft(6)})";
            LogTimedLine(text, group, time);
        }

        private void ConsoleWriterThreadBody()
        {
            while (true)
            {
                if (printableConsoleMessages.TryDequeue(out LogEntry msg))
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
                    noPrintableMessages.Set();
                    consoleMessagesAvailable.WaitOne();
                }
            }
        }

        private void LogWriterThreadBody()
        {
            while (true)
            {
                if (messages.TryDequeue(out LogEntry msg))
                {
                    lock (lockObj)
                    {
                        incomingConsoleMessages.Enqueue(msg);
                        consoleMessagesAvailable.Set();
                        if (logToFile)
                        {
                            using (StreamWriter writer = new StreamWriter(logFilePath, true))
                            {
                                writer.WriteLine(msg.Item1);
                            }
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
    
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                messagesAvailable.Set();
                consoleMessagesAvailable.Set();
                noPrintableMessages.Set();
                Thread.Sleep(50);
                messagesAvailable.Dispose();
                consoleMessagesAvailable.Dispose();
                noPrintableMessages.Dispose();
            }
        }
    }
}
