using System;
using System.Diagnostics;
using Rhino;
using Rhino.UI;

namespace Sanderling.Scene
{
    /// <summary>
    /// Progress and cancellation while navigation geometry is being built.
    ///
    /// Building runs on the UI thread, so Report pumps Windows messages a few times per second: the status bar
    /// progress meter repaints, Rhino stays responsive and Esc can be detected. The meter only appears when the
    /// build takes longer than ShowDelay, so the usual quick incremental update does not flash it.
    /// </summary>
    internal sealed class BuildProgress : IDisposable
    {
        private const double ShowDelay = 0.3;     // seconds before the progress meter appears
        private const double PumpInterval = 0.1;  // seconds between message pumps
        private const double NotifyInterval = 0.5; // seconds between percentChanged calls (the HUD redraws the whole view)

        private readonly string _label;
        private readonly Func<bool> _cancelRequested;
        private readonly Action<int> _percentChanged;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastPump;
        private double _lastNotify = double.NegativeInfinity;
        private int _lastPercent = -1;
        private bool _meterShown;

        /// <param name="label">Text shown next to the status bar progress meter</param>
        /// <param name="cancelRequested">Polled after every message pump; returning true cancels the build</param>
        /// <param name="percentChanged">Optional, called after a message pump when the percentage changed (e.g. to update the HUD)</param>
        public BuildProgress(string label, Func<bool> cancelRequested, Action<int> percentChanged)
        {
            _label = label;
            _cancelRequested = cancelRequested;
            _percentChanged = percentChanged;
        }

        /// <summary>Whether the build was cancelled.</summary>
        public bool Cancelled { get; private set; }

        /// <summary>
        /// Reports that done of total objects have been built.
        /// </summary>
        /// <returns>false if the build should stop.</returns>
        public bool Report(int done, int total)
        {
            if (Cancelled) return false;

            double now = _clock.Elapsed.TotalSeconds;
            if (now - _lastPump < PumpInterval) return true;
            _lastPump = now;

            int percent = total > 0 ? (int)(100L * done / total) : 100;
            if (now >= ShowDelay && percent != _lastPercent)
            {
                _lastPercent = percent;
                if (!_meterShown)
                {
                    StatusBar.ShowProgressMeter(0, 100, _label, true, true);
                    _meterShown = true;
                }
                StatusBar.UpdateProgressMeter(percent, true);
                if (_percentChanged != null && now - _lastNotify >= NotifyInterval)
                {
                    _lastNotify = now;
                    _percentChanged(percent);
                }
            }

            RhinoApp.Wait();

            if (_cancelRequested != null && _cancelRequested())
            {
                Cancelled = true;
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (_meterShown)
            {
                StatusBar.HideProgressMeter();
                _meterShown = false;
            }
        }
    }
}
