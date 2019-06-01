using System;
using System.Threading;

namespace PixelPlanetBot
{
    class Gate : IDisposable
    {
        private AutoResetEvent opened;
        private AutoResetEvent closed;
        private bool disposed = false;
        private bool isOpen;
        private readonly object lockObject = new object();
        private readonly Thread workingThread;

        public Gate(bool isOpen = false)
        {
            this.isOpen = isOpen;
            opened = new AutoResetEvent(isOpen);
            closed = new AutoResetEvent(false);
            workingThread = Program.StartBackgroundThread(ThreadBody);
        }

        public void WaitOpened()
        {
            if (!disposed)
            {
                lock (lockObject)
                { }
            }
        }

        public bool IsOpen
        {
            get => isOpen;
            set
            {
                if (!disposed)
                {
                    if (value && !isOpen)
                    {
                        opened.Set();
                        isOpen = true;
                    }
                    else if (!value && isOpen)
                    {
                        closed.Set();
                        isOpen = false;
                    }
                }
            }
        }

        public void Open()
        {
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        private void ThreadBody()
        {
            try
            {
                while (!disposed)
                {
                    lock (lockObject)
                    {
                        opened.WaitOne();
                    }
                    if (disposed)
                    {
                        return;
                    }
                    closed.WaitOne();
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
                IsOpen = true;
                opened.Dispose();
                closed.Dispose();
                Thread.Sleep(50);
                if (workingThread.IsAlive)
                {
                    workingThread.Interrupt();
                }
            }
        }
    }
}
