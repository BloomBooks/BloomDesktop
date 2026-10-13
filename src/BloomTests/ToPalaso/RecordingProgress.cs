using System;
using System.Collections.Generic;
using System.Threading;
using SIL.Progress;

namespace BloomTests.ToPalaso
{
    /// <summary>
    /// An IProgress that remembers every call made on it, so a test can assert on exactly what a
    /// piece of code reported: which status lines, messages, warnings and errors, and which
    /// percentages it set on the indicator.
    /// </summary>
    public class RecordingProgress : IProgress
    {
        public readonly List<string> Statuses = new List<string>();
        public readonly List<string> Messages = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Verbose = new List<string>();
        public readonly List<Exception> Exceptions = new List<Exception>();
        public readonly RecordingIndicator Indicator = new RecordingIndicator();
        public bool ShowVerboseWasSet;

        public class RecordingIndicator : IProgressIndicator
        {
            public readonly List<int> Percents = new List<int>();
            private int _percent;
            public int PercentCompleted
            {
                get { return _percent; }
                set
                {
                    _percent = value;
                    Percents.Add(value);
                }
            }
            public SynchronizationContext SyncContext { get; set; }

            public void Finish() { }

            public void IndicateUnknownProgress() { }

            public void Initialize() { }
        }

        public void WriteStatus(string message, params object[] args) =>
            Statuses.Add(string.Format(message, args));

        public void WriteMessage(string message, params object[] args) =>
            Messages.Add(string.Format(message, args));

        public void WriteMessageWithColor(string colorName, string message, params object[] args) =>
            Messages.Add(string.Format(message, args));

        public void WriteWarning(string message, params object[] args) =>
            Warnings.Add(string.Format(message, args));

        public void WriteException(Exception error) => Exceptions.Add(error);

        public void WriteError(string message, params object[] args) =>
            Errors.Add(string.Format(message, args));

        public void WriteVerbose(string message, params object[] args) =>
            Verbose.Add(string.Format(message, args));

        public bool ShowVerbose
        {
            set { ShowVerboseWasSet = true; }
        }

        public bool CancelRequested { get; set; }
        public bool ErrorEncountered { get; set; }

        public IProgressIndicator ProgressIndicator
        {
            get { return Indicator; }
            set { throw new NotSupportedException(); }
        }

        public SynchronizationContext SyncContext { get; set; }
    }
}
