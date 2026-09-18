using System;
using System.Threading;
using SIL.Progress;

namespace Bloom.ToPalaso
{
    /// <summary>
    /// An IProgress that passes everything through to another one except WriteStatus, which it
    /// drops. Use it around a loop that reports a status line per item (one per image, one per
    /// page...) while also driving the percent bar. Status was designed for a single overwriting
    /// label, where one line per item is a useful "what am I doing now"; the React progress dialog
    /// (WebProgressAdapter) has no such label and appends every status as a permanent log line, so
    /// the same loop there produces dozens of near-identical lines that only duplicate the bar
    /// (BL-16893). Messages, warnings and errors still get through, and so does the percent.
    /// </summary>
    public class QuietStatusProgress : IProgress
    {
        private readonly IProgress _inner;

        public QuietStatusProgress(IProgress inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void WriteStatus(string message, params object[] args)
        {
            // Deliberately nothing: the percent bar is the progress report for the loop this wraps.
        }

        public void WriteMessage(string message, params object[] args) =>
            _inner.WriteMessage(message, args);

        public void WriteMessageWithColor(string colorName, string message, params object[] args) =>
            _inner.WriteMessageWithColor(colorName, message, args);

        public void WriteWarning(string message, params object[] args) =>
            _inner.WriteWarning(message, args);

        public void WriteException(Exception error) => _inner.WriteException(error);

        public void WriteError(string message, params object[] args) =>
            _inner.WriteError(message, args);

        public void WriteVerbose(string message, params object[] args) =>
            _inner.WriteVerbose(message, args);

        public bool ShowVerbose
        {
            set { _inner.ShowVerbose = value; }
        }

        public bool CancelRequested
        {
            get { return _inner.CancelRequested; }
            set { _inner.CancelRequested = value; }
        }

        public bool ErrorEncountered
        {
            get { return _inner.ErrorEncountered; }
            set { _inner.ErrorEncountered = value; }
        }

        public IProgressIndicator ProgressIndicator
        {
            get { return _inner.ProgressIndicator; }
            set { _inner.ProgressIndicator = value; }
        }

        public SynchronizationContext SyncContext
        {
            get { return _inner.SyncContext; }
            set { _inner.SyncContext = value; }
        }
    }
}
