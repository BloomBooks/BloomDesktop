using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.web;
using SIL.Code;
using SIL.IO;

namespace Bloom.TeamCollection
{
    // Must match the TeamCollectionStatus type in TeamCollectionStatus.ts
    public enum TeamCollectionStatus
    {
        Nominal, // all is well
        NewStuff, // New remote stuff to see after reload
        Error, // Errors you should check into sometime
        ClobberPending, // Your current work is about to be overwritten, drop everything!
        Disconnected, // It's a TC, but we can't connect right now, so most things are disabled.
        None, // The current collection is not a team collection.
    }

    public interface ITeamCollectionMessageLog
    {
        List<TeamCollectionMessage> Messages { get; }
        List<TeamCollectionMessage> CurrentErrors { get; }
        List<TeamCollectionMessage> ReloadMessages { get; }
        bool NextTeamCollectionDialogShouldForceReloadButton { get; set; }
        bool ShouldShowReloadButton { get; }
        List<TeamCollectionMessage> CurrentNewStuff { get; }
        TeamCollectionMessage CurrentClobberMessage { get; }
        DateTime LastReloadTime { get; }
        TeamCollectionStatus TeamCollectionStatus { get; }
        void WriteMessage(
            MessageAndMilestoneType messageType,
            string l10nId,
            string message,
            string param0 = "",
            string param1 = ""
        );
        void WriteMessage(TeamCollectionMessage message);
        void WriteMilestone(MessageAndMilestoneType milestoneType);
        void Flush();
        BloomWebSocketProgressEvent[] GetProgressMessages();
    }

    /// <summary>
    /// Stores a log of messages and milestones that form the local history of the collection.
    /// Deduces from these the current state of the collection.
    /// </summary>
    public class TeamCollectionMessageLog : ITeamCollectionMessageLog
    {
        private string _logFilePath;

        // Length of the log file at the time the TC was created, indicating the length of
        // old messages that LoadSavedMessages must prepend. Set to zero if that is called.
        private long _oldMessageLength;

        public TeamCollectionMessageLog(string logFilePath)
        {
            _logFilePath = logFilePath;
            if (RobustFile.Exists(_logFilePath))
            {
                _oldMessageLength = new FileInfo(_logFilePath).Length;
            }
        }

        // Review: currently includes milestones. Should it?
        private readonly List<TeamCollectionMessage> _messages = new List<TeamCollectionMessage>();

        /// <summary>
        /// A snapshot of the messages so far. Deliberately a copy: callers enumerate this from
        /// threads that are not the UI thread (teamCollection/getLog and
        /// teamCollection/logImportant are both registered with handleOnUiThread false), and
        /// enumerating the live list while another thread appends to it throws
        /// "Collection was modified". See BL-16729.
        /// </summary>
        public List<TeamCollectionMessage> Messages
        {
            get
            {
                lock (_messagesLock)
                {
                    return new List<TeamCollectionMessage>(_messages);
                }
            }
        }

        /// <summary>
        /// Guards Messages. Writers are not all on the UI thread: several API endpoints are
        /// registered with handleOnUiThread false and can end up here via CheckConnection, and
        /// (BL-16729) a file system watcher failing calls in from a thread-pool thread. Because
        /// WriteMessage enumerates the list to de-duplicate and then appends to it, while the
        /// status properties below enumerate the same list, an unguarded overlap could produce
        /// duplicate entries or throw InvalidOperationException.
        /// </summary>
        private readonly object _messagesLock = new object();

        /// <summary>
        /// Guards the log file. Deliberately NOT _messagesLock: appending is normally a matter
        /// of microseconds, but when something else has the file open (antivirus, a backup
        /// agent, the read-only collection folder of BL-16772) RobustFile retries over a period
        /// of seconds, and for all that time nothing may read Messages, TeamCollectionStatus and
        /// the rest -- TeamCollectionStatus is read on the UI thread whenever the Team
        /// Collection button refreshes.
        /// </summary>
        private readonly object _fileLock = new object();

        /// <summary>
        /// Lines waiting to be appended to the log file. Enqueued while _messagesLock is held,
        /// so their order is exactly the order of _messages, and dequeued only while _fileLock
        /// is held, so one thread writes at a time. Between them the file ends up in the same
        /// order as the in-memory list, without the file being touched under _messagesLock.
        /// </summary>
        private readonly ConcurrentQueue<string> _pendingWrites = new ConcurrentQueue<string>();

