using System;
using System.Windows.Forms;

namespace PS4PKGTool.Utilities
{
    /// <summary>
    /// Keeps high-frequency worker progress from flooding the WinForms message queue.
    /// Only the newest value is rendered on each UI timer tick.
    /// </summary>
    internal sealed class UiProgress<T> : IProgress<T>, IDisposable
    {
        private readonly object _sync = new();
        private readonly Action<T> _update;
        private readonly Timer _timer;
        private T _latest;
        private bool _hasValue;
        private bool _disposed;

        public UiProgress(Action<T> update, int intervalMilliseconds = 75)
        {
            _update = update ?? throw new ArgumentNullException(nameof(update));
            _timer = new Timer { Interval = Math.Max(16, intervalMilliseconds) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        public void Report(T value)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _latest = value;
                _hasValue = true;
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            T value;
            lock (_sync)
            {
                if (_disposed || !_hasValue) return;
                value = _latest;
                _hasValue = false;
            }
            _update(value);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _hasValue = false;
            }
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer.Dispose();
        }
    }
}
