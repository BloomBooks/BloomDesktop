using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NDesk.DBus;
using SIL.Code;
using SIL.IO;

namespace Bloom.TeamCollection
{
	public enum TeamCollectionStatus
	{
		Nominal, // all is well
		NewStuff, // New remote stuff to see after reload
		Error, // Errors you should check into sometime
		ClobberPending, // Your current work is about to be overwritten, drop everything!
		Disconnected, // It's a TC, but we can't connect right now, so most things are disabled.
		None // The current collection is not a team collection.
	}

	/// <summary>
	/// Stores a log of messages and milestones that form the local history of the collection.
	/// Deduces from these the current state of the collection.
	/// </summary>
	public class TeamCollectionMessageLog
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
			Messages = new List<TeamCollectionMessage>();
		}

		// Review: currently includes milestones. Should it?
		public List<TeamCollectionMessage> Messages { get; private set; }

		public List<TeamCollectionMessage> CurrentErrors
		{
			get
			{
				// correctly 0 if none match
				var index = Messages.FindLastIndex(m =>
					m.MessageType == MessageAndMilestoneType.LogDisplayed ||
					m.MessageType == MessageAndMilestoneType.Reloaded) + 1;
				return Messages.Skip(index).Where(m => m.MessageType == MessageAndMilestoneType.Error).ToList();
			}
		}

		public List<TeamCollectionMessage> CurrentNewStuff
		{
			get
			{
				// correctly 0 if none match
				var index = Messages.FindLastIndex(m =>
					m.MessageType == MessageAndMilestoneType.Reloaded) + 1;
				return Messages.Skip(index).Where(m => m.MessageType == MessageAndMilestoneType.NewStuff).ToList();
			}
		}

		public TeamCollectionMessage CurrentClobberMessage
		{
			get
			{
				var last = Messages.FindLast(m =>
					m.MessageType == MessageAndMilestoneType.ClobberPending ||
					m.MessageType == MessageAndMilestoneType.ShowedClobbered ||
					m.MessageType == MessageAndMilestoneType.Reloaded
				);
				return last?.MessageType == MessageAndMilestoneType.ClobberPending ? last : null;
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

		public void WriteMessage(MessageAndMilestoneType messageType, string l10nId, string message, string param0, string param1)
		{
			if (IsRedundantMessage(messageType, l10nId, param0, param1))
				return;
			var msg = new TeamCollectionMessage();
			msg.When = DateTime.UtcNow;
			msg.MessageType = messageType;
			msg.L10NId = l10nId;
			msg.Message = message;
			msg.Param0 = param0;
			msg.Param1 = param1;
			Messages.Add(msg);
			TeamCollectionManager.RaiseTeamCollectionStatusChanged();
			// Using Environment.NewLine here means the format of the file will be appropriate for the
			// computer we are running on. It's possible a shared collection might be used by both
			// Linux and Windows. But that's OK, because .NET line reading accepts either line
			// break on either platform.
			var toPersist = msg.ToPersistedForm + Environment.NewLine;
			// There ought to be a RobustFile.AppendAllText, but there isn't.
			// However, as this promises to close the file each call, it should be pretty reliable.
			RetryUtility.Retry(() => File.AppendAllText(_logFilePath, toPersist));
		}

		private bool IsRedundantMessage(MessageAndMilestoneType messageType, string l10nId, string param0, string param1)
		{
			if (messageType == MessageAndMilestoneType.NewStuff)
			{
				return CurrentNewStuff.Any((msg) => msg.MessageType == messageType && msg.L10NId == l10nId && msg.Param0 == param0 && msg.Param1 == param1);
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
		//	using (var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read))
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

		public Tuple<MessageAndMilestoneType, String>[] PrettyPrintMessages
		{
			get { return Messages.Select(m => Tuple.Create(m.MessageType, m.PrettyPrint)).Where(t => !String.IsNullOrEmpty(t.Item2)).ToArray(); }
		}
	}
}

// Todo (in other cards now)
// - clobber-pending stuff: detect, toast, reload with dialog.
//		(Review: do we even need clobber-pending in the log now that it's not a possible state of the status button?)
// - actual icons (and restore TC label)...later task
