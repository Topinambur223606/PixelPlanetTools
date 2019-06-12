using System;
using System.Threading;

namespace PixelPlanetUtils
{
    public class Gate : IDisposable
    {
        private readonly AutoResetEvent autoResetEvent;
        private bool isOpen;

        public bool IsDisposed { get; private set; } = false;

        public Gate(bool isOpen = false)
        {
            this.isOpen = isOpen;
            autoResetEvent = new AutoResetEvent(isOpen);
        }

        public bool IsOpen
        {
            get => isOpen;
            set
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

        public void WaitOpened()
        {
            if (!IsDisposed)
            {
                autoResetEvent.WaitOne();
                if (!IsDisposed && isOpen)
                {
                    autoResetEvent.Set();
                }
            }
        }

        public void Close()
        {
            if (!IsDisposed)
            {
                autoResetEvent.Reset();
                isOpen = false;
            }
        }

        public void Open()
        {
            if (!IsDisposed)
            {
                autoResetEvent.Set();
                isOpen = true;
            }
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                Open();
                IsDisposed = true;
                autoResetEvent.Dispose();
            }
        }
    }
}