        /// <summary>
        /// Lines an append failed to write, kept in order and written ahead of everything else
        /// by the next attempt. Guarded by _fileLock.
        /// </summary>
        private List<string> _carryOver = new List<string>();

        // How hard an ordinary append tries. This often runs on the UI thread (every remote
        // change is handled from Application.Idle), so we must not stall it for seconds the way
        // RobustFile would: a couple of quick tries catches the usual transient sharing
        // violation, and anything still unwritten waits in _carryOver for the next message, or
        // for the stubborn Flush at shutdown.
        private const int kQuickAppendAttempts = 2;
        private const int kQuickAppendRetryMs = 50;

        // Only IOException is worth a second try: that is the transient case, something else
        // holding the file open for a moment. A permissions failure (the read-only collection
        // folder of BL-16772) will not get better while we wait, so let it out at once.
        private static readonly ISet<Type> kTransientAppendExceptions = new HashSet<Type>
        {
            typeof(IOException),
        };

        // A ceiling on _carryOver, so that a log file that stays unwritable for the rest of the
        // session cannot grow it without bound. Generous: the messages are short, and this is
        // only reached when something is badly wrong.
        private const int kMaxCarryOverChars = 64 * 1024;

        public List<TeamCollectionMessage> CurrentErrors
        {
            get
            {
                lock (_messagesLock)
                {
                    // correctly 0 if none match
                    var index =
                        _messages.FindLastIndex(m =>
                            m.MessageType == MessageAndMilestoneType.LogDisplayed
                            || m.MessageType == MessageAndMilestoneType.Reloaded
                        ) + 1;
                    return _messages
                        .Skip(index)
                        .Where(m =>
                            m.MessageType == MessageAndMilestoneType.Error
                            || m.MessageType == MessageAndMilestoneType.ErrorNoReload
                        )
                        .ToList();
                }
            }
        }

        /// <summary>
        /// Messages that should cause the Reload button to be present. That is,
        /// messages since the last Reload that are
        /// - NewStuff
        /// - Error (but not ErrorNoReload)
        /// </summary>
        /// <remarks>Note that unlike CurrentErrors or CurrentNewStuff, a LogShown does not prevent
        /// earlier messages being included. If we open the dialog and close it without reloading,
        /// we want the button to stop indicating a new problem, but we don't want the user to
        /// lose the ability to reload until he actually reloads.</remarks>
        public List<TeamCollectionMessage> ReloadMessages
        {
            get
            {
                lock (_messagesLock)
                {
                    // correctly 0 if none match
                    var index =
                        _messages.FindLastIndex(m =>
                            m.MessageType == MessageAndMilestoneType.Reloaded
                        ) + 1;
                    return _messages
                        .Skip(index)
                        .Where(m =>
                            m.MessageType == MessageAndMilestoneType.Error
                            || m.MessageType == MessageAndMilestoneType.NewStuff
                        )
                        .ToList();
                }
            }
        }

        public bool NextTeamCollectionDialogShouldForceReloadButton { get; set; }

        /// <summary>
        /// True if there is any messages for which reloading the collection is a useful action.
        /// </summary>
        public bool ShouldShowReloadButton =>
            NextTeamCollectionDialogShouldForceReloadButton || ReloadMessages.Count > 0;

        public List<TeamCollectionMessage> CurrentNewStuff
        {
            get
            {
                lock (_messagesLock)
                {
                    // correctly 0 if none match
                    var index =
                        _messages.FindLastIndex(m =>
                            m.MessageType == MessageAndMilestoneType.Reloaded
                        ) + 1;
                    return _messages
                        .Skip(index)
                        .Where(m => m.MessageType == MessageAndMilestoneType.NewStuff)
                        .ToList();
                }
            }
        }

        public TeamCollectionMessage CurrentClobberMessage
        {
            get
            {
                lock (_messagesLock)
                {
                    var last = _messages.FindLast(m =>
                        m.MessageType == MessageAndMilestoneType.ClobberPending
                        || m.MessageType == MessageAndMilestoneType.ShowedClobbered
                        || m.MessageType == MessageAndMilestoneType.Reloaded
                    );
                    return last?.MessageType == MessageAndMilestoneType.ClobberPending
                        ? last
                        : null;
                }
            }
        }

