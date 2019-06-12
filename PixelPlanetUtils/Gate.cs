using System;
using System.Threading;

namespace PixelPlanetUtils
{
    public class Gate : IDisposable
    {
        private AutoResetEvent opened;
        private AutoResetEvent closed;
        private bool isOpen;
        private readonly object lockObject = new object();
        private Thread workingThread;

        public Gate(bool isOpen = false)
        {
            this.isOpen = isOpen;
            opened = new AutoResetEvent(isOpen);
            closed = new AutoResetEvent(false);
            workingThread = new Thread(ThreadBody);
            workingThread.Start();
        }

        public void WaitOpened()
        {
            if (!IsDisposed)
            {
                lock (lockObject)
                { }
            }
        }

        public bool IsDisposed { get; private set; } = false;

        public bool IsOpen
        {
            get => isOpen;
            set
            {
                if (!IsDisposed)
                {
                    if (value)
                    {
                        Open();
                    }
                    else
                    {
                        Close();
                    }
                }
            }
        }

        public void Open()
        {
            if (!IsDisposed && !isOpen)
            {
                opened.Set();
                isOpen = true;
            }
        }

        public void Close()
        {
            if (!IsDisposed && isOpen)
            {
                closed.Set();
                isOpen = false;
            }
        }

        private void ThreadBody()
        {
            try
            {
                while (!IsDisposed)
                {
                    lock (lockObject)
                    {
                        opened.WaitOne();
                    }
                    if (IsDisposed)
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
            if (!IsDisposed)
            {
                IsDisposed = true;
                opened.Set();
                closed.Set();
                opened.Dispose();
                closed.Dispose();
                if (workingThread.IsAlive)
                {
                    workingThread.Interrupt();
                }
            }
        }
    }
}
