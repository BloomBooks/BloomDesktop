using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SIL.Progress;

namespace Bloom.web
{
    /// <summary>
    /// Class that allows code expecting an SIL.Progress.IProgress object to use an IWebSocketProgress instead.
    /// </summary>
    public class WebProgressAdapter : IProgress
    {
        /// <summary>
        /// Forwards the percent-done that IProgress-based code sets on its indicator to the web
        /// progress as a "percent" event, which the React ProgressDialog shows when it was opened
        /// as determinate (see BrowserProgressDialog.DoWorkWithProgressDialogAsync). A dialog
        /// that was not opened that way ignores the event, so it is harmless to send.
        /// </summary>
        private class WebProgressIndicator : IProgressIndicator
        {
            private readonly IWebSocketProgress _webProgress;

            public WebProgressIndicator(IWebSocketProgress webProgress)
            {
                _webProgress = webProgress;
            }

            int _percent;
            public int PercentCompleted
            {
                get { return _percent; }
                set
                {
                    _percent = value;
                    _webProgress?.SendPercent(value);
                }
            }
            public SynchronizationContext SyncContext
            {
                get { return null; }
                set { return; }
            }

            public void Finish() { }

            public void IndicateUnknownProgress() { }

            public void Initialize() { }
        }

        private readonly IWebSocketProgress _webProgress;

        public WebProgressAdapter(IWebSocketProgress progress)
        {
            _webProgress = progress;
            _indicator = new WebProgressIndicator(progress);
        }

        private bool _showVerbose;
        public bool ShowVerbose
        {
            set { _showVerbose = value; }
        }

        public bool CancelRequested { get; set; }
        public bool ErrorEncountered
        {
            get { return false; }
            set { return; }
        }

        private IProgressIndicator _indicator;
        public IProgressIndicator ProgressIndicator
        {
            get { return _indicator; }
            set { _indicator = value; }
        }

        public SynchronizationContext SyncContext
        {
            get { return null; }
            set { return; }
        }

        private List<string> _filters = new List<string>();

        public void AddFilter(string filter)
        {
            _filters.Add(filter);
        }

        private bool ShowMessage(string message)
        {
            return _filters.Count == 0 || _filters.Any(x => message.StartsWith(x));
        }

        public void WriteError(string message, params object[] args)
        {
            var msg = string.Format(message, args);
            if (ShowMessage(msg))
                _webProgress?.MessageWithoutLocalizing(msg, ProgressKind.Error);
        }

        public void WriteException(Exception error)
        {
            _webProgress?.MessageWithoutLocalizing(error.Message, ProgressKind.Error);
        }

        public void WriteMessage(string message, params object[] args)
        {
            var msg = string.Format(message, args);
            if (ShowMessage(msg))
                _webProgress?.MessageWithoutLocalizing(msg, ProgressKind.Note);
        }

        public void WriteMessageWithColor(string colorName, string message, params object[] args)
        {
            var msg = string.Format(message, args);
            if (ShowMessage(msg))
                _webProgress?.MessageWithoutLocalizing(msg, ProgressKind.Note);
        }

        public void WriteStatus(string message, params object[] args)
        {
            var msg = string.Format(message, args);
            if (ShowMessage(msg))
                _webProgress?.MessageWithoutLocalizing(msg, ProgressKind.Progress);
        }

        public void WriteVerbose(string message, params object[] args)
        {
            if (!_showVerbose)
                return;
            var msg = string.Format(message, args);
            if (ShowMessage(msg))
                _webProgress?.MessageWithoutLocalizing(msg, ProgressKind.Instruction);
        }

        public void WriteWarning(string message, params object[] args)
        {
            var msg = string.Format(message, args);
            if (ShowMessage(msg))
                _webProgress?.MessageWithoutLocalizing(msg, ProgressKind.Warning);
        }
    }
}