        public DateTime LastReloadTime
        {
            get
            {
                lock (_messagesLock)
                {
                    var last = _messages.FindLast(m =>
                        m.MessageType == MessageAndMilestoneType.Reloaded
                    );
                    return last == null ? DateTime.MinValue : last.When;
                }
            }
        }

        public TeamCollectionStatus TeamCollectionStatus
        {
            get
            {
                if (CurrentClobberMessage != null)
                    return TeamCollectionStatus.ClobberPending;
                if (CurrentErrors.Count > 0)
                    return TeamCollectionStatus.Error;
                if (CurrentNewStuff.Count > 0)
                    return TeamCollectionStatus.NewStuff;
                return TeamCollectionStatus.Nominal;
            }
        }

        public void WriteMessage(
            MessageAndMilestoneType messageType,
            string l10nId,
            string message,
            string param0 = "",
            string param1 = ""
        )
        {
            var msg = new TeamCollectionMessage(messageType, l10nId, message, param0, param1);
            // The de-duplication check and the append have to be one atomic step, or two
            // concurrent writers can both decide the message is new and both add it.
            lock (_messagesLock)
            {
                if (IsRedundantMessage(messageType, l10nId, message, param0, param1))
                    return;
                _messages.Add(msg);
                QueueForPersisting(msg);
            }
            DrainPendingWrites();
            AfterMessageAdded(msg);
        }

        public void WriteMessage(TeamCollectionMessage message)
        {
            lock (_messagesLock)
            {
                _messages.Add(message);
                QueueForPersisting(message);
            }
            DrainPendingWrites();
            AfterMessageAdded(message);
        }

        /// <summary>
        /// Line up one message to be appended to the log file. Called with _messagesLock held,
        /// which is what makes the queue's order the same as the in-memory list's.
        /// </summary>
        private void QueueForPersisting(TeamCollectionMessage message)
        {
            // Using Environment.NewLine here means the format of the file will be appropriate for the
            // computer we are running on. It's possible a shared collection might be used by both
            // Linux and Windows. But that's OK, because .NET line reading accepts either line
            // break on either platform.
            _pendingWrites.Enqueue(message.ToPersistedForm + Environment.NewLine);
        }

        /// <summary>
        /// Append everything queued so far to the log file, giving up quickly if the file is
        /// busy. Deliberately called with _messagesLock released, so a slow append cannot block
        /// the readers; _fileLock instead keeps two threads from colliding over the file, and
        /// because the queue is FIFO and is only ever drained under that lock, the file stays in
        /// the same order as _messages.
        /// </summary>
        /// <remarks>
        /// Nothing can be left behind unwritten: whoever queues a message goes on to call this,
        /// and waits for _fileLock rather than giving up, so its line is written either by this
        /// call or by the drain that is already running. Finding nothing to write, which is what
        /// happens when that other drain took our line, is therefore a normal outcome.
        /// </remarks>
        private void DrainPendingWrites()
        {
            WritePendingMessages(beStubborn: false);
        }

        /// <summary>
        /// Write anything still waiting, trying as hard as RobustFile does. Ordinary appends give
        /// up quickly so as not to stall the thread writing the message -- usually the UI thread
        /// -- and leave what they could not write for the next message to carry out. At shutdown
        /// there is no next message, so this is those lines' last chance to reach the file, and
        /// here it is worth waiting out whatever has the file open.
        /// </summary>
        public void Flush()
        {
            WritePendingMessages(beStubborn: true);
        }

        private void WritePendingMessages(bool beStubborn)
        {
            lock (_fileLock)
            {
                // Whatever an earlier attempt could not write goes first, so that the file stays
                // in the order of the in-memory list. A burst of messages (SyncAtStartup
                // produces one) thus costs a single append rather than one per message.
                var lines = _carryOver;
                _carryOver = new List<string>();
                while (_pendingWrites.TryDequeue(out var line))
                    lines.Add(line);
                if (lines.Count == 0)
                    return;
                if (TryAppend(string.Concat(lines), beStubborn))
                    return;
                // The messages are already in Messages, so the current session still shows them,
                // and AfterMessageAdded writes them to the ordinary log as well; the worst case
                // is that they don't survive a restart. Keep them for the next attempt.
                _carryOver = lines;
                TrimCarryOverIfTooBig();
            }
        }

        /// <summary>
        /// Called with _fileLock held.
        /// </summary>
        /// <returns>true if the text reached the file.</returns>
        private bool TryAppend(string text, bool beStubborn)
        {
            try
            {
                if (beStubborn)
                {
                    RobustFile.AppendAllText(_logFilePath, text);
                }
                else
                {
                    // What RobustFile.AppendAllText does -- the same call, and so the same
                    // encoding (UTF-8, no BOM), so the two paths can append to one file
                    // interchangeably -- but over milliseconds rather than seconds.
                    RetryUtility.Retry(
                        () => File.AppendAllText(_logFilePath, text),
                        kQuickAppendAttempts,
                        kQuickAppendRetryMs,
                        kTransientAppendExceptions,
                        memo: $"AppendAllText {_logFilePath}"
                    );
                }
                return true;
            }
            catch (Exception ex)
            {
                // Not being able to write must not take Bloom down: this path is used while
                // reporting a TC initialization failure, and when the underlying problem is an
                // unwritable collection folder (e.g. read-only files, BL-16772), throwing here
                // turned a degraded-but-working Team Collection into a collection that could not
                // open at all.
                SIL.Reporting.Logger.WriteError(
                    $"Could not persist Team Collection messages to {_logFilePath}",
                    ex
                );
                return false;
            }
        }

        /// <summary>
        /// Called with _fileLock held. Drops the oldest unwritten lines if they have piled up,
        /// keeping the most recent, which are the ones most likely to explain what went wrong.
        /// </summary>
        private void TrimCarryOverIfTooBig()
        {
            var total = _carryOver.Sum(line => line.Length);
            if (total <= kMaxCarryOverChars)
                return;
            var toDrop = 0;
            while (toDrop < _carryOver.Count && total > kMaxCarryOverChars)
            {
                total -= _carryOver[toDrop].Length;
                toDrop++;
            }
            _carryOver.RemoveRange(0, toDrop);
            SIL.Reporting.Logger.WriteEvent(
                $"Gave up on {toDrop} Team Collection messages that could not be written to {_logFilePath}"
            );
        }

        /// <summary>
        /// Deliberately called with the lock released: raising the status-changed event reaches
        /// WinForms and the websocket server, and holding a lock across that is how deadlocks
        /// happen. Everything here reads only the message it was handed.
        /// </summary>
        private void AfterMessageAdded(TeamCollectionMessage message)
        {
            SIL.Reporting.Logger.WriteEvent(message.TextForDisplay);
            TeamCollectionManager.RaiseTeamCollectionStatusChanged();
        }

        private bool MatchParams(string p1, string p2)
        {
            if (string.IsNullOrEmpty(p1) && string.IsNullOrEmpty(p2))
                return true;
            return p1 == p2;
        }

        private bool IsRedundantMessage(
            MessageAndMilestoneType messageType,
            string l10nId,
            string message,
            string param0,
            string param1
        )
        {
            if (messageType == MessageAndMilestoneType.NewStuff)
            {
                return CurrentNewStuff.Any(
                    (msg) =>
                        msg.MessageType == messageType
                        && msg.L10NId == l10nId
                        && MatchParams(msg.Param0, param0)
                        && MatchParams(msg.Param1, param1)
                );
            }

            if (
                messageType == MessageAndMilestoneType.Error
                || messageType == MessageAndMilestoneType.ErrorNoReload
            )
            {
                // At some point, if we're loading the whole history of messages, this might want to consider whether
                // the message is redundant with a current session report. But currently we reset completely for each
                // session, and problems (particularly the one produced by a bad zip file in the repo) tend to be very
                // frequent. We need to look at everything to weed out duplicates.
                return _messages.Any(msg =>
                    (
                        msg.MessageType == MessageAndMilestoneType.Error
                        || msg.MessageType == MessageAndMilestoneType.ErrorNoReload
                    )
                    && msg.L10NId == l10nId
                    && msg.RawEnglishMessageTemplate == message
                    && MatchParams(msg.Param0, param0)
                    && MatchParams(msg.Param1, param1)
                );
            }
            return false;
        }

        public void WriteMilestone(MessageAndMilestoneType milestoneType)
        {
            WriteMessage(milestoneType, null, null, null, null);
        }

        /// <summary>
        /// Call this (as many times as you like) if you want to see a complete history of
        /// messages we have saved in the Messages list. Otherwise, it only shows ones since
        /// the TCML was created (which is usually enough since opening a collection
        /// is accompanied by a Reload, which logs a Reloaded milestone, which means all the
        /// current lists only go back that far anyway.)
        /// </summary>
        /// We're not currently using this because we think the dialog is not yet up to
        /// displaying a list as long as this might get.
        /// If we reinstate, may need to make it more robust, possibly by discarding the
        /// messages we have in memory and read the whole file (if we haven't already).
        //public void LoadSavedMessages()
        //{
        //	if (!RobustFile.Exists(_logFilePath) || _oldMessageLength == 0)
        //		return;
        //	// There ought to be some way to read the file a line at a time without loading it all into one
        //	// big buffer and still to know when we get to _oldMessageLength, but it's not easy.
        //	// Note that we can't count on adding up the length of the lines, because utf-8 is a variable
        //	// length encoding. We could convert each line back to UTF-8, but that feels fragile
        //	// (e.g., what if something is badly encoded?). We could try to work with the position of
        //	// the stream inside the streamReader, but the streamReader may buffer it. StreamReader
        //	// does not expose its own position. The only option I see so far would be to read the file
        //	// a byte at a time and do our own processing into lines. We can switch to that if it
        //	// becomes necessary.
        //	var bytes = new byte[_oldMessageLength];
        //	using (var stream = ToPalaso.RobustIO.GetFileStream(_logFilePath, FileMode.Open, FileAccess.Read))
        //	{
        //		// We better not be getting over 2G of log!
        //		stream.Read(bytes, 0, (int)_oldMessageLength);
        //	}
        //	var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);
        //	var messages = new List<TeamCollectionMessage>();
        //	string line;
        //	while ((line = reader.ReadLine()) != null)
        //	{
        //		var msg = TeamCollectionMessage.FromPersistedForm(line);
        //		if (msg != null)
        //			messages.Add(msg);
        //	}

        //	Messages.InsertRange(0, messages);
        //	// In case this is called again, we don't have any old messages still unloaded.
        //	_oldMessageLength = 0;
        //}

        const string kWebSocketContext = "unused"; // this is just all preloaded, doesn't use websocket at this point

        public BloomWebSocketProgressEvent[] GetProgressMessages()
        {
            var messages = Messages
                .Where(m => !string.IsNullOrEmpty(m.RawEnglishMessageTemplate))
                .Select(ProgressMessageFromTeamCollectionLogEntry)
                .ToArray();

            if (messages.Length == 0)
            {
                return new[]
                {
                    new BloomWebSocketProgressEvent(
                        kWebSocketContext,
                        ProgressKind.Progress,
                        "No new activity."
                    ),
                };
            }

            return messages;
        }

        // TeamCollection has its own set of message types. Here we convert them to standard
        // progress messages so we can reuse existing UI controls.
        private BloomWebSocketProgressEvent ProgressMessageFromTeamCollectionLogEntry(
            TeamCollectionMessage entry
        )
        {
            switch (entry.MessageType)
            {
                case MessageAndMilestoneType.Error:
                case MessageAndMilestoneType.ErrorNoReload:
                case MessageAndMilestoneType.ClobberPending:
                    return new BloomWebSocketProgressEvent(
                        kWebSocketContext,
                        ProgressKind.Error,
                        entry.TextForDisplay
                    );
                default:
                    return new BloomWebSocketProgressEvent(
                        kWebSocketContext,
                        ProgressKind.Progress,
                        entry.TextForDisplay
                    );
            }
        }
    }
}

// Todo (in other cards now)
// - clobber-pending stuff: detect, toast, reload with dialog.
//		(Review: do we even need clobber-pending in the log now that it's not a possible state of the status button?)
// - actual icons (and restore TC label)...later task
